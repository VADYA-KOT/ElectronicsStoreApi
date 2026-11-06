namespace ElectronicsStoreApi.DTOs.Response;

public class InsufficientStockItemResponse
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int RequestedQuantity { get; set; }

    public int AvailableQuantity { get; set; }
}