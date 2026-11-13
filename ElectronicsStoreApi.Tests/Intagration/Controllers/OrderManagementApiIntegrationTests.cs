
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.DTOs.Response;
using ElectronicsStoreApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElectronicsStoreApi.Tests;

public class OrderManagementApiIntegrationTests
{
    // ================================================
    // GET /api/orders
    // ================================================

    [Fact]
    public async Task GetMyOrders_ReturnsOnlyCurrentUsersOrdersInNewestFirst()
    {
        using var factory = new SqliteTestApplicationFactory();

        var firstUser = await CreateUserAsync(factory);
        var secondUser = await CreateUserAsync(factory);

        using var client = firstUser.Client;
        using var secondClient = secondUser.Client;

        int oldOrderId = await SeedOrderAsync(
            factory,
            firstUser.UserId,
            createdAt: DateTime.UtcNow.AddHours(-3));

        int newOrderId = await SeedOrderAsync(
            factory,
            firstUser.UserId,
            createdAt: DateTime.UtcNow.AddHours(-1));

        await SeedOrderAsync(
            factory,
            secondUser.UserId,
            createdAt: DateTime.UtcNow);

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var orders =
            await response.Content.ReadFromJsonAsync<List<OrderResponse>>();

        Assert.NotNull(orders);
        Assert.Equal(2, orders.Count);

        Assert.Equal(
            new[] { newOrderId, oldOrderId },
            orders.Select(o => o.Id).ToArray());
    }

    [Fact]
    public async Task GetMyOrders_WithoutOrders_ReturnsEmptyList()
    {
        using var factory = new SqliteTestApplicationFactory();

        var session = await CreateUserAsync(factory);
        using var client = session.Client;

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var orders =
            await response.Content.ReadFromJsonAsync<List<OrderResponse>>();

        Assert.NotNull(orders);
        Assert.Empty(orders);
    }

    [Fact]
    public async Task GetMyOrders_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new SqliteTestApplicationFactory();

        await factory.InitializeDatabaseAsync();

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    // ================================================
    // GET /api/orders/admin
    // ================================================

    [Fact]
    public async Task GetAllOrders_AdminReturnsAllOrdersInNewestFirst()
    {
        using var factory = new SqliteTestApplicationFactory();

        var firstUser = await CreateUserAsync(factory);
        var secondUser = await CreateUserAsync(factory);
        var admin = await CreateUserAsync(factory, isAdmin: true);

        using var firstClient = firstUser.Client;
        using var secondClient = secondUser.Client;
        using var client = admin.Client;

        int oldOrderId = await SeedOrderAsync(
            factory,
            firstUser.UserId,
            createdAt: DateTime.UtcNow.AddHours(-3));

        int middleOrderId = await SeedOrderAsync(
            factory,
            secondUser.UserId,
            createdAt: DateTime.UtcNow.AddHours(-2));

        int newOrderId = await SeedOrderAsync(
            factory,
            firstUser.UserId,
            createdAt: DateTime.UtcNow.AddHours(-1));

        var response = await client.GetAsync("/api/orders/admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var orders =
            await response.Content.ReadFromJsonAsync<List<OrderResponse>>();

        Assert.NotNull(orders);
        Assert.Equal(3, orders.Count);

        Assert.Equal(
            new[] { newOrderId, middleOrderId, oldOrderId },
            orders.Select(o => o.Id).ToArray());
    }

    [Fact]
    public async Task GetAllOrders_RegularUser_ReturnsForbidden()
    {
        using var factory = new SqliteTestApplicationFactory();

        var session = await CreateUserAsync(factory);
        using var client = session.Client;

        var response = await client.GetAsync("/api/orders/admin");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    [Fact]
    public async Task GetAllOrders_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new SqliteTestApplicationFactory();

        await factory.InitializeDatabaseAsync();

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders/admin");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    // ================================================
    // PATCH /api/orders/{id}/status
    // ================================================

    [Fact]
    public async Task UpdateOrderStatus_ValidTransitions_UpdatesStatus()
    {
        using var factory = new SqliteTestApplicationFactory();

        var user = await CreateUserAsync(factory);
        var admin = await CreateUserAsync(factory, isAdmin: true);

        using var userClient = user.Client;
        using var client = admin.Client;

        int orderId = await SeedOrderAsync(
            factory,
            user.UserId,
            status: "Pending");

        // Pending -> Processing.
        var processingResponse = await client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/status",
            new { status = " processing " });

        Assert.Equal(
            HttpStatusCode.OK,
            processingResponse.StatusCode);

        var processingOrder =
            await processingResponse.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.NotNull(processingOrder);
        Assert.Equal("Processing", processingOrder.Status);

        // Processing -> Shipped.
        var shippedResponse = await client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/status",
            new { status = "SHIPPED" });

        Assert.Equal(
            HttpStatusCode.OK,
            shippedResponse.StatusCode);

        var shippedOrder =
            await shippedResponse.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.NotNull(shippedOrder);
        Assert.Equal("Shipped", shippedOrder.Status);

        // Shipped -> Completed.
        var completedResponse = await client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/status",
            new { status = " completed " });

        Assert.Equal(
            HttpStatusCode.OK,
            completedResponse.StatusCode);

        var completedOrder =
            await completedResponse.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.NotNull(completedOrder);
        Assert.Equal("Completed", completedOrder.Status);
    }

    [Fact]
    public async Task UpdateOrderStatus_InvalidStatus_ReturnsBadRequest()
    {
        using var factory = new SqliteTestApplicationFactory();

        var user = await CreateUserAsync(factory);
        var admin = await CreateUserAsync(factory, isAdmin: true);

        using var userClient = user.Client;
        using var client = admin.Client;

        int orderId = await SeedOrderAsync(
            factory,
            user.UserId,
            status: "Pending");

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/status",
            new { status = "Waiting" });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        await AssertOrderStatusAsync(
            factory,
            orderId,
            "Pending");
    }

    [Fact]
    public async Task UpdateOrderStatus_InvalidTransition_ReturnsConflict()
    {
        using var factory = new SqliteTestApplicationFactory();

        var user = await CreateUserAsync(factory);
        var admin = await CreateUserAsync(factory, isAdmin: true);

        using var userClient = user.Client;
        using var client = admin.Client;

        int orderId = await SeedOrderAsync(
            factory,
            user.UserId,
            status: "Pending");

        // Pending -> Shipped запрещён.
        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/status",
            new { status = "Shipped" });

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);

        await AssertOrderStatusAsync(
            factory,
            orderId,
            "Pending");
    }

    [Fact]
    public async Task UpdateOrderStatus_MissingOrder_ReturnsNotFound()
    {
        using var factory = new SqliteTestApplicationFactory();

        var admin = await CreateUserAsync(factory, isAdmin: true);
        using var client = admin.Client;

        var response = await client.PatchAsJsonAsync(
            "/api/orders/999999/status",
            new { status = "Processing" });

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    // ================================================
    // ОТМЕНА ЗАКАЗА И ВОССТАНОВЛЕНИЕ ОСТАТКОВ
    // ================================================

    [Fact]
    public async Task UpdateOrderStatus_Cancelled_RestoresProductStock()
    {
        using var factory = new SqliteTestApplicationFactory();

        var user = await CreateUserAsync(factory);
        var admin = await CreateUserAsync(factory, isAdmin: true);

        using var userClient = user.Client;
        using var client = admin.Client;

        // На складе осталось 3 единицы после оформления заказа.
        int productId = await SeedProductAsync(
            factory,
            "Test Mouse",
            1250m,
            3);

        int orderId = await SeedOrderAsync(
            factory,
            user.UserId,
            status: "Processing",
            productId: productId,
            quantity: 2,
            addItem: true);

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/status",
            new { status = "Cancelled" });

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var order =
            await response.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.NotNull(order);
        Assert.Equal("Cancelled", order.Status);

        // Должно восстановиться 2 единицы: 3 + 2 = 5.
        var productQuantity = await GetProductQuantityAsync(
            factory,
            productId);

        Assert.Equal(5, productQuantity);

        await AssertOrderStatusAsync(
            factory,
            orderId,
            "Cancelled");
    }

    [Fact]
    public async Task UpdateOrderStatus_CancelledWithDeletedProduct_Succeeds()
    {
        using var factory = new SqliteTestApplicationFactory();

        var user = await CreateUserAsync(factory);
        var admin = await CreateUserAsync(factory, isAdmin: true);

        using var userClient = user.Client;
        using var client = admin.Client;

        // Товар уже удалён из каталога.
        // В OrderItem ProductId равен null, но история позиции остаётся.
        int orderId = await SeedOrderAsync(
            factory,
            user.UserId,
            status: "Pending",
            productId: null,
            quantity: 1,
            addItem: true);

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/status",
            new { status = "Cancelled" });

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var order =
            await response.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.NotNull(order);
        Assert.Equal("Cancelled", order.Status);

        var item = Assert.Single(order.Items);

        Assert.Null(item.ProductId);
        Assert.Equal("Deleted Product", item.ProductName);
    }

    [Fact]
    public async Task UpdateOrderStatus_CancelledInventoryOverflow_ReturnsConflict()
    {
        using var factory = new SqliteTestApplicationFactory();

        var user = await CreateUserAsync(factory);
        var admin = await CreateUserAsync(factory, isAdmin: true);

        using var userClient = user.Client;
        using var client = admin.Client;

        int productId = await SeedProductAsync(
            factory,
            "Test Mouse",
            1250m,
            int.MaxValue);

        int orderId = await SeedOrderAsync(
            factory,
            user.UserId,
            status: "Pending",
            productId: productId,
            quantity: 1,
            addItem: true);

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/status",
            new { status = "Cancelled" });

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);

        // Статус и остаток не должны измениться.
        await AssertOrderStatusAsync(
            factory,
            orderId,
            "Pending");

        var productQuantity = await GetProductQuantityAsync(
            factory,
            productId);

        Assert.Equal(int.MaxValue, productQuantity);
    }

    [Fact]
    public async Task UpdateOrderStatus_CompletedOrderCannotBeCancelled()
    {
        using var factory = new SqliteTestApplicationFactory();

        var user = await CreateUserAsync(factory);
        var admin = await CreateUserAsync(factory, isAdmin: true);

        using var userClient = user.Client;
        using var client = admin.Client;

        int productId = await SeedProductAsync(
            factory,
            "Test Mouse",
            1250m,
            3);

        int orderId = await SeedOrderAsync(
            factory,
            user.UserId,
            status: "Completed",
            productId: productId,
            quantity: 2,
            addItem: true);

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/status",
            new { status = "Cancelled" });

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);

        await AssertOrderStatusAsync(
            factory,
            orderId,
            "Completed");

        // Завершённый заказ не должен возвращать товар на склад.
        var productQuantity = await GetProductQuantityAsync(
            factory,
            productId);

        Assert.Equal(3, productQuantity);
    }

    // ================================================
    // АВТОРИЗАЦИЯ ИЗМЕНЕНИЯ СТАТУСА
    // ================================================

    [Fact]
    public async Task UpdateOrderStatus_RegularUser_ReturnsForbidden()
    {
        using var factory = new SqliteTestApplicationFactory();

        var user = await CreateUserAsync(factory);
        using var client = user.Client;

        int orderId = await SeedOrderAsync(
            factory,
            user.UserId,
            status: "Pending");

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/status",
            new { status = "Processing" });

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);

        await AssertOrderStatusAsync(
            factory,
            orderId,
            "Pending");
    }

    [Fact]
    public async Task UpdateOrderStatus_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new SqliteTestApplicationFactory();

        await factory.InitializeDatabaseAsync();

        using var client = factory.CreateClient();

        var response = await client.PatchAsJsonAsync(
            "/api/orders/1/status",
            new { status = "Processing" });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    // ================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ================================================

    private sealed record UserSession(
        HttpClient Client,
        int UserId);

    private static async Task<UserSession> CreateUserAsync(
        SqliteTestApplicationFactory factory,
        bool isAdmin = false)
    {
        await factory.InitializeDatabaseAsync();

        var client = factory.CreateClient();

        string username = $"om_{Guid.NewGuid():N}";
        const string password = "TestPassword_12345";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username,
                password
            });

        Assert.Equal(
            HttpStatusCode.Created,
            registerResponse.StatusCode);

        int userId;

        using (var scope = factory.Services.CreateScope())
        {
            var context =
                scope.ServiceProvider.GetRequiredService<StoreDbContext>();

            var user = await context.AppUsers.SingleAsync(
                u => u.NormalizedUsername ==
                     username.ToUpperInvariant());

            userId = user.Id;

            if (isAdmin)
            {
                user.Role = "Admin";
                await context.SaveChangesAsync();
            }
        }

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                username,
                password
            });

        Assert.Equal(
            HttpStatusCode.OK,
            loginResponse.StatusCode);

        var token =
            await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        Assert.NotNull(token);
        Assert.False(string.IsNullOrWhiteSpace(token.AccessToken));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token.AccessToken);

        return new UserSession(client, userId);
    }

    private static async Task<int> SeedProductAsync(
        SqliteTestApplicationFactory factory,
        string name,
        decimal price,
        int quantity)
    {
        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        var product = new Product
        {
            Name = name,
            Price = price,
            Quantity = quantity
        };

        context.Products.Add(product);

        await context.SaveChangesAsync();

        return product.Id;
    }

    private static async Task<int> SeedOrderAsync(
        SqliteTestApplicationFactory factory,
        int userId,
        string status = "Pending",
        DateTime? createdAt = null,
        int? productId = null,
        int quantity = 1,
        bool addItem = false)
    {
        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        var order = new Order
        {
            UserId = userId,
            CreatedAtUtc = createdAt ?? DateTime.UtcNow,
            Status = status,
            TotalAmount = addItem ? 100m * quantity : 0m
        };

        if (addItem)
        {
            order.Items.Add(new OrderItem
            {
                ProductId = productId,
                ProductName = productId.HasValue
                    ? "Test Mouse"
                    : "Deleted Product",
                UnitPrice = 100m,
                Quantity = quantity
            });
        }

        context.Orders.Add(order);

        await context.SaveChangesAsync();

        return order.Id;
    }

    private static async Task<int> GetProductQuantityAsync(
        SqliteTestApplicationFactory factory,
        int productId)
    {
        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        return await context.Products
            .Where(p => p.Id == productId)
            .Select(p => p.Quantity)
            .SingleAsync();
    }

    private static async Task AssertOrderStatusAsync(
        SqliteTestApplicationFactory factory,
        int orderId,
        string expectedStatus)
    {
        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        var order = await context.Orders
            .AsNoTracking()
            .SingleAsync(o => o.Id == orderId);

        Assert.Equal(expectedStatus, order.Status);
    }
}
