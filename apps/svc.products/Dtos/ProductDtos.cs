namespace svc.products.Dtos
{
    public sealed record ProductCreateDto(string Sku, string Name, string? Description, decimal Price);
    public sealed record ProductUpdateDto(string Name, string? Description, decimal Price, bool IsActive, string? IfMatch);
    public sealed record ProductView(Guid Id, string Sku, string Name, string? Description, decimal Price, bool IsActive);
}
