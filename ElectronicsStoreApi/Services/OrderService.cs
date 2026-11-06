using System.Data;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.DTOs.Requests;
using ElectronicsStoreApi.DTOs.Response;
using ElectronicsStoreApi.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ElectronicsStoreApi.Services;

public enum OrderServiceStatus
{
    Success,
    InvalidRequest,
    UserNotFound,
    ProductNotFound,
    InsufficientStock,
    DuplicateProduct,
    ConcurrencyConflict
}

public sealed record OrderServiceResult(
    OrderServiceStatus Status,
    OrderResponse? Order = null,
    int? ProductId = null,
    List<InsufficientStockItemResponse>? InsufficientStockItems = null);

public class OrderService
{
    private readonly StoreDbContext _context;

    public OrderService(StoreDbContext context)
    {
        _context = context;
    }

    // Создание заказа.
    public async Task<OrderServiceResult> CreateAsync(
        int userId,
        CreateOrderRequest request)
    {
        // Проверяем входные данные.
        if (userId <= 0 ||
            request is null ||
            request.Items is null ||
            request.Items.Count == 0)
        {
            return new OrderServiceResult(
                OrderServiceStatus.InvalidRequest);
        }

        if (request.Items.Any(item =>
                item is null ||
                item.ProductId <= 0 ||
                item.Quantity <= 0))
        {
            return new OrderServiceResult(
                OrderServiceStatus.InvalidRequest);
        }

        // Проверяем повторение товаров.
        var duplicate = request.Items
            .GroupBy(item => item.ProductId)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            return new OrderServiceResult(
                OrderServiceStatus.DuplicateProduct,
                ProductId: duplicate.Key);
        }

        try
        {
            // Открываем транзакцию.
            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            // Проверяем существование пользователя.
            bool userExists = await _context.AppUsers
                .AnyAsync(user => user.Id == userId);

            if (!userExists)
            {
                return new OrderServiceResult(
                    OrderServiceStatus.UserNotFound);
            }

            // Получаем ID товаров из запроса.
            int[] productIds = request.Items
                .Select(item => item.ProductId)
                .ToArray();

            // Загружаем товары из БД.
            var products = await _context.Products
                .Where(product => productIds.Contains(product.Id))
                .ToListAsync();

            var existingIds = products
                .Select(product => product.Id)
                .ToHashSet();

            // Проверяем существование всех товаров.
            int? missingProductId = productIds
                .Where(id => !existingIds.Contains(id))
                .Select(id => (int?)id)
                .FirstOrDefault();

            if (missingProductId.HasValue)
            {
                return new OrderServiceResult(
                    OrderServiceStatus.ProductNotFound,
                    ProductId: missingProductId.Value);
            }

            var productsById = products.ToDictionary(
                product => product.Id);

            // Собираем все товары с недостаточным остатком.
            var insufficientStockItems =
                new List<InsufficientStockItemResponse>();

            foreach (var item in request.Items)
            {
                var product = productsById[item.ProductId];

                // Название позиции заказа ограничено 200 символами.
                if (product.Name.Length > 200)
                {
                    return new OrderServiceResult(
                        OrderServiceStatus.InvalidRequest,
                        ProductId: product.Id);
                }

                // Проверяем остаток каждого товара.
                if (product.Quantity < item.Quantity)
                {
                    insufficientStockItems.Add(
                        new InsufficientStockItemResponse
                        {
                            ProductId = product.Id,
                            ProductName = product.Name,
                            RequestedQuantity = item.Quantity,
                            AvailableQuantity = product.Quantity
                        });
                }
            }

            // Если есть хотя бы один проблемный товар,
            // возвращаем информацию обо всех таких товарах.
            if (insufficientStockItems.Count > 0)
            {
                return new OrderServiceResult(
                    OrderServiceStatus.InsufficientStock,
                    ProductId: insufficientStockItems[0].ProductId,
                    InsufficientStockItems: insufficientStockItems);
            }

            // Проверяем, помещается ли итоговая сумма в numeric(12,2).
            const decimal MaxOrderTotal = 9_999_999_999.99m;
            decimal totalAmount = 0m;

            foreach (var item in request.Items)
            {
                decimal lineTotal =
                    productsById[item.ProductId].Price * item.Quantity;

                if (lineTotal > MaxOrderTotal - totalAmount)
                {
                    return new OrderServiceResult(
                        OrderServiceStatus.InvalidRequest);
                }

                totalAmount += lineTotal;
            }

            // Все товары доступны. Создаём заказ.
            var order = new Order
            {
                UserId = userId,
                CreatedAtUtc = DateTime.UtcNow,
                Status = "Pending",
                TotalAmount = totalAmount
            };

            // Добавляем позиции и уменьшаем остатки.
            foreach (var item in request.Items)
            {
                var product = productsById[item.ProductId];

                decimal unitPrice = product.Price;

                product.Quantity -= item.Quantity;

                order.Items.Add(new OrderItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    UnitPrice = unitPrice,
                    Quantity = item.Quantity
                });
            }

            // Добавляем заказ в контекст.
            _context.Orders.Add(order);

            // Сохраняем заказ, позиции и остатки.
            await _context.SaveChangesAsync();

            // Подтверждаем транзакцию.
            await transaction.CommitAsync();

            return new OrderServiceResult(
                OrderServiceStatus.Success,
                MapToResponse(order));
        }
        catch (DbUpdateException ex)
            when (IsSerializationFailure(ex))
        {
            return new OrderServiceResult(
                OrderServiceStatus.ConcurrencyConflict);
        }
        catch (PostgresException ex)
            when (ex.SqlState ==
                  PostgresErrorCodes.SerializationFailure)
        {
            return new OrderServiceResult(
                OrderServiceStatus.ConcurrencyConflict);
        }
    }

    // Преобразование модели заказа в DTO ответа.
    private static OrderResponse MapToResponse(Order order)
    {
        return new OrderResponse
        {
            Id = order.Id,
            CreatedAtUtc = order.CreatedAtUtc,
            Status = order.Status,
            TotalAmount = order.TotalAmount,

            Items = order.Items.Select(item =>
                new OrderItemResponse
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    UnitPrice = item.UnitPrice,
                    Quantity = item.Quantity,
                    LineTotal = item.UnitPrice * item.Quantity
                }).ToList()
        };
    }

    private static bool IsSerializationFailure(Exception exception)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is PostgresException postgresException &&
                postgresException.SqlState ==
                PostgresErrorCodes.SerializationFailure)
            {
                return true;
            }
        }

        return false;
    }
}