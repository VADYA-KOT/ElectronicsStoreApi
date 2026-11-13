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

    // Получение истории заказов текущего пользователя.
    [HttpGet]
    public async Task<ActionResult<List<OrderResponse>>> GetMyOrders()
    {
        int? userId = GetCurrentUserId();

        if (!userId.HasValue)
        {
            return Unauthorized(new
            {
                message = "Не удалось определить пользователя."
            });
        }

        var orders = await _orderService.GetByUserAsync(userId.Value);

        return Ok(orders);
    }

    // Получение всех заказов для администратора.
    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<OrderResponse>>> GetAllOrders()
    {
        var orders = await _orderService.GetAllForAdminAsync();

        return Ok(orders);
    }

    // Создание заказа.
    [HttpPost]
    public async Task<ActionResult<OrderResponse>> CreateOrder(
        [FromBody] CreateOrderRequest request)
    {
        int? userId = GetCurrentUserId();

        if (!userId.HasValue)
        {
            return Unauthorized(new
            {
                message = "Не удалось определить пользователя."
            });
        }

        var result = await _orderService.CreateAsync(
            userId.Value,
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
                    message = $"Товар с ID {result.ProductId} не найден."
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

    // Изменение статуса заказа администратором.
    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<OrderResponse>> UpdateOrderStatus(
        int id,
        [FromBody] UpdateOrderStatusRequest request)
    {
        var result = await _orderService.UpdateStatusAsync(
            id,
            request.Status);

        switch (result.Status)
        {
            case OrderStatusUpdateStatus.Success:
                return Ok(result.Order);

            case OrderStatusUpdateStatus.OrderNotFound:
                return NotFound(new
                {
                    message = $"Заказ с ID {id} не найден."
                });

            case OrderStatusUpdateStatus.InvalidStatus:
                return BadRequest(new
                {
                    message =
                        "Недопустимый статус. Используйте Processing, Shipped, Completed или Cancelled."
                });

            case OrderStatusUpdateStatus.InvalidTransition:
                return Conflict(new
                {
                    message =
                        "Такой переход статуса заказа запрещён."
                });

            case OrderStatusUpdateStatus.InventoryLimitExceeded:
                return Conflict(new
                {
                    message =
                        "Невозможно отменить заказ: восстановление остатков приведёт к превышению допустимого количества."
                });

            case OrderStatusUpdateStatus.ConcurrencyConflict:
                return Conflict(new
                {
                    message =
                        "Заказ одновременно изменяется другим запросом. Обновите данные и повторите попытку."
                });

            default:
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        message =
                            "Не удалось изменить статус заказа."
                    });
        }
    }

    // Получение ID пользователя из JWT.
    private int? GetCurrentUserId()
    {
        string? userIdClaim =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!int.TryParse(userIdClaim, out int userId) ||
            userId <= 0)
        {
            return null;
        }

        return userId;
    }
}