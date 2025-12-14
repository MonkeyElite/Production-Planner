using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using svc.products.Dtos;

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

        response.StatusCode.Should().BeOneOf(HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.BadRequest);
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
