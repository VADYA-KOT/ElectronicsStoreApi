using System.ComponentModel.DataAnnotations;

namespace ElectronicsStoreApi.Models;

public class CategorySpecification
{
    public int Id { get; set; }

    public int CategoryId { get; set; }

    [Required]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(30)]
    public string? Unit { get; set; }

    public int SortOrder { get; set; }

    public Category? Category { get; set; }

    public List<ProductSpecification> ProductSpecifications { get; set; } = new();
}