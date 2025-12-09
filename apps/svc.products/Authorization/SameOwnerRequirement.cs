using Microsoft.AspNetCore.Authorization;
using svc.products.Extensions;

namespace svc.products.Authorization;

public sealed record SameOwnerRequirement : IAuthorizationRequirement
{
    public static SameOwnerRequirement Instance { get; } = new();
}

public sealed class SameOwnerAuthorizationHandler : AuthorizationHandler<SameOwnerRequirement, Guid>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SameOwnerRequirement requirement, Guid resourceOwnerId)
    {
        if (context.User.TryGetOwnerId(out var ownerId) && ownerId == resourceOwnerId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
