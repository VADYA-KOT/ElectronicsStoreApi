using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.DTOs.Requests;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace ElectronicsStoreApi.Tests;

public class OrderServiceTransactionTests
{
    [Fact]
    public async Task CreateAsync_DatabaseFailure_RollsBackChanges()
    {
        // Создаём SQLite БД.
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseSqlite(connection)
            .Options;

        int userId;
        int productId;

        // Подготавливаем данные.
        await using (var setupContext = new StoreDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();

            var user = new AppUser
            {
                Username = "testuser",
                NormalizedUsername = "TESTUSER",
                PasswordHash = "test-password-hash"
            };

            var product = new Product
            {
                Name = "Test Mouse",
                Price = 1250m,
                Quantity = 5
            };

            setupContext.AppUsers.Add(user);
            setupContext.Products.Add(product);

            await setupContext.SaveChangesAsync();

            userId = user.Id;
            productId = product.Id;

            // Создаём триггер, который искусственно вызывает
            // ошибку при добавлении позиции заказа.
            await setupContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER FailOrderItemInsert
                BEFORE INSERT ON order_items
                BEGIN
                    SELECT RAISE(
                        ABORT,
                        'Simulated order item insertion failure'
                    );
                END;
                """);
        }

        // Формируем запрос на покупку двух товаров.
        var request = new CreateOrderRequest
        {
            Items =
            {
                new CreateOrderItemRequest
                {
                    ProductId = productId,
                    Quantity = 2
                }
            }
        };

        // Пытаемся создать заказ.
        await using (var serviceContext = new StoreDbContext(options))
        {
            var service = new OrderService(serviceContext);

            // Ожидаем ошибку сохранения в БД.
            await Assert.ThrowsAsync<DbUpdateException>(
                () => service.CreateAsync(userId, request));
        }

        // Проверяем состояние БД после ошибки.
        await using (var verificationContext =
                     new StoreDbContext(options))
        {
            // Заказ не должен сохраниться.
            Assert.False(
                await verificationContext.Orders.AnyAsync());

            // Позиции заказа не должны сохраниться.
            Assert.False(
                await verificationContext.OrderItems.AnyAsync());

            // Остаток товара должен остаться прежним.
            var product = await verificationContext.Products
                .AsNoTracking()
                .SingleAsync(p => p.Id == productId);

            Assert.Equal(5, product.Quantity);
        }
    }
}