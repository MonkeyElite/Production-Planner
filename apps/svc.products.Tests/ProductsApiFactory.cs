using System.Security.Claims;
using System.Linq;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using svc.products.Data;

namespace svc.products.Tests;

public class ProductsApiFactory : WebApplicationFactory<Program>
{
    public const long RequestBodyLimit = Program.MaxRequestBodySizeBytes;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((context, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Limits:MaxRequestBodySizeBytes"] = RequestBodyLimit.ToString(),
                ["RateLimiting:PermitLimit"] = "50",
                ["RateLimiting:WindowSeconds"] = "60",
                ["RateLimiting:QueueLimit"] = "0"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<ProductsDb>));
            services.RemoveAll(typeof(ProductsDb));
            services.AddDbContext<ProductsDb>(options =>
                options.UseInMemoryDatabase($"ProductsTests_{Guid.NewGuid()}")
            );

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }
}

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock) : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.TryGetValue("X-Test-Anonymous", out var anonymous) &&
            anonymous.Any(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var subject = Request.Headers.TryGetValue("X-Test-Subject", out var subjectHeader)
            ? subjectHeader.FirstOrDefault()
            : null;

        var scopes = Request.Headers.TryGetValue("X-Test-Scopes", out var scopesHeader)
            ? scopesHeader.FirstOrDefault()
            : "products.read products.write";

        var roles = Request.Headers.TryGetValue("X-Test-Roles", out var rolesHeader)
            ? rolesHeader.FirstOrDefault()
            : "planner";

        var claims = new[]
        {
            new Claim("sub", subject ?? UserId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, subject ?? UserId.ToString()),
            new Claim("scope", scopes ?? string.Empty),
            new Claim("roles", roles ?? string.Empty)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.NameIdentifier, "roles");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
