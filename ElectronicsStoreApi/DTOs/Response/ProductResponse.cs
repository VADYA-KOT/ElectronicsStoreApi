namespace ElectronicsStoreApi.DTOs.Response;

public class ProductResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public decimal? OldPrice { get; set; }

    public int Quantity { get; set; }

    public string? ImageUrl { get; set; }

    public int? CategoryId { get; set; }

    public string? CategoryName { get; set; }

    public string? Description { get; set; }

    public List<ProductSpecificationResponse> Specifications { get; set; } = new();
}