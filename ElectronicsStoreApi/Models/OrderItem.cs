namespace ElectronicsStoreApi.Models;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    // Может быть null, если товар удалён из каталога.
    public int? ProductId { get; set; }

    // Исторические данные сохраняются независимо от товара.
    public string ProductName { get; set; } = string.Empty;

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public Order Order { get; set; } = null!;

    // Навигация необязательна.
    public Product? Product { get; set; }
}