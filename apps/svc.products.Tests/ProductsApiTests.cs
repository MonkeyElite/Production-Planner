using System.Net;
using System.Net.Http.Json;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using svc.products.Dtos;
using Xunit;

namespace svc.products.Tests;

public class ProductsApiTests : IClassFixture<ProductsApiFactory>
{
    private readonly ProductsApiFactory _factory;

    public ProductsApiTests(ProductsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Large_payload_is_rejected()
    {
        var client = _factory.CreateClient();

        var oversized = new string('x', (int)ProductsApiFactory.RequestBodyLimit + 1024);
        var response = await client.PostAsJsonAsync("/api/products", new
        {
            name = "Big payload",
            description = oversized,
            price = 15.5m
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Errors.Should().ContainKey("Description");
    }

    [Fact]
    public void Rate_limiting_is_configured_globally()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<RateLimiterOptions>>();

        options.Value.GlobalLimiter.Should().NotBeNull();
        options.Value.RejectionStatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public async Task Unhandled_exception_returns_problem_details()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll(typeof(IValidator<ProductCreateDto>));
                services.AddSingleton<IValidator<ProductCreateDto>>(new ThrowingValidator<ProductCreateDto>());
            });
        }).CreateClient();

        var response = await client.PostAsJsonAsync("/api/products", new
        {
            name = "trigger",
            description = "boom",
            price = 1.0m
        });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Detail.Should().Be("An unexpected error occurred while processing the request.");
        problem.Extensions.Should().ContainKey("traceId");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("ThrowingValidator");
    }

    [Fact]
    public async Task Products_read_policy_requires_scope()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Scopes", "orders.read");

        var response = await client.GetAsync("/api/products");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Products_write_policy_requires_planner_role()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Roles", "viewer");

        var response = await client.PostAsJsonAsync("/api/products", new
        {
            name = "No planner",
            description = "role check",
            price = 12.5m
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Product_create_rejects_missing_name()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/products", new
        {
            name = "",
            description = "invalid",
            price = 5.0m
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Errors.Should().ContainKey("Name");
    }

    [Fact]
    public async Task Product_create_rejects_negative_price()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/products", new
        {
            name = "Bad price",
            description = "invalid",
            price = -1.0m
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Errors.Should().ContainKey("Price");
    }
}

public class ThrowingValidator<T> : AbstractValidator<T>
{
    public override Task<FluentValidation.Results.ValidationResult> ValidateAsync(
        FluentValidation.ValidationContext<T> context,
        CancellationToken cancellation = default)
    {
        throw new InvalidOperationException("Test failure");
    }

    public override FluentValidation.Results.ValidationResult Validate(FluentValidation.ValidationContext<T> context)
    {
        throw new InvalidOperationException("Test failure");
    }
}
