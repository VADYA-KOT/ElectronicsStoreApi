namespace ElectronicsStoreApi.DTOs.Response;

public class ProductSpecificationResponse
{
    public int CategorySpecificationId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Unit { get; set; }

    public string Value { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}