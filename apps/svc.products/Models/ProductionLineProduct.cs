namespace svc.products.Models;

public sealed class ProductionLineProduct
{
    public Guid ProductionLineId { get; set; }
    public ProductionLine ProductionLine { get; set; } = default!;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = default!;
}
