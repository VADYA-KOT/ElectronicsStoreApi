namespace ElectronicsStoreApi.DTOs.Response;

public class CategoryResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int ProductCount { get; set; }
}