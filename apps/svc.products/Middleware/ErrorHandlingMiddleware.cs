using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace svc.products.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

            _logger.LogError(ex,
                "Unhandled exception processing {Method} {Path}. TraceId={TraceId}",
                context.Request.Method,
                context.Request.Path,
                traceId);

            if (context.Response.HasStarted)
            {
                throw;
            }

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = "An unexpected error occurred while processing the request.",
                Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1"
            };
            problem.Extensions["traceId"] = traceId;

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}
