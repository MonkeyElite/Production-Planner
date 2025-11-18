using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Serilog;
using System.Linq;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
  .AddJwtBearer(o =>
  {
      o.Authority = builder.Configuration["Jwt:Authority"];
      o.Audience = builder.Configuration["Jwt:Audience"];

      o.RequireHttpsMetadata = false;
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
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    ctx.Response.Headers.XFrameOptions = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

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
