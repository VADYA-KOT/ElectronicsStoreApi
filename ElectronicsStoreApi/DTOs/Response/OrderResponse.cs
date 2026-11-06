namespace ElectronicsStoreApi.DTOs.Response;

public class OrderResponse
{
    public int Id { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string Status { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }

    public List<OrderItemResponse> Items { get; set; } = new();
}