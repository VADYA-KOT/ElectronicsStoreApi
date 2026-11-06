using System.ComponentModel.DataAnnotations;

namespace ElectronicsStoreApi.DTOs.Requests;

public class RegisterRequest
{
    [Required]
    [StringLength(40, MinimumLength = 3)]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 12)]
    public string Password { get; set; } = string.Empty;
}
