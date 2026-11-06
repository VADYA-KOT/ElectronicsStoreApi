using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ElectronicsStoreApi.DTOs.Requests;
using ElectronicsStoreApi.DTOs.Response;
using ElectronicsStoreApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElectronicsStoreApi;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly OrderService _orderService;

    public OrdersController(OrderService orderService)
    {
        _orderService = orderService;
    }

    // Создание заказа.
    [HttpPost]
    public async Task<ActionResult<OrderResponse>> CreateOrder(
        [FromBody] CreateOrderRequest request)
    {
        // Получаем ID пользователя из JWT-токена.
        string? userIdClaim =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!int.TryParse(userIdClaim, out int userId))
        {
            return Unauthorized(new
            {
                message = "Не удалось определить пользователя."
            });
        }

        var result = await _orderService.CreateAsync(
            userId,
            request);

        switch (result.Status)
        {
            case OrderServiceStatus.Success:
                return StatusCode(
                    StatusCodes.Status201Created,
                    result.Order);

            case OrderServiceStatus.InvalidRequest:
                return BadRequest(new
                {
                    message = "Некорректные данные заказа."
                });

            case OrderServiceStatus.UserNotFound:
                return Unauthorized(new
                {
                    message = "Пользователь не найден."
                });

            case OrderServiceStatus.ProductNotFound:
                return NotFound(new
                {
                    message =
                        $"Товар с ID {result.ProductId} не найден."
                });

            case OrderServiceStatus.InsufficientStock:
                return Conflict(new
                {
                    message =
                        "Некоторых товаров недостаточно на складе. Проверьте количество и подтвердите изменения.",

                    items = result.InsufficientStockItems
                });

            case OrderServiceStatus.DuplicateProduct:
                return BadRequest(new
                {
                    message =
                        $"Товар с ID {result.ProductId} повторяется в заказе."
                });

            case OrderServiceStatus.ConcurrencyConflict:
                return Conflict(new
                {
                    message =
                        "Остатки товаров изменились во время оформления заказа. Повторите попытку."
                });

            default:
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        message = "Не удалось оформить заказ."
                    });
        }
    }
}