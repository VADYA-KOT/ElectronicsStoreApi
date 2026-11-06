using System.ComponentModel.DataAnnotations;

namespace ElectronicsStoreApi.DTOs.Requests;

public class CreateOrderRequest
{
    [Required]
    [MinLength(1, ErrorMessage = "Заказ должен содержать хотя бы один товар.")]
    public List<CreateOrderItemRequest> Items { get; set; } = new();
}