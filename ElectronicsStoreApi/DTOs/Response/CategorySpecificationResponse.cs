namespace ElectronicsStoreApi.DTOs.Response;

public class CategorySpecificationResponse
{
    public int Id { get; set; }

    public int CategoryId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Unit { get; set; }

    public int SortOrder { get; set; }
}