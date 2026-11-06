using System.ComponentModel.DataAnnotations;

namespace ElectronicsStoreApi.Models;

public class ProductSpecification
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public int CategorySpecificationId { get; set; }

    [Required]
    [StringLength(500)]
    public string Value { get; set; } = string.Empty;

    public Product? Product { get; set; }

    public CategorySpecification? CategorySpecification { get; set; }
}