namespace svc.products.Models
{
    public sealed class Product
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid OwnerId { get; set; }
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public uint Version { get; private set; } // concurrency
        public ICollection<ProductionLineProduct> ProductionLineProducts { get; set; } = new List<ProductionLineProduct>();
    }
}
