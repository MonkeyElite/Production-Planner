using Microsoft.EntityFrameworkCore;
using svc.products.Models;

namespace svc.products.Data
{
    public sealed class ProductsDb(DbContextOptions<ProductsDb> options) : DbContext(options)
    {
        public DbSet<Product> Products => Set<Product>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Product>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.Sku).IsUnique();
                e.Property(x => x.Sku).HasMaxLength(64).IsRequired();
                e.Property(x => x.Name).HasMaxLength(200).IsRequired();
                e.Property(x => x.Price).HasPrecision(12, 2);
                e.Property(p => p.Version)
                 .HasColumnName("xmin")
                 .IsRowVersion();
            });
        }
    }
}
