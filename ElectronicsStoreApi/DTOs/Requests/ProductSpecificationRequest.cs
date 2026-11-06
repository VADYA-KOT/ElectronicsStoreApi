using System.ComponentModel.DataAnnotations;

namespace ElectronicsStoreApi.DTOs.Requests;

public class ProductSpecificationRequest
{
    [Range(1, int.MaxValue)]
    public int CategorySpecificationId { get; set; }

    [Required]
    [StringLength(500)]
    public string Value { get; set; } = string.Empty;
}