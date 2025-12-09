using System.Security.Claims;

namespace svc.products.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static bool TryGetOwnerId(this ClaimsPrincipal principal, out Guid ownerId)
    {
        ownerId = Guid.Empty;

        // 1. Prefer a dedicated owner/tenant claim if you add one later
        var candidate =
            principal.FindFirst("owner_id")?.Value ??
            principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? // mapped from "sub" by default
            principal.FindFirst("sub")?.Value;                       // raw "sub" if mapping is disabled

        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        return Guid.TryParse(candidate, out ownerId);
    }

    public static Guid GetRequiredOwnerId(this ClaimsPrincipal principal)
    {
        if (principal.TryGetOwnerId(out var ownerId))
        {
            return ownerId;
        }

        throw new InvalidOperationException("Authenticated principal is missing a valid subject identifier.");
    }
}
