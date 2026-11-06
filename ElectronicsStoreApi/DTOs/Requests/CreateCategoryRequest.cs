using System.ComponentModel.DataAnnotations;

namespace ElectronicsStoreApi.DTOs.Requests;

public class CreateCategoryRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;
}