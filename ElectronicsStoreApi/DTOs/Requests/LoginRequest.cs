using System.ComponentModel.DataAnnotations;

namespace ElectronicsStoreApi.DTOs.Requests;

public class LoginRequest
{
    [Required]
    [StringLength(40, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
