using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace ElectronicsStoreApi.DTOs.Requests;

public class UpdateProductRequest : IValidatableObject
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "99999999.99",
        ParseLimitsInInvariantCulture = true)]
    public decimal Price { get; set; }

    [Range(typeof(decimal), "0.01", "99999999.99",
        ParseLimitsInInvariantCulture = true)]
    public decimal? OldPrice { get; set; }

    [Range(0, int.MaxValue)]
    public int Quantity { get; set; }

    public int? CategoryId { get; set; }

    public string? Description { get; set; }

    // Характеристики товара.
    [MaxLength(50)]
    public List<ProductSpecificationRequest> Specifications { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (OldPrice.HasValue && OldPrice.Value <= Price)
        {
            yield return new ValidationResult(
                "Старая цена должна быть выше текущей.",
                new[] { nameof(OldPrice) });
        }
    }
}