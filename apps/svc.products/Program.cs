using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using svc.products.Authorization;
using svc.products.Data;
using svc.products.Validation;
using System.Security.Claims;
using System.Linq;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Database
        builder.Services.AddDbContext<ProductsDb>(opt =>
            opt.UseNpgsql(builder.Configuration.GetConnectionString("ProductsDb")));

        // AuthN/Z
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = builder.Configuration["Jwt:Authority"];
                o.Audience = builder.Configuration["Jwt:Audience"];
                o.RequireHttpsMetadata = false;
                o.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices
                            .GetRequiredService<ILogger<Program>>();
                        logger.LogError(ctx.Exception,
                            "JWT auth failed. Authority={Authority}, Audience={Audience}",
                            o.Authority, o.Audience);
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices
                            .GetRequiredService<ILogger<Program>>();
                        var scopes = string.Join(" ", ctx.Principal?
                            .FindAll("scope")
                            .Select(c => c.Value));
                        logger.LogInformation("JWT validated. Sub={Sub}, Scope={Scope}",
                            ctx.Principal?.FindFirst("sub")?.Value, scopes);
                        return Task.CompletedTask;
                    }
                };

            });

        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("products:read", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx => HasScope(ctx.User, "products.read"));
            });

            o.AddPolicy("products:write", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx =>
                    HasScope(ctx.User, "products.write") && HasPlannerRole(ctx.User));
            });

            o.AddPolicy("mfa", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx => HasStrongAuthentication(ctx.User));
            });
        });

        // CORS
        builder.Services.AddCors(o =>
        {
            o.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>())
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            });
        });

        // Validation
        builder.Services.AddControllers().AddNewtonsoftJson();
        builder.Services.AddValidatorsFromAssemblyContaining<ProductCreateValidator>();
        builder.Services.AddSingleton<IAuthorizationHandler, SameOwnerAuthorizationHandler>();

        // Swagger (dev only)
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.MapControllers();

        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var logger = services.GetRequiredService<ILogger<Program>>();
            var db = services.GetRequiredService<ProductsDb>();

            const int maxAttempts = 10;
            var delay = TimeSpan.FromSeconds(3);

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var pending = db.Database.GetPendingMigrations().ToList();
                    logger.LogInformation(
                        "Attempt {Attempt}: pending migrations {Count} - {Names}",
                        attempt,
                        pending.Count,
                        string.Join(", ", pending)
                    );

                    db.Database.Migrate(); // <-- sync, actually blocks until done

                    logger.LogInformation("Database migrated successfully.");
                    break; // success, leave loop
                }
                catch (Npgsql.NpgsqlException ex) when (attempt < maxAttempts)
                {
                    logger.LogWarning(
                        ex,
                        "Database not ready yet. Attempt {Attempt}/{MaxAttempts}. Retrying in {Delay}s...",
                        attempt,
                        maxAttempts,
                        delay.TotalSeconds
                    );
                    Thread.Sleep(delay);
                }
                catch (System.Net.Sockets.SocketException ex) when (attempt < maxAttempts)
                {
                    logger.LogWarning(
                        ex,
                        "Socket error while connecting to DB. Attempt {Attempt}/{MaxAttempts}. Retrying in {Delay}s...",
                        attempt,
                        maxAttempts,
                        delay.TotalSeconds
                    );
                    Thread.Sleep(delay);
                }
            }
        }


        app.Run();
    }

    private static bool HasScope(ClaimsPrincipal user, string requiredScope)
    {
        return user.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Any(scope => string.Equals(scope, requiredScope, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasPlannerRole(ClaimsPrincipal user)
    {
        var plannerRole = "planner";
        return user.FindAll(ClaimTypes.Role).Concat(user.FindAll("role"))
            .Any(c => string.Equals(c.Value, plannerRole, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasStrongAuthentication(ClaimsPrincipal user)
    {
        var amrValues = user.FindAll("amr").Select(c => c.Value);
        var acr = user.FindFirst("acr")?.Value;

        var accepted = new[] { "mfa", "otp", "hwk" };

        return amrValues.Any(v => accepted.Any(a => string.Equals(a, v, StringComparison.OrdinalIgnoreCase)))
            || (acr is not null && string.Equals(acr, "urn:mfa", StringComparison.OrdinalIgnoreCase));
    }
}