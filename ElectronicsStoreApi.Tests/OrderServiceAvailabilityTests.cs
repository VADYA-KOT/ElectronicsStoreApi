using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.DTOs.Requests;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElectronicsStoreApi.Tests;

public class OrderServiceAvailabilityTests
{
    [Fact]
    public async Task CreateAsync_MultipleProductsInsufficientStock_ReturnsAllProblems()
    {
        // Создаём SQLite БД.
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new StoreDbContext(options);

        await context.Database.EnsureCreatedAsync();

        // Создаём пользователя.
        var user = new AppUser
        {
            Username = "testuser",
            NormalizedUsername = "TESTUSER",
            PasswordHash = "test-password-hash"
        };

        // Создаём два товара с недостаточным остатком.
        var mouse = new Product
        {
            Name = "Test Mouse",
            Price = 1250m,
            Quantity = 2
        };

        var keyboard = new Product
        {
            Name = "Test Keyboard",
            Price = 3000m,
            Quantity = 0
        };

        context.AppUsers.Add(user);
        context.Products.AddRange(mouse, keyboard);

        await context.SaveChangesAsync();

        // Заказываем больше, чем доступно.
        var request = new CreateOrderRequest
        {
            Items =
            {
                new CreateOrderItemRequest
                {
                    ProductId = mouse.Id,
                    Quantity = 3
                },

                new CreateOrderItemRequest
                {
                    ProductId = keyboard.Id,
                    Quantity = 1
                }
            }
        };

        var service = new OrderService(context);

        var result = await service.CreateAsync(user.Id, request);

        // Проверяем общий статус.
        Assert.Equal(
            OrderServiceStatus.InsufficientStock,
            result.Status);

        // Проверяем, что получены обе проблемные позиции.
        Assert.NotNull(result.InsufficientStockItems);
        Assert.Equal(2, result.InsufficientStockItems.Count);

        var mouseProblem = Assert.Single(
            result.InsufficientStockItems,
            item => item.ProductId == mouse.Id);

        Assert.Equal("Test Mouse", mouseProblem.ProductName);
        Assert.Equal(3, mouseProblem.RequestedQuantity);
        Assert.Equal(2, mouseProblem.AvailableQuantity);

        var keyboardProblem = Assert.Single(
            result.InsufficientStockItems,
            item => item.ProductId == keyboard.Id);

        Assert.Equal("Test Keyboard", keyboardProblem.ProductName);
        Assert.Equal(1, keyboardProblem.RequestedQuantity);
        Assert.Equal(0, keyboardProblem.AvailableQuantity);

        // Заказ не должен создаваться.
        Assert.Null(result.Order);
        Assert.False(await context.Orders.AnyAsync());
        Assert.False(await context.OrderItems.AnyAsync());

        // Остатки не должны изменяться.
        var updatedMouse = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == mouse.Id);

        var updatedKeyboard = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == keyboard.Id);

        Assert.Equal(2, updatedMouse.Quantity);
        Assert.Equal(0, updatedKeyboard.Quantity);
    }
}