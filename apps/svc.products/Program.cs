using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using svc.products.Authorization;
using svc.products.Data;
using svc.products.Middleware;
using svc.products.Validation;
using Microsoft.Extensions.Configuration;
using System;
using System.Security.Claims;
using System.Linq;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Context;
using Serilog.Core.Enrichers;
using System.Threading.RateLimiting;

public partial class Program
{
    public const long MaxRequestBodySizeBytes = 1 * 1024 * 1024; // 1 MB

    private const int DefaultRateLimitPermitLimit = 60;
    private const int DefaultRateLimitWindowSeconds = 60;
    private const int DefaultRateLimitQueueLimit = 0;

    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

        ConfigureKestrel(builder, MaxRequestBodySizeBytes);
        var maxRequestBodySizeBytes = builder.Configuration.GetValue<long?>("Limits:MaxRequestBodySizeBytes")
            ?? MaxRequestBodySizeBytes;

        var rateLimitPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:PermitLimit")
            ?? DefaultRateLimitPermitLimit;
        var rateLimitWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:WindowSeconds")
            ?? DefaultRateLimitWindowSeconds;
        var rateLimitQueueLimit = builder.Configuration.GetValue<int?>("RateLimiting:QueueLimit")
            ?? DefaultRateLimitQueueLimit;

        ConfigureKestrel(builder, maxRequestBodySizeBytes);

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
                    },
                    OnForbidden = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                        logger.LogWarning("Authorization denied for {Path}. User={User}", ctx.Request.Path, ctx.Principal?.FindFirst("sub")?.Value ?? "anonymous");
                        return Task.CompletedTask;
                    },
                    OnChallenge = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                        logger.LogWarning("Authentication challenge for {Path}", ctx.Request.Path);
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
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var partitionKey = ResolveRateLimitPartitionKey(context);
                return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
                {
                    AutoReplenishment = true,
                    TokenLimit = rateLimitPermitLimit,
                    TokensPerPeriod = rateLimitPermitLimit,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(rateLimitWindowSeconds),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = rateLimitQueueLimit
                });
            });
        });

        builder.Services.AddControllers(options =>
        {
            options.Filters.Add(new RequestSizeLimitAttribute(maxRequestBodySizeBytes));
        }).AddNewtonsoftJson();
        builder.Services.AddValidatorsFromAssemblyContaining<ProductCreateValidator>();
        builder.Services.AddSingleton<IAuthorizationHandler, SameOwnerAuthorizationHandler>();

        // Swagger (dev only)
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        app.UseMiddleware<ErrorHandlingMiddleware>();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        app.Use(async (ctx, next) =>
        {
            const string headerName = "X-Correlation-ID";

            var correlationId = ctx.Request.Headers[headerName].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(correlationId))
            {
                correlationId = Guid.NewGuid().ToString("N");
            }

            ctx.Items[headerName] = correlationId;
            ctx.Response.Headers[headerName] = correlationId;

            using (LogContext.PushProperty("CorrelationId", correlationId))
            {
                await next();
            }
        });

        app.UseSerilogRequestLogging();

        app.UseAuthentication();
        app.Use(async (ctx, next) =>
        {
            using var userContext = EnrichUserContext(ctx.User);
            await next();
        });
        app.UseRateLimiter();
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

    private static void ConfigureKestrel(WebApplicationBuilder builder, long maxRequestBodySizeBytes)
    {
        builder.WebHost.ConfigureKestrel((context, options) =>
        {
            if (!context.HostingEnvironment.IsDevelopment())
            {
                var httpsEndpoint = context.Configuration.GetSection("Kestrel:Endpoints:Https");
                var certificateSection = httpsEndpoint.GetSection("Certificate");

                if (!httpsEndpoint.Exists() || !certificateSection.Exists())
                {
                    throw new InvalidOperationException("HTTPS endpoint and certificate must be configured for non-development environments.");
                }
            }

            options.Configure(context.Configuration.GetSection("Kestrel"));
            options.Limits.MaxRequestBodySize = maxRequestBodySizeBytes;
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

    private static string ResolveRateLimitPartitionKey(HttpContext context)
    {
        var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User?.FindFirstValue("sub");

        if (!string.IsNullOrWhiteSpace(userId))
        {
            return $"user:{userId}";
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            return $"ip:{remoteIp}";
        }

        return "anonymous";
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

    private static IDisposable EnrichUserContext(ClaimsPrincipal? user)
    {
        var subject = user?.FindFirst("sub")?.Value
                     ?? user?.Identity?.Name
                     ?? "anonymous";

        var scopes = string.Join(" ", user?.FindAll("scope").Select(c => c.Value) ?? Array.Empty<string>());
        var roles = string.Join(" ",
            (user?.FindAll("roles").Select(c => c.Value) ?? Enumerable.Empty<string>())
                .Concat(user?.FindAll("role").Select(c => c.Value) ?? Enumerable.Empty<string>()));

        return LogContext.Push(
            new PropertyEnricher("UserSubject", subject),
            new PropertyEnricher("UserScopes", scopes),
            new PropertyEnricher("UserRoles", roles)
        );
    }
}
