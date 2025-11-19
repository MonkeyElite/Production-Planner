using System.Security.Authentication;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using svc.products.Data;
using svc.products.Validation;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Load secrets (Jwt__Authority, Jwt__Audience, connection string, etc.)
        builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

        // Bind strongly-typed settings
        var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
        var mutualTlsOptions = builder.Configuration.GetSection("MutualTls").Get<MutualTlsOptions>();

        // Database
        builder.Services.AddDbContext<ProductsDb>(opt =>
            opt.UseNpgsql(builder.Configuration.GetConnectionString("ProductsDb")));

        // AuthN
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = jwtSettings.Authority;
                options.Audience = jwtSettings.Audience;

                options.RequireHttpsMetadata = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2)
                };

                // Correct way to get logger factory
                var provider = builder.Services.BuildServiceProvider();
                var logger = provider.GetRequiredService<ILogger<Program>>();

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = ctx =>
                    {
                        logger.LogError(ctx.Exception,
                            "JWT auth failed. Authority={Authority}, Audience={Audience}",
                            options.Authority, options.Audience);

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = ctx =>
                    {
                        var scopes = string.Join(" ",
                            ctx.Principal?
                                .FindAll("scope")
                                .Select(c => c.Value));

                        logger.LogInformation(
                            "JWT validated. Sub={Sub}, Scope={Scope}",
                            ctx.Principal?.FindFirst("sub")?.Value,
                            scopes);

                        return Task.CompletedTask;
                    }
                };
            });

        // AuthZ
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
                policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()!)
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            });
        });

        // Controllers + validation
        builder.Services.AddControllers().AddNewtonsoftJson();
        builder.Services.AddValidatorsFromAssemblyContaining<ProductCreateValidator>();

        // Swagger (dev only)
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // Kestrel + Mutual TLS for inbound HTTPS (gateway → products-api)
        builder.WebHost.ConfigureKestrel(options =>
        {
            X509Certificate2? clientCa = null;

            if (!string.IsNullOrWhiteSpace(mutualTlsOptions?.ClientCaPath) &&
                File.Exists(mutualTlsOptions.ClientCaPath))
            {
                clientCa = new X509Certificate2(mutualTlsOptions.ClientCaPath);
            }

            options.ConfigureHttpsDefaults(httpsOptions =>
            {
                httpsOptions.SslProtocols = SslProtocols.Tls13;
                httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;

                if (clientCa is not null)
                {
                    httpsOptions.ClientCertificateValidation = (certificate, chain, errors) =>
                    {
                        if (certificate is null)
                        {
                            return false;
                        }

                        chain ??= new X509Chain();
                        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                        chain.ChainPolicy.CustomTrustStore.Clear();
                        chain.ChainPolicy.CustomTrustStore.Add(clientCa);
                        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

                        var subjectMatches = string.IsNullOrWhiteSpace(mutualTlsOptions?.ClientCertificateSubject)
                            || string.Equals(certificate.Subject, mutualTlsOptions.ClientCertificateSubject,
                                StringComparison.OrdinalIgnoreCase);

                        return subjectMatches && chain.Build(certificate);
                    };
                }
            });
        });

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        app.UseCors();

        app.UseAuthentication();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.MapControllers();

        // DB migrations with retry
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

                    db.Database.Migrate();
                    logger.LogInformation("Database migrated successfully.");
                    break;
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
        const string plannerRole = "planner";
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

public class JwtSettings
{
    public string Authority { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
}

public class MutualTlsOptions
{
    public string? ClientCertificateSubject { get; set; }
    public string? ClientCaPath { get; set; }
}
