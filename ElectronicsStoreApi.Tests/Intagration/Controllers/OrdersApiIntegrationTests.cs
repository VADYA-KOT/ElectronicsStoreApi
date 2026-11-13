
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.DTOs.Response;
using ElectronicsStoreApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElectronicsStoreApi.Tests;

public class OrdersApiIntegrationTests
{
    // ================================================
    // УСПЕШНОЕ СОЗДАНИЕ ЗАКАЗА
    // ================================================

    [Fact]
    public async Task CreateOrder_ValidRequest_ReturnsCreatedAndDecreasesStock()
    {
        using var factory = new SqliteTestApplicationFactory();

        var auth = await CreateAuthenticatedClientAsync(factory);
        using var client = auth.Client;

        int productId = await SeedProductAsync(
            factory,
            "Test Mouse",
            1250m,
            5);

        var request = new
        {
            items = new[]
            {
                new
                {
                    productId,
                    quantity = 2
                }
            }
        };

        var response = await client.PostAsJsonAsync(
            "/api/orders",
            request);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var order =
            await response.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.NotNull(order);

        Assert.True(order.Id > 0);
        Assert.Equal("Pending", order.Status);
        Assert.Equal(2500m, order.TotalAmount);

        var item = Assert.Single(order.Items);

        Assert.Equal(productId, item.ProductId);
        Assert.Equal("Test Mouse", item.ProductName);
        Assert.Equal(1250m, item.UnitPrice);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(2500m, item.LineTotal);

        // Проверяем данные в базе.
        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        var savedOrder = await context.Orders
            .Include(o => o.Items)
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(order.Id, savedOrder.Id);
        Assert.Equal(auth.UserId, savedOrder.UserId);
        Assert.Equal(2500m, savedOrder.TotalAmount);
        Assert.Equal("Pending", savedOrder.Status);
        Assert.Single(savedOrder.Items);

        // Проверяем уменьшение остатка товара.
        var updatedProduct = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == productId);

        Assert.Equal(3, updatedProduct.Quantity);
    }

    // ================================================
    // НЕСКОЛЬКО ТОВАРОВ В ОДНОМ ЗАКАЗЕ
    // ================================================

    [Fact]
    public async Task CreateOrder_MultipleProducts_CalculatesTotalAndDecreasesStock()
    {
        using var factory = new SqliteTestApplicationFactory();

        var auth = await CreateAuthenticatedClientAsync(factory);
        using var client = auth.Client;

        int mouseId = await SeedProductAsync(
            factory,
            "Test Mouse",
            1250m,
            5);

        int keyboardId = await SeedProductAsync(
            factory,
            "Test Keyboard",
            3000m,
            3);

        var request = new
        {
            items = new[]
            {
                new
                {
                    productId = mouseId,
                    quantity = 2
                },
                new
                {
                    productId = keyboardId,
                    quantity = 1
                }
            }
        };

        var response = await client.PostAsJsonAsync(
            "/api/orders",
            request);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var order =
            await response.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.NotNull(order);

        Assert.Equal(5500m, order.TotalAmount);
        Assert.Equal(2, order.Items.Count);

        Assert.Equal(2500m, order.Items[0].LineTotal);
        Assert.Equal(3000m, order.Items[1].LineTotal);

        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        var mouse = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == mouseId);

        var keyboard = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == keyboardId);

        Assert.Equal(3, mouse.Quantity);
        Assert.Equal(2, keyboard.Quantity);
    }

    // ================================================
    // НЕДОСТАТОЧНЫЙ ОСТАТОК
    // ================================================

    [Fact]
    public async Task CreateOrder_InsufficientStock_ReturnsConflictWithoutChangingData()
    {
        using var factory = new SqliteTestApplicationFactory();

        var auth = await CreateAuthenticatedClientAsync(factory);
        using var client = auth.Client;

        int productId = await SeedProductAsync(
            factory,
            "Test Mouse",
            1250m,
            2);

        var request = new
        {
            items = new[]
            {
                new
                {
                    productId,
                    quantity = 3
                }
            }
        };

        var response = await client.PostAsJsonAsync(
            "/api/orders",
            request);

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);

        // Проверяем описание ошибки.
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var items = document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .ToList();

        var problem = Assert.Single(items);

        Assert.Equal(
            productId,
            problem.GetProperty("productId").GetInt32());

        Assert.Equal(
            3,
            problem.GetProperty("requestedQuantity").GetInt32());

        Assert.Equal(
            2,
            problem.GetProperty("availableQuantity").GetInt32());

        // Проверяем, что заказ не появился.
        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        Assert.False(await context.Orders.AnyAsync());
        Assert.False(await context.OrderItems.AnyAsync());

        // Остаток должен остаться неизменным.
        var product = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == productId);

        Assert.Equal(2, product.Quantity);
    }

    // ================================================
    // НЕСУЩЕСТВУЮЩИЙ ТОВАР
    // ================================================

    [Fact]
    public async Task CreateOrder_MissingProduct_ReturnsNotFound()
    {
        using var factory = new SqliteTestApplicationFactory();

        var auth = await CreateAuthenticatedClientAsync(factory);
        using var client = auth.Client;

        var request = new
        {
            items = new[]
            {
                new
                {
                    productId = 999999,
                    quantity = 1
                }
            }
        };

        var response = await client.PostAsJsonAsync(
            "/api/orders",
            request);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        Assert.False(await context.Orders.AnyAsync());
        Assert.False(await context.OrderItems.AnyAsync());
    }

    // ================================================
    // ДУБЛИРОВАНИЕ ТОВАРА В ЗАКАЗЕ
    // ================================================

    [Fact]
    public async Task CreateOrder_DuplicateProduct_ReturnsBadRequest()
    {
        using var factory = new SqliteTestApplicationFactory();

        var auth = await CreateAuthenticatedClientAsync(factory);
        using var client = auth.Client;

        int productId = await SeedProductAsync(
            factory,
            "Test Mouse",
            1250m,
            5);

        var request = new
        {
            items = new[]
            {
                new
                {
                    productId,
                    quantity = 1
                },
                new
                {
                    productId,
                    quantity = 2
                }
            }
        };

        var response = await client.PostAsJsonAsync(
            "/api/orders",
            request);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        Assert.False(await context.Orders.AnyAsync());

        var product = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == productId);

        Assert.Equal(5, product.Quantity);
    }

    // ================================================
    // ПУСТОЙ СПИСОК ТОВАРОВ
    // ================================================

    [Fact]
    public async Task CreateOrder_EmptyItems_ReturnsBadRequest()
    {
        using var factory = new SqliteTestApplicationFactory();

        var auth = await CreateAuthenticatedClientAsync(factory);
        using var client = auth.Client;

        var request = new
        {
            items = Array.Empty<object>()
        };

        var response = await client.PostAsJsonAsync(
            "/api/orders",
            request);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        Assert.False(await context.Orders.AnyAsync());
    }

    // ================================================
    // НЕКОРРЕКТНОЕ КОЛИЧЕСТВО
    // ================================================

    [Fact]
    public async Task CreateOrder_ZeroQuantity_ReturnsBadRequest()
    {
        using var factory = new SqliteTestApplicationFactory();

        var auth = await CreateAuthenticatedClientAsync(factory);
        using var client = auth.Client;

        int productId = await SeedProductAsync(
            factory,
            "Test Mouse",
            1250m,
            5);

        var request = new
        {
            items = new[]
            {
                new
                {
                    productId,
                    quantity = 0
                }
            }
        };

        var response = await client.PostAsJsonAsync(
            "/api/orders",
            request);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        Assert.False(await context.Orders.AnyAsync());

        var product = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == productId);

        Assert.Equal(5, product.Quantity);
    }

    // ================================================
    // АВТОРИЗАЦИЯ
    // ================================================

    [Fact]
    public async Task CreateOrder_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new SqliteTestApplicationFactory();

        await factory.InitializeDatabaseAsync();

        using var client = factory.CreateClient();

        var request = new
        {
            items = new[]
            {
                new
                {
                    productId = 1,
                    quantity = 1
                }
            }
        };

        var response = await client.PostAsJsonAsync(
            "/api/orders",
            request);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    // ================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ================================================

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

    private static async Task<(HttpClient Client, int UserId)>
        CreateAuthenticatedClientAsync(
            SqliteTestApplicationFactory factory)
    {
        await factory.InitializeDatabaseAsync();

        var client = factory.CreateClient();

        string username = $"order_{Guid.NewGuid():N}";
        const string password = "TestPassword_12345";

        // Регистрируем пользователя.
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

        // Получаем ID пользователя из тестовой БД.
        int userId;

        using (var scope = factory.Services.CreateScope())
        {
            var context =
                scope.ServiceProvider.GetRequiredService<StoreDbContext>();

            var user = await context.AppUsers.SingleAsync(
                u => u.NormalizedUsername ==
                     username.ToUpperInvariant());

            userId = user.Id;
        }

        // Авторизуемся.
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

        return (client, userId);
    }
}
