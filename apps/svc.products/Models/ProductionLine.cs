namespace svc.products.Models;

public sealed class ProductionLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public int CapacityPerShift { get; set; }
    public string ShiftSchedule { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ProductionLineProduct> ProductionLineProducts { get; set; } = new List<ProductionLineProduct>();
}
