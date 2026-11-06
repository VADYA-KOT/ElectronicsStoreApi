using System.ComponentModel.DataAnnotations;

namespace ElectronicsStoreApi.DTOs.Requests;

public class SaveCategorySpecificationRequest
{
    [Required]
    [StringLength(50, MinimumLength = 2)]
    [RegularExpression(
        @"^[a-zA-Z0-9_-]+$",
        ErrorMessage = "Код может содержать только латинские буквы, цифры, дефис и нижнее подчёркивание.")]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(30)]
    public string? Unit { get; set; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; set; }
}