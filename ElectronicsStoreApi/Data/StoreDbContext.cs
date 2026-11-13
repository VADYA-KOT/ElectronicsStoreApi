using ElectronicsStoreApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ElectronicsStoreApi.Data;

public class StoreDbContext : DbContext
{
    public DbSet<AppUser> AppUsers => Set<AppUser>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<CategorySpecification> CategorySpecifications =>
        Set<CategorySpecification>();

    public DbSet<ProductSpecification> ProductSpecifications =>
        Set<ProductSpecification>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public StoreDbContext(
        DbContextOptions<StoreDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Категории.
        var category = modelBuilder.Entity<Category>();

        category.ToTable("categories");

        category.HasKey(c => c.Id);

        category.Property(c => c.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        category.Property(c => c.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        category.Property(c => c.ImageUrl)
            .HasColumnName("image_url")
            .HasMaxLength(500);

        category.HasIndex(c => c.Name)
            .IsUnique();

        // Товары.
        var product = modelBuilder.Entity<Product>();

        product.ToTable("products", table =>
        {
            table.HasCheckConstraint(
                "CK_products_price_positive",
                "price > 0");

            table.HasCheckConstraint(
                "CK_products_quantity_nonnegative",
                "quantity >= 0");

            table.HasCheckConstraint(
                "CK_products_old_price_greater_than_price",
                "old_price IS NULL OR old_price > price");
        });

        product.HasKey(p => p.Id);

        product.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        product.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        product.Property(p => p.Price)
            .HasColumnName("price")
            .HasPrecision(10, 2);

        product.Property(p => p.OldPrice)
            .HasColumnName("old_price")
            .HasPrecision(10, 2);

        product.Property(p => p.ImageUrl)
            .HasColumnName("image_url")
            .HasMaxLength(500);

        product.HasIndex(p => p.Price);

        product.Property(p => p.Quantity)
            .HasColumnName("quantity");

        product.Property(p => p.CategoryId)
            .HasColumnName("category_id");

        product.Property(p => p.Description)
            .HasColumnName("description");

        product.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId);

        // Справочник характеристик категорий.
        var categorySpecification =
            modelBuilder.Entity<CategorySpecification>();

        categorySpecification.ToTable("category_specifications", table =>
        {
            table.HasCheckConstraint(
                "CK_category_specifications_sort_order_nonnegative",
                "sort_order >= 0");
        });

        categorySpecification.HasKey(s => s.Id);

        categorySpecification.Property(s => s.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        categorySpecification.Property(s => s.CategoryId)
            .HasColumnName("category_id")
            .IsRequired();

        categorySpecification.Property(s => s.Code)
            .HasColumnName("code")
            .HasMaxLength(50)
            .IsRequired();

        categorySpecification.Property(s => s.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        categorySpecification.Property(s => s.Unit)
            .HasColumnName("unit")
            .HasMaxLength(30);

        categorySpecification.Property(s => s.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        categorySpecification.HasIndex(s => new
        {
            s.CategoryId,
            s.Code
        })
        .IsUnique();

        categorySpecification.HasOne(s => s.Category)
            .WithMany(c => c.Specifications)
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // Значения характеристик конкретных товаров.
        var productSpecification =
            modelBuilder.Entity<ProductSpecification>();

        productSpecification.ToTable("product_specifications");

        productSpecification.HasKey(s => s.Id);

        productSpecification.Property(s => s.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        productSpecification.Property(s => s.ProductId)
            .HasColumnName("product_id")
            .IsRequired();

        productSpecification.Property(s => s.CategorySpecificationId)
            .HasColumnName("category_specification_id")
            .IsRequired();

        productSpecification.Property(s => s.Value)
            .HasColumnName("value")
            .HasMaxLength(500)
            .IsRequired();

        // У одного товара не может быть двух одинаковых характеристик.
        productSpecification.HasIndex(s => new
        {
            s.ProductId,
            s.CategorySpecificationId
        })
        .IsUnique();

        // При удалении товара удаляются его значения характеристик.
        productSpecification.HasOne(s => s.Product)
            .WithMany(p => p.Specifications)
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // При удалении определения характеристики удаляются её значения.
        productSpecification.HasOne(s => s.CategorySpecification)
            .WithMany(s => s.ProductSpecifications)
            .HasForeignKey(s => s.CategorySpecificationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Пользователи.
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(u => u.Id);

            entity.Property(u => u.Username)
                .HasMaxLength(40)
                .IsRequired();

            entity.Property(u => u.NormalizedUsername)
                .HasMaxLength(40)
                .IsRequired();

            entity.Property(u => u.PasswordHash)
                .HasMaxLength(512)
                .IsRequired();

            entity.HasIndex(u => u.NormalizedUsername)
                .IsUnique();

            entity.Property(u => u.Role)
                .HasMaxLength(20)
                .IsRequired()
                .HasDefaultValue("User");
        });

        // Заказы.
        var order = modelBuilder.Entity<Order>();

        order.ToTable("orders");

        order.HasKey(o => o.Id);

        order.Property(o => o.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        order.Property(o => o.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        order.Property(o => o.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        order.Property(o => o.Status)
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired();

        order.Property(o => o.TotalAmount)
            .HasColumnName("total_amount")
            .HasPrecision(12, 2);

        order.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Позиции заказа.
        var orderItem = modelBuilder.Entity<OrderItem>();

        orderItem.ToTable("order_items", table =>
        {
            table.HasCheckConstraint(
                "CK_order_items_quantity_positive",
                "quantity > 0");

            table.HasCheckConstraint(
                "CK_order_items_unit_price_positive",
                "unit_price > 0");
        });

        orderItem.HasKey(i => i.Id);

        orderItem.Property(i => i.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        orderItem.Property(i => i.OrderId)
            .HasColumnName("order_id");

        orderItem.Property(i => i.ProductId)
            .HasColumnName("product_id");

        orderItem.Property(i => i.ProductName)
            .HasColumnName("product_name")
            .HasMaxLength(200)
            .IsRequired();

        orderItem.Property(i => i.UnitPrice)
            .HasColumnName("unit_price")
            .HasPrecision(10, 2);

        orderItem.Property(i => i.Quantity)
            .HasColumnName("quantity")
            .IsRequired();

        orderItem.HasOne(i => i.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        orderItem.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}