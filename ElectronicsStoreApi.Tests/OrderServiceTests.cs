using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.DTOs.Requests;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElectronicsStoreApi.Tests;

public class OrderServiceTests
{
    // Успешное создание заказа.
    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesOrderAndDecreasesStock()
    {
        await using var connection = await CreateConnectionAsync();
        await using var context = CreateContext(connection);

        await context.Database.EnsureCreatedAsync();

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

        context.AppUsers.Add(user);
        context.Products.Add(product);

        await context.SaveChangesAsync();

        var request = new CreateOrderRequest
        {
            Items =
            {
                new CreateOrderItemRequest
                {
                    ProductId = product.Id,
                    Quantity = 2
                }
            }
        };

        var service = new OrderService(context);

        var result = await service.CreateAsync(user.Id, request);

        Assert.Equal(OrderServiceStatus.Success, result.Status);
        Assert.NotNull(result.Order);

        var order = result.Order;

        Assert.Equal("Pending", order.Status);
        Assert.Equal(2500m, order.TotalAmount);

        var orderItem = Assert.Single(order.Items);

        Assert.Equal(product.Id, orderItem.ProductId);
        Assert.Equal("Test Mouse", orderItem.ProductName);
        Assert.Equal(1250m, orderItem.UnitPrice);
        Assert.Equal(2, orderItem.Quantity);
        Assert.Equal(2500m, orderItem.LineTotal);

        // Проверяем изменение остатка.
        var updatedProduct = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == product.Id);

        Assert.Equal(3, updatedProduct.Quantity);

