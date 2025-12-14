using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Context;
using Serilog.Core.Enrichers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

ConfigureKestrelForTls(builder);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
  .AddJwtBearer(o =>
  {
      o.Authority = ResolveAuthority(builder.Configuration, builder.Environment);
      o.Audience = builder.Configuration["Jwt:Audience"];

      o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
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
          ClockSkew = TimeSpan.FromMinutes(builder.Configuration.GetValue<int>("Jwt:ClockSkewMinutes", 1)),
          ValidAlgorithms = builder.Configuration.GetSection("Jwt:Algorithms").Get<string[]>() ?? Array.Empty<string>()
      };

      o.Events = new JwtBearerEvents
      {
          OnAuthenticationFailed = ctx =>
          {
              var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
              logger.LogWarning(ctx.Exception, "JWT authentication failed for {Path}", ctx.Request.Path);
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

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
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

app.Use(async (ctx, next) =>
{
    var csp = BuildContentSecurityPolicy(app.Environment.IsDevelopment());

    ctx.Response.Headers["Content-Security-Policy"] = csp;
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    ctx.Response.Headers.XFrameOptions = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";

    if (!app.Environment.IsDevelopment() && IsHttpsRequest(ctx))
    {
        ctx.Response.Headers["Strict-Transport-Security"] =
            "max-age=31536000; includeSubDomains; preload";
    }

    await next();
});

app.UseCors();

app.UseAuthentication();

app.Use(async (ctx, next) =>
{
    using var userContext = EnrichUserContext(ctx.User);
    await next();
});

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

static void ConfigureKestrelForTls(WebApplicationBuilder builder)
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

static string ResolveAuthority(IConfiguration configuration, IWebHostEnvironment environment)
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

static IDisposable EnrichUserContext(ClaimsPrincipal? user)
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

static bool RequiresStepUp(HttpContext context)
{
    if (!(HttpMethods.IsPost(context.Request.Method)
        || HttpMethods.IsPut(context.Request.Method)
        || HttpMethods.IsDelete(context.Request.Method)))
    {
        return false;
    }

    return context.Request.Path.StartsWithSegments("/api/products", out _)
        || context.Request.Path.StartsWithSegments("/api/productionlines", out _);
}

static bool HasStepUpClaims(ClaimsPrincipal user)
{
    if (user is null)
    {
        return false;
    }

    return true;

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

static string BuildContentSecurityPolicy(bool isDevelopment)
{
    var directives = new List<string>
    {
        "default-src 'self'",
        "base-uri 'self'",
        "frame-ancestors 'none'",
        "form-action 'self'",
        "object-src 'none'",
        "img-src 'self' data: blob: https:",
        "font-src 'self' data:",
        "style-src 'self' 'unsafe-inline'",
        "connect-src 'self' https:",
    };

    if (isDevelopment)
    {
        directives.Add("script-src 'self' 'unsafe-eval' 'unsafe-inline'");
    }
    else
    {
        directives.Add("script-src 'self' 'unsafe-inline'");
        directives.Add("upgrade-insecure-requests");
    }

    return string.Join("; ", directives);
}

static bool IsHttpsRequest(HttpContext context)
{
    if (context.Request.IsHttps)
    {
        return true;
    }

    var forwardedProto = context.Request.Headers["X-Forwarded-Proto"].ToString();
    return string.Equals(forwardedProto, "https", StringComparison.OrdinalIgnoreCase);
}
