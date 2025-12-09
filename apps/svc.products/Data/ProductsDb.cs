using Microsoft.EntityFrameworkCore;
using svc.products.Models;

namespace svc.products.Data
{
    public sealed class ProductsDb(DbContextOptions<ProductsDb> options) : DbContext(options)
    {
        public DbSet<Product> Products => Set<Product>();
        public DbSet<ProductionLine> ProductionLines => Set<ProductionLine>();
        public DbSet<ProductionLineProduct> ProductionLineProducts => Set<ProductionLineProduct>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Product>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.OwnerId).IsRequired();
                e.HasIndex(x => new { x.OwnerId, x.Name }).IsUnique();
                e.Property(x => x.Name).HasMaxLength(200).IsRequired();
                e.Property(x => x.Price).HasPrecision(12, 2);
                e.Property(p => p.Version)
                 .HasColumnName("xmin")
                 .IsRowVersion();
            });

            b.Entity<ProductionLine>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.OwnerId).IsRequired();
                e.Property(x => x.Name).HasMaxLength(200).IsRequired();
                e.Property(x => x.ShiftSchedule).HasMaxLength(200).IsRequired();
                e.Property(x => x.CapacityPerShift).IsRequired();
                e.HasIndex(x => new { x.OwnerId, x.Name }).IsUnique();
            });

            b.Entity<ProductionLineProduct>(e =>
            {
                e.HasKey(x => new { x.ProductionLineId, x.ProductId });
                e.HasOne(x => x.ProductionLine)
                    .WithMany(x => x.ProductionLineProducts)
                    .HasForeignKey(x => x.ProductionLineId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Product)
                    .WithMany(x => x.ProductionLineProducts)
                    .HasForeignKey(x => x.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
