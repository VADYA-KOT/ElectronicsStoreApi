using System.ComponentModel.DataAnnotations;

namespace ElectronicsStoreApi.Models;

public class Product
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "99999999.99",
        ParseLimitsInInvariantCulture = true)]
    public decimal Price { get; set; }

    // Прежняя цена. null означает отсутствие скидки.
    public decimal? OldPrice { get; set; }

    [Range(0, int.MaxValue)]
    public int Quantity { get; set; }

    // Относительный URL фотографии товара.
    public string? ImageUrl { get; set; }

    public int? CategoryId { get; set; }

    public string? Description { get; set; }

    public Category? Category { get; set; }

    // Технические характеристики товара.
    public List<ProductSpecification> Specifications { get; set; } = new();
}