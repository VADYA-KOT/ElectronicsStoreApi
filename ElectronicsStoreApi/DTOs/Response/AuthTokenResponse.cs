namespace ElectronicsStoreApi.DTOs.Response;

public class AuthTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;

    public string TokenType { get; set; } = "Bearer";

    public DateTime ExpiresAtUtc { get; set; }
}
