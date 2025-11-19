using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Serilog;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Security.Authentication;
using System.IO;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
  .AddJwtBearer();


builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>>(sp =>
{
    var jwtMonitor = sp.GetRequiredService<IOptionsMonitor<JwtSettings>>();

    return new ConfigureNamedOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        void Apply(JwtSettings current)
        {
            options.Authority = current.Authority;
            options.Audience = current.Audience;

            options.TokenValidationParameters.ValidIssuer = current.Authority;
            options.TokenValidationParameters.ValidateIssuer = true;

            options.TokenValidationParameters.ValidAudience = current.Audience;
            options.TokenValidationParameters.ValidateAudience = true;
        }

        Apply(jwtMonitor.CurrentValue);
        jwtMonitor.OnChange(Apply);

        options.RequireHttpsMetadata = false;
    });
});

var clientCertForwardingSection = builder.Configuration.GetSection("ClientCertificateForwarding");
builder.Services.AddCertificateForwarding(options =>
{
    options.CertificateHeader = clientCertForwardingSection["HeaderName"] ?? "X-Client-Cert";
    options.HeaderConverter = headerValue =>
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(headerValue);
            return new X509Certificate2(bytes);
        }
        catch
        {
            return null;
        }
    };
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ApiUser", p => p.RequireAuthenticatedUser().RequireClaim("scope", "api"))
    .AddPolicy("PlannerOnly", p => p.RequireAuthenticatedUser().RequireClaim("role", "planner"));

builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>())
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("Standard", opts =>
    {
        opts.Window = TimeSpan.FromSeconds(1);
        opts.PermitLimit = 20;
        opts.QueueLimit = 0;
    });
    o.AddFixedWindowLimiter("Strict", opts =>
    {
        opts.Window = TimeSpan.FromSeconds(1);
        opts.PermitLimit = 5;
        opts.QueueLimit = 0;
    });
});

var reverseProxySection = builder.Configuration.GetSection("ReverseProxy");
builder.Services.AddReverseProxy()
    .LoadFromConfig(reverseProxySection);
    //.ConfigureHttpClient((context, handler) =>
    //{
    //    if (!string.Equals(context.ClusterId, "products", StringComparison.OrdinalIgnoreCase))
    //    {
    //        return;
    //    }

    //    handler.SslOptions ??= new SslClientAuthenticationOptions();
    //    handler.SslOptions.EnabledSslProtocols = SslProtocols.Tls13;

    //    var clientCert = LoadCertificate(reverseProxySection.GetSection("ClientCertificates:GatewayToProducts"));
    //    if (clientCert is not null)
    //    {
    //        handler.SslOptions.ClientCertificates ??= new X509CertificateCollection();
    //        handler.SslOptions.ClientCertificates.Add(clientCert);
    //    }

    //    var backendCa = LoadCertificate(reverseProxySection.GetSection("ClientCertificates:BackendCa"));
    //    if (backendCa is not null)
    //    {
    //        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
    //        {
    //            if (certificate is null)
    //            {
    //                return false;
    //            }

    //            chain ??= new X509Chain();
    //            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
    //            chain.ChainPolicy.CustomTrustStore.Clear();
    //            chain.ChainPolicy.CustomTrustStore.Add(backendCa);
    //            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
    //            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

    //            var isValid = chain.Build((X509Certificate2)certificate);
    //            return isValid && errors == SslPolicyErrors.None;
    //        };
    //    }
    //});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    ctx.Response.Headers.XFrameOptions = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseCertificateForwarding();

var trustedSubjects = clientCertForwardingSection.GetSection("AllowedSubjects").Get<string[]>() ?? Array.Empty<string>();
if (trustedSubjects.Length > 0)
{
    app.Use(async (ctx, next) =>
    {
        var forwardedCert = ctx.Connection.ClientCertificate;
        if (forwardedCert is not null)
        {
            var isAllowed = trustedSubjects.Any(subject =>
                string.Equals(subject, forwardedCert.Subject, StringComparison.OrdinalIgnoreCase));

            if (!isAllowed)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync("Untrusted client certificate.");
                return;
            }
        }

        await next();
    });
}

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.UseWhen(ctx => RequiresStepUp(ctx), branch =>
{
    branch.Use(async (ctx, next) =>
    {
        if (ctx.User?.Identity?.IsAuthenticated != true)
        {
            await ctx.ChallengeAsync();
            return;
        }

        if (!HasStepUpClaims(ctx.User))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsync("Step-up MFA required for this route.");
            return;
        }

        await next();
    });
});

app.UseRateLimiter();

app.UseForwardedHeaders();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapReverseProxy();

app.Run();

static bool RequiresStepUp(HttpContext context)
{
    if (!(HttpMethods.IsPost(context.Request.Method)
        || HttpMethods.IsPut(context.Request.Method)
        || HttpMethods.IsDelete(context.Request.Method)))
    {
        return false;
    }

    return context.Request.Path.StartsWithSegments("/api/products", out _);
}

static bool HasStepUpClaims(ClaimsPrincipal user)
{
    if (user is null)
    {
        return false;
    }

    var amrMatches = user.FindAll("amr")
        .Select(c => c.Value)
        .Any(v => string.Equals(v, "mfa", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "otp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "hwk", StringComparison.OrdinalIgnoreCase));

    if (amrMatches)
    {
        return true;
    }

    var acr = user.FindFirst("acr")?.Value;
    return acr is not null && string.Equals(acr, "urn:mfa", StringComparison.OrdinalIgnoreCase);
}

static X509Certificate2? LoadCertificate(IConfiguration section)
{
    var path = section?["Path"];
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
        return null;
    }

    var password = section?["Password"] ?? string.Empty;
    return new X509Certificate2(path, password,
        X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.EphemeralKeySet);
}

public class JwtSettings
{
    public string Authority { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
}
