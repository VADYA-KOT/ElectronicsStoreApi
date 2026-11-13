namespace ElectronicsStoreApi.DTOs.Response;

public class UserProfileResponse
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}