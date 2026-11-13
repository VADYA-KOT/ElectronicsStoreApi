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

public enum OrderStatusUpdateStatus
{
    Success,
    OrderNotFound,
    InvalidStatus,
    InvalidTransition,
    InventoryLimitExceeded,
    ConcurrencyConflict
}

public sealed record OrderServiceResult(
    OrderServiceStatus Status,
    OrderResponse? Order = null,
    int? ProductId = null,
    List<InsufficientStockItemResponse>? InsufficientStockItems = null);

public sealed record OrderStatusUpdateResult(
    OrderStatusUpdateStatus Status,
    OrderResponse? Order = null);

public class OrderService
{
    private readonly StoreDbContext _context;

    public OrderService(StoreDbContext context)
    {
        _context = context;
    }

    // Получение истории заказов пользователя.
    public async Task<List<OrderResponse>> GetByUserAsync(int userId)
    {
        if (userId <= 0)
        {
            return new List<OrderResponse>();
        }

        var orders = await _context.Orders
            .AsNoTracking()
            .Where(order => order.UserId == userId)
            .Include(order => order.Items)
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id)
            .ToListAsync();

        return orders
            .Select(MapToResponse)
            .ToList();
    }

    // Получение всех заказов для администратора.
    public async Task<List<OrderResponse>> GetAllForAdminAsync()
    {
        var orders = await _context.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id)
            .ToListAsync();

        return orders
            .Select(MapToResponse)
            .ToList();
    }

    // Изменение статуса заказа.
    public async Task<OrderStatusUpdateResult> UpdateStatusAsync(
        int orderId,
        string? requestedStatus)
    {
        if (orderId <= 0)
        {
            return new OrderStatusUpdateResult(
                OrderStatusUpdateStatus.OrderNotFound);
        }

        string? newStatus = NormalizeStatus(requestedStatus);

        if (newStatus is null)
        {
            return new OrderStatusUpdateResult(
                OrderStatusUpdateStatus.InvalidStatus);
        }

        try
        {
            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            // Загружаем заказ вместе с его позициями.
            var order = await _context.Orders
                .Include(order => order.Items)
                .FirstOrDefaultAsync(order => order.Id == orderId);

            if (order is null)
            {
                return new OrderStatusUpdateResult(
                    OrderStatusUpdateStatus.OrderNotFound);
            }

            // Проверяем допустимость перехода.
            if (!CanChangeStatus(order.Status, newStatus))
            {
                return new OrderStatusUpdateResult(
                    OrderStatusUpdateStatus.InvalidTransition);
            }

            // При отмене возвращаем товары на склад.
            if (newStatus == "Cancelled")
            {
                var quantitiesToRestore = order.Items
                    .Where(item => item.ProductId.HasValue)
                    .GroupBy(item => item.ProductId!.Value)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Sum(item => (long)item.Quantity));

                int[] productIds = quantitiesToRestore.Keys.ToArray();

                var products = await _context.Products
                    .Where(product => productIds.Contains(product.Id))
                    .ToDictionaryAsync(product => product.Id);

                // Сначала проверяем, что возврат не вызовет
                // переполнение целочисленного остатка.
                foreach (var entry in quantitiesToRestore)
                {
                    if (!products.TryGetValue(
                            entry.Key,
                            out var product))
                    {
                        // Товар уже отсутствует в каталоге.
                        continue;
                    }

                    long restoredQuantity =
                        (long)product.Quantity + entry.Value;

                    if (restoredQuantity > int.MaxValue)
                    {
                        return new OrderStatusUpdateResult(
                            OrderStatusUpdateStatus.InventoryLimitExceeded);
                    }
                }

                // После всех проверок восстанавливаем остатки.
                foreach (var entry in quantitiesToRestore)
                {
                    if (!products.TryGetValue(
                            entry.Key,
                            out var product))
                    {
                        continue;
                    }

                    product.Quantity = checked(
                        product.Quantity + (int)entry.Value);
                }
            }

            order.Status = newStatus;

            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            return new OrderStatusUpdateResult(
                OrderStatusUpdateStatus.Success,
                MapToResponse(order));
        }
        catch (DbUpdateException ex)
            when (IsSerializationFailure(ex))
        {
            return new OrderStatusUpdateResult(
                OrderStatusUpdateStatus.ConcurrencyConflict);
        }
        catch (PostgresException ex)
            when (ex.SqlState ==
                  PostgresErrorCodes.SerializationFailure)
        {
            return new OrderStatusUpdateResult(
                OrderStatusUpdateStatus.ConcurrencyConflict);
        }
    }

    // Проверка допустимых переходов между статусами.
    private static bool CanChangeStatus(
        string currentStatus,
        string newStatus)
    {
        return currentStatus switch
        {
            "Pending" =>
                newStatus is "Processing" or "Cancelled",

            "Processing" =>
                newStatus is "Shipped" or "Cancelled",

            "Shipped" =>
                newStatus == "Completed",

            _ => false
        };
    }

    // Нормализация входящего статуса.
    private static string? NormalizeStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "processing" => "Processing",
            "shipped" => "Shipped",
            "completed" => "Completed",
            "cancelled" => "Cancelled",
            _ => null
        };
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

                if (product.Name.Length > 200)
                {
                    return new OrderServiceResult(
                        OrderServiceStatus.InvalidRequest,
                        ProductId: product.Id);
                }

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

            // Создаём заказ.
            var newOrder = new Order
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

                product.Quantity -= item.Quantity;

                newOrder.Items.Add(new OrderItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    UnitPrice = product.Price,
                    Quantity = item.Quantity
                });
            }

            _context.Orders.Add(newOrder);

            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            return new OrderServiceResult(
                OrderServiceStatus.Success,
                MapToResponse(newOrder));
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