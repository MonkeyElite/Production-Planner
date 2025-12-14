using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using svc.products.Authorization;
using svc.products.Data;
using svc.products.Validation;
using Microsoft.Extensions.Configuration;
using System;
using System.Security.Claims;
using System.Linq;
using Microsoft.IdentityModel.Tokens;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureKestrelForTls(builder);

        // Database
        builder.Services.AddDbContext<ProductsDb>(opt =>
            opt.UseNpgsql(builder.Configuration.GetConnectionString("ProductsDb")));

        // AuthN/Z
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = ResolveAuthority(builder.Configuration, builder.Environment);
                o.Audience = builder.Configuration["Jwt:Audience"];
                o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"],
                    ValidIssuers = builder.Configuration.GetSection("Jwt:ValidIssuers").Get<string[]>() ?? Array.Empty<string>(),
                    ValidateAudience = true,
                    ValidAudience = builder.Configuration["Jwt:Audience"],
                    ValidAudiences = builder.Configuration.GetSection("Jwt:Audiences").Get<string[]>() ?? Array.Empty<string>(),
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    RoleClaimType = "roles",
                    ClockSkew = TimeSpan.FromMinutes(builder.Configuration.GetValue<int>("Jwt:ClockSkewMinutes", 1)),
                    ValidAlgorithms = builder.Configuration.GetSection("Jwt:Algorithms").Get<string[]>() ?? Array.Empty<string>()
                };
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
                        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();

                        var scopes = string.Join(" ", ctx.Principal?.FindAll("scope").Select(c => c.Value) ?? Array.Empty<string>());
                        var roles = string.Join(" ", ctx.Principal?.FindAll("roles").Select(c => c.Value) ?? Array.Empty<string>());

                        logger.LogInformation("JWT validated. Sub={Sub}, Scope={Scope}, Roles={Roles}",
                            ctx.Principal?.FindFirst("sub")?.Value,
                            scopes,
                            roles
                        );

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
                    HasPlannerRole(ctx.User));
                    // HasScope(ctx.User, "products.write") && HasPlannerRole(ctx.User));
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

    private static void ConfigureKestrelForTls(WebApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment())
        {
            return;
        }

        var httpsEndpoint = builder.Configuration.GetSection("Kestrel:Endpoints:Https");
        var certificateSection = httpsEndpoint.GetSection("Certificate");

        if (!httpsEndpoint.Exists() || !certificateSection.Exists())
        {
            throw new InvalidOperationException("HTTPS endpoint and certificate must be configured for non-development environments.");
        }

        builder.WebHost.ConfigureKestrel((context, options) =>
        {
            options.Configure(context.Configuration.GetSection("Kestrel"));
        });
    }

    private static string ResolveAuthority(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var authority = configuration["Jwt:Authority"];

        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new InvalidOperationException("JWT authority configuration is missing.");
        }

        if (!environment.IsDevelopment() && authority.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("JWT authority must use HTTPS in non-development environments.");
        }

        return authority;
    }

    private static bool HasScope(ClaimsPrincipal user, string requiredScope)
    {
        return user.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Any(scope => string.Equals(scope, requiredScope, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasPlannerRole(ClaimsPrincipal user)
    {
        return user.IsInRole("planner")
            || user.FindAll("roles").Any(c => string.Equals(c.Value, "planner", StringComparison.OrdinalIgnoreCase))
            || user.FindAll("role").Any(c => string.Equals(c.Value, "planner", StringComparison.OrdinalIgnoreCase));
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