using System.ComponentModel.DataAnnotations;

namespace ElectronicsStoreApi.Models;

public class Category
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    public List<Product> Products { get; set; } = new();

    public List<CategorySpecification> Specifications { get; set; } = new();
}