        // Проверяем сохранение заказа и позиции в БД.
        var savedOrder = await context.Orders
            .Include(o => o.Items)
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(order.Id, savedOrder.Id);
        Assert.Equal(user.Id, savedOrder.UserId);
        Assert.Equal(2500m, savedOrder.TotalAmount);
        Assert.Single(savedOrder.Items);
    }

    // Недостаточное количество товара.
    [Fact]
    public async Task CreateAsync_InsufficientStock_ReturnsErrorWithoutChangingData()
    {
        await using var connection = await CreateConnectionAsync();
        await using var context = CreateContext(connection);

        await context.Database.EnsureCreatedAsync();

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
            Quantity = 2
        };

        context.AppUsers.Add(user);
        context.Products.Add(product);

        await context.SaveChangesAsync();

        var request = new CreateOrderRequest
        {
            Items =
            {
                new CreateOrderItemRequest
                {
                    ProductId = product.Id,
                    Quantity = 3
                }
            }
        };

        var service = new OrderService(context);

        var result = await service.CreateAsync(user.Id, request);

        Assert.Equal(
            OrderServiceStatus.InsufficientStock,
            result.Status);

        Assert.Equal(product.Id, result.ProductId);
        Assert.Null(result.Order);

        // Заказ и позиции не должны появиться.
        Assert.False(await context.Orders.AnyAsync());
        Assert.False(await context.OrderItems.AnyAsync());

        // Остаток должен остаться прежним.
        var unchangedProduct = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == product.Id);

        Assert.Equal(2, unchangedProduct.Quantity);
    }

    // Несуществующий товар.
    [Fact]
    public async Task CreateAsync_MissingProduct_ReturnsProductNotFound()
    {
        await using var connection = await CreateConnectionAsync();
        await using var context = CreateContext(connection);

        await context.Database.EnsureCreatedAsync();

        var user = new AppUser
        {
            Username = "testuser",
            NormalizedUsername = "TESTUSER",
            PasswordHash = "test-password-hash"
        };

        context.AppUsers.Add(user);

        await context.SaveChangesAsync();

        var request = new CreateOrderRequest
        {
            Items =
            {
                new CreateOrderItemRequest
                {
                    ProductId = 9999,
                    Quantity = 1
                }
            }
        };

        var service = new OrderService(context);

        var result = await service.CreateAsync(user.Id, request);

        Assert.Equal(
            OrderServiceStatus.ProductNotFound,
            result.Status);

        Assert.Equal(9999, result.ProductId);
        Assert.Null(result.Order);

        Assert.False(await context.Orders.AnyAsync());
        Assert.False(await context.OrderItems.AnyAsync());
    }

    // Повторение одного товара в запросе.
    [Fact]
    public async Task CreateAsync_DuplicateProduct_ReturnsDuplicateProduct()
    {
        await using var connection = await CreateConnectionAsync();
        await using var context = CreateContext(connection);

        await context.Database.EnsureCreatedAsync();

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

        context.AppUsers.Add(user);
        context.Products.Add(product);

        await context.SaveChangesAsync();

        var request = new CreateOrderRequest
        {
            Items =
            {
                new CreateOrderItemRequest
                {
                    ProductId = product.Id,
                    Quantity = 1
                },

                new CreateOrderItemRequest
                {
                    ProductId = product.Id,
                    Quantity = 2
                }
            }
        };

        var service = new OrderService(context);

        var result = await service.CreateAsync(user.Id, request);

        Assert.Equal(
            OrderServiceStatus.DuplicateProduct,
            result.Status);

        Assert.Equal(product.Id, result.ProductId);
        Assert.Null(result.Order);

        Assert.False(await context.Orders.AnyAsync());
        Assert.False(await context.OrderItems.AnyAsync());

        var unchangedProduct = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == product.Id);

        Assert.Equal(5, unchangedProduct.Quantity);
    }

    // Несуществующий пользователь.
    [Fact]
    public async Task CreateAsync_MissingUser_ReturnsUserNotFound()
    {
        await using var connection = await CreateConnectionAsync();
        await using var context = CreateContext(connection);

        await context.Database.EnsureCreatedAsync();

        var product = new Product
        {
            Name = "Test Mouse",
            Price = 1250m,
            Quantity = 5
        };

        context.Products.Add(product);

        await context.SaveChangesAsync();

        const int missingUserId = 9999;

        var request = new CreateOrderRequest
        {
            Items =
            {
                new CreateOrderItemRequest
                {
                    ProductId = product.Id,
                    Quantity = 1
                }
            }
        };

        var service = new OrderService(context);

        var result = await service.CreateAsync(
            missingUserId,
            request);

        Assert.Equal(
            OrderServiceStatus.UserNotFound,
            result.Status);

        Assert.Null(result.Order);

        Assert.False(await context.Orders.AnyAsync());
        Assert.False(await context.OrderItems.AnyAsync());

        var unchangedProduct = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == product.Id);

        Assert.Equal(5, unchangedProduct.Quantity);
    }

    // Несколько разных товаров в одном заказе.
    [Fact]
    public async Task CreateAsync_MultipleProducts_CalculatesTotalAndDecreasesStock()
    {
        await using var connection = await CreateConnectionAsync();
        await using var context = CreateContext(connection);

        await context.Database.EnsureCreatedAsync();

        var user = new AppUser
        {
            Username = "testuser",
            NormalizedUsername = "TESTUSER",
            PasswordHash = "test-password-hash"
        };

        var mouse = new Product
        {
            Name = "Test Mouse",
            Price = 1250m,
            Quantity = 5
        };

        var keyboard = new Product
        {
            Name = "Test Keyboard",
            Price = 3000m,
            Quantity = 4
        };

        context.AppUsers.Add(user);
        context.Products.AddRange(mouse, keyboard);

        await context.SaveChangesAsync();

        // Две мыши и одна клавиатура.
        var request = new CreateOrderRequest
        {
            Items =
            {
                new CreateOrderItemRequest
                {
                    ProductId = mouse.Id,
                    Quantity = 2
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

        Assert.Equal(OrderServiceStatus.Success, result.Status);
        Assert.NotNull(result.Order);

        var order = result.Order;

        // 1250 * 2 + 3000 * 1 = 5500.
        Assert.Equal(5500m, order.TotalAmount);

        Assert.Equal("Pending", order.Status);
        Assert.Equal(2, order.Items.Count);

        var mouseItem = order.Items
            .Single(item => item.ProductId == mouse.Id);

        Assert.Equal("Test Mouse", mouseItem.ProductName);
        Assert.Equal(1250m, mouseItem.UnitPrice);
        Assert.Equal(2, mouseItem.Quantity);
        Assert.Equal(2500m, mouseItem.LineTotal);

        var keyboardItem = order.Items
            .Single(item => item.ProductId == keyboard.Id);

        Assert.Equal("Test Keyboard", keyboardItem.ProductName);
        Assert.Equal(3000m, keyboardItem.UnitPrice);
        Assert.Equal(1, keyboardItem.Quantity);
        Assert.Equal(3000m, keyboardItem.LineTotal);

        // Остаток мышей: 5 - 2 = 3.
        var updatedMouse = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == mouse.Id);

        Assert.Equal(3, updatedMouse.Quantity);

        // Остаток клавиатур: 4 - 1 = 3.
        var updatedKeyboard = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == keyboard.Id);

        Assert.Equal(3, updatedKeyboard.Quantity);

        var savedOrder = await context.Orders
            .Include(o => o.Items)
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(user.Id, savedOrder.UserId);
        Assert.Equal(5500m, savedOrder.TotalAmount);
        Assert.Equal(2, savedOrder.Items.Count);
    }

    // Пустой список товаров.
    [Fact]
    public async Task CreateAsync_EmptyItems_ReturnsInvalidRequest()
    {
        await using var connection = await CreateConnectionAsync();
        await using var context = CreateContext(connection);

        await context.Database.EnsureCreatedAsync();

        var request = new CreateOrderRequest
        {
            Items = new List<CreateOrderItemRequest>()
        };

        var service = new OrderService(context);

        var result = await service.CreateAsync(1, request);

        Assert.Equal(
            OrderServiceStatus.InvalidRequest,
            result.Status);

        Assert.Null(result.Order);

        Assert.False(await context.Orders.AnyAsync());
        Assert.False(await context.OrderItems.AnyAsync());
    }

    // Некорректный ProductId или Quantity.
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public async Task CreateAsync_InvalidItem_ReturnsInvalidRequest(
        int productId,
        int quantity)
    {
        await using var connection = await CreateConnectionAsync();
        await using var context = CreateContext(connection);

        await context.Database.EnsureCreatedAsync();

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

        context.AppUsers.Add(user);
        context.Products.Add(product);

        await context.SaveChangesAsync();

        var request = new CreateOrderRequest
        {
            Items =
            {
                new CreateOrderItemRequest
                {
                    ProductId = productId > 0
                        ? product.Id
                        : productId,

                    Quantity = quantity
                }
            }
        };

        var service = new OrderService(context);

        var result = await service.CreateAsync(user.Id, request);

        Assert.Equal(
            OrderServiceStatus.InvalidRequest,
            result.Status);

        Assert.Null(result.Order);
        Assert.Null(result.ProductId);

        Assert.False(await context.Orders.AnyAsync());
        Assert.False(await context.OrderItems.AnyAsync());

        var unchangedProduct = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == product.Id);

        Assert.Equal(5, unchangedProduct.Quantity);
    }

    // Некорректный ID пользователя.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateAsync_InvalidUserId_ReturnsInvalidRequest(
        int userId)
    {
        await using var connection = await CreateConnectionAsync();
        await using var context = CreateContext(connection);

        await context.Database.EnsureCreatedAsync();

        var request = new CreateOrderRequest
        {
            Items =
            {
                new CreateOrderItemRequest
                {
                    ProductId = 1,
                    Quantity = 1
                }
            }
        };

        var service = new OrderService(context);

        var result = await service.CreateAsync(userId, request);

        Assert.Equal(
            OrderServiceStatus.InvalidRequest,
            result.Status);

        Assert.Null(result.Order);

        Assert.False(await context.Orders.AnyAsync());
    }

    // Null вместо запроса.
    [Fact]
    public async Task CreateAsync_NullRequest_ReturnsInvalidRequest()
    {
        await using var connection = await CreateConnectionAsync();
        await using var context = CreateContext(connection);

        await context.Database.EnsureCreatedAsync();

        var service = new OrderService(context);

        var result = await service.CreateAsync(1, null!);

        Assert.Equal(
            OrderServiceStatus.InvalidRequest,
            result.Status);

        Assert.Null(result.Order);

        Assert.False(await context.Orders.AnyAsync());
    }

    // Null вместо списка товаров.
    [Fact]
    public async Task CreateAsync_NullItems_ReturnsInvalidRequest()
    {
        await using var connection = await CreateConnectionAsync();
        await using var context = CreateContext(connection);

        await context.Database.EnsureCreatedAsync();

        var request = new CreateOrderRequest
        {
            Items = null!
        };

        var service = new OrderService(context);

        var result = await service.CreateAsync(1, request);

        Assert.Equal(
            OrderServiceStatus.InvalidRequest,
            result.Status);

        Assert.Null(result.Order);

        Assert.False(await context.Orders.AnyAsync());
    }

    // Null вместо отдельной позиции заказа.
    [Fact]
    public async Task CreateAsync_NullItem_ReturnsInvalidRequest()
    {
        await using var connection = await CreateConnectionAsync();
        await using var context = CreateContext(connection);

        await context.Database.EnsureCreatedAsync();

        var request = new CreateOrderRequest
        {
            Items =
            {
                null!
            }
        };

        var service = new OrderService(context);

        var result = await service.CreateAsync(1, request);

        Assert.Equal(
            OrderServiceStatus.InvalidRequest,
            result.Status);

        Assert.Null(result.Order);

        Assert.False(await context.Orders.AnyAsync());
    }

    // Название товара длиннее допустимых 200 символов.
    [Fact]
    public async Task CreateAsync_ProductNameTooLong_ReturnsInvalidRequest()
    {
        await using var connection = await CreateConnectionAsync();
        await using var context = CreateContext(connection);

        await context.Database.EnsureCreatedAsync();

        var user = new AppUser
        {
            Username = "testuser",
            NormalizedUsername = "TESTUSER",
            PasswordHash = "test-password-hash"
        };

        var longName = new string('A', 201);

        var product = new Product
        {
            Name = longName,
            Price = 1250m,
            Quantity = 5
        };

        context.AppUsers.Add(user);
        context.Products.Add(product);

        await context.SaveChangesAsync();

        var request = new CreateOrderRequest
        {
            Items =
            {
                new CreateOrderItemRequest
                {
                    ProductId = product.Id,
                    Quantity = 1
                }
            }
        };

        var service = new OrderService(context);

        var result = await service.CreateAsync(user.Id, request);

        Assert.Equal(
            OrderServiceStatus.InvalidRequest,
            result.Status);

        Assert.Equal(product.Id, result.ProductId);
        Assert.Null(result.Order);

        Assert.False(await context.Orders.AnyAsync());
        Assert.False(await context.OrderItems.AnyAsync());

        var unchangedProduct = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == product.Id);

        Assert.Equal(5, unchangedProduct.Quantity);
    }

    // Создание открытого SQLite-соединения.
    private static async Task<SqliteConnection> CreateConnectionAsync()
    {
        var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        return connection;
    }

    // Создание контекста EF Core для SQLite.
    private static StoreDbContext CreateContext(
        SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseSqlite(connection)
            .Options;

        return new StoreDbContext(options);
    }
}