
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ElectronicsStoreApi.Tests;

public class OrdersControllerResultMappingIntegrationTests
{
    // ================================================
    // INVALID REQUEST
    // ================================================

    [Fact]
    public async Task CreateOrder_ProductNameTooLong_ReturnsBadRequest()
    {
        using var factory = new SqliteTestApplicationFactory();

        var user = await CreateUserAsync(factory);
        using var client = user.Client;

        int productId = await SeedProductAsync(
            factory,
            new string('A', 201),
            100m,
            5);

        var request = new
        {
            items = new[]
            {
                new
                {
                    productId,
                    quantity = 1
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
    // CREATE ORDER CONCURRENCY CONFLICT
    // ================================================

    [Fact]
    public async Task CreateOrder_SerializationFailure_ReturnsConflict()
    {
        var interceptor = new SerializationFailureInterceptor();

        using var factory =
            new SqliteTestApplicationFactory(interceptor);

        var user = await CreateUserAsync(factory);
        using var client = user.Client;

        int productId = await SeedProductAsync(
            factory,
            "Test Mouse",
            1250m,
            5);

        // Ошибку включаем только перед целевым запросом.
        interceptor.ShouldFail = true;

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
            HttpStatusCode.Conflict,
            response.StatusCode);

        // Проверяем, что транзакция не сохранила изменения.
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
    // UPDATE ORDER STATUS CONCURRENCY CONFLICT
    // ================================================

    [Fact]
    public async Task UpdateOrderStatus_SerializationFailure_ReturnsConflict()
    {
        var interceptor = new SerializationFailureInterceptor();

        using var factory =
            new SqliteTestApplicationFactory(interceptor);

        var admin = await CreateUserAsync(
            factory,
            isAdmin: true);

        using var client = admin.Client;

        int orderId = await SeedOrderAsync(
            factory,
            admin.UserId,
            "Pending");

        interceptor.ShouldFail = true;

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/status",
            new
            {
                status = "Processing"
            });

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);

        // Статус не должен измениться после неудачного сохранения.
        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        var order = await context.Orders
            .AsNoTracking()
            .SingleAsync(o => o.Id == orderId);

        Assert.Equal("Pending", order.Status);
    }

    // ================================================
    // TEST HELPERS
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

        string username = $"mapping_{Guid.NewGuid():N}";
        const string password = "TestPassword_12345";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username,
                password
            });

        string registrationContent =
            await registerResponse.Content.ReadAsStringAsync();

        Assert.True(
            registerResponse.StatusCode == HttpStatusCode.Created,
            $"Registration failed. HTTP: {(int)registerResponse.StatusCode}. Response: {registrationContent}");

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

        var token = await loginResponse.Content
            .ReadFromJsonAsync<ElectronicsStoreApi.DTOs.Response.AuthTokenResponse>();

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
        string status)
    {
        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        var order = new Order
        {
            UserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            Status = status,
            TotalAmount = 0m
        };

        context.Orders.Add(order);

        await context.SaveChangesAsync();

        return order.Id;
    }

    // ================================================
    // FAILURE SIMULATION
    // ================================================

    private sealed class SerializationFailureInterceptor
        : SaveChangesInterceptor
    {
        public bool ShouldFail { get; set; }

        public override ValueTask<InterceptionResult<int>>
            SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                throw new DbUpdateException(
                    "Simulated PostgreSQL serialization failure.",
                    new PostgresException(
                        "Serialization failure",
                        "ERROR",
                        "ERROR",
                        PostgresErrorCodes.SerializationFailure));
            }

            return base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }
    }
}
