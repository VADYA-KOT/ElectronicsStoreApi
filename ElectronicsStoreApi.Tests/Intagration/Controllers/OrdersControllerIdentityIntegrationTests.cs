
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.Tasks;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.DTOs.Response;
using ElectronicsStoreApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ElectronicsStoreApi.Tests;

public class OrdersControllerIdentityIntegrationTests
{
    // ================================================
    // GET /api/orders
    // НЕКОРРЕКТНЫЙ ID ПОЛЬЗОВАТЕЛЯ В JWT
    // ================================================

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task GetMyOrders_InvalidUserIdClaim_ReturnsUnauthorized(
        string? userIdClaim)
    {
        using var factory = new SqliteTestApplicationFactory();

        await factory.InitializeDatabaseAsync();

        using var client = factory.CreateClient();

        string token = CreateToken(factory, userIdClaim);

        SetBearerToken(client, token);

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    // ================================================
    // POST /api/orders
    // НЕКОРРЕКТНЫЙ ID ПОЛЬЗОВАТЕЛЯ В JWT
    // ================================================

    [Fact]
    public async Task CreateOrder_InvalidUserIdClaim_ReturnsUnauthorized()
    {
        using var factory = new SqliteTestApplicationFactory();

        await factory.InitializeDatabaseAsync();

        using var client = factory.CreateClient();

        string token = CreateToken(factory, "not-a-number");

        SetBearerToken(client, token);

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
    // POST /api/orders
    // ПОЛЬЗОВАТЕЛЬ УДАЛЁН ПОСЛЕ АВТОРИЗАЦИИ
    // ================================================

    [Fact]
    public async Task CreateOrder_UserDeletedAfterLogin_ReturnsUnauthorized()
    {
        using var factory = new SqliteTestApplicationFactory();

        var session = await CreateAuthenticatedClientAsync(factory);

        using var client = session.Client;

        // Создаём товар.
        int productId = await SeedProductAsync(
            factory,
            "Test Mouse",
            1250m,
            5);

        // Удаляем пользователя после получения JWT.
        using (var scope = factory.Services.CreateScope())
        {
            var context =
                scope.ServiceProvider.GetRequiredService<StoreDbContext>();

            var user = await context.AppUsers
                .SingleAsync(u => u.Id == session.UserId);

            context.AppUsers.Remove(user);

            await context.SaveChangesAsync();
        }

        // Отправляем запрос со старым, но ещё действительным JWT.
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
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        // Проверяем, что заказ не создан и остаток не изменился.
        using var verificationScope = factory.Services.CreateScope();

        var verificationContext =
            verificationScope.ServiceProvider
                .GetRequiredService<StoreDbContext>();

        Assert.False(
            await verificationContext.Orders.AnyAsync());

        var product = await verificationContext.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == productId);

        Assert.Equal(5, product.Quantity);
    }

    // ================================================
    // JWT
    // ================================================

    private static string CreateToken(
        SqliteTestApplicationFactory factory,
        string? userIdClaim)
    {
        var configuration =
            factory.Services.GetRequiredService<IConfiguration>();

        string issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException(
                "Jwt:Issuer не настроен.");

        string audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException(
                "Jwt:Audience не настроен.");

        string encodedKey = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "Jwt:Key не настроен.");

        var key = new SymmetricSecurityKey(
            Convert.FromBase64String(encodedKey));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>();

        if (userIdClaim is not null)
        {
            claims.Add(
                new Claim(
                    JwtRegisteredClaimNames.Sub,
                    userIdClaim));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }

    private static void SetBearerToken(
        HttpClient client,
        string token)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    // ================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ================================================

    private sealed record UserSession(
        HttpClient Client,
        int UserId);

    private static async Task<UserSession> CreateAuthenticatedClientAsync(
        SqliteTestApplicationFactory factory)
    {
        await factory.InitializeDatabaseAsync();

        var client = factory.CreateClient();

        string username = $"order_{Guid.NewGuid():N}";

        const string password = "TestPassword_12345";

        // Регистрация.
        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username,
                password
            });

        string registerContent =
            await registerResponse.Content.ReadAsStringAsync();

        Assert.True(
            registerResponse.StatusCode == HttpStatusCode.Created,
            $"""
            Регистрация тестового пользователя завершилась ошибкой.
            Username: {username}
            HTTP Status: {(int)registerResponse.StatusCode}
            Response:
            {registerContent}
            """);

        // Получаем ID пользователя из БД.
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

        // Авторизация.
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

        Assert.False(
            string.IsNullOrWhiteSpace(token.AccessToken));

        SetBearerToken(client, token.AccessToken);

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
}
