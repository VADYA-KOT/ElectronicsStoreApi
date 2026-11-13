using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ElectronicsStoreApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElectronicsStoreApi.Tests;

public class CategoriesAuthorizationIntegrationTests
    : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;

    public CategoriesAuthorizationIntegrationTests(
        TestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateCategory_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var category = new
        {
            id = Random.Shared.Next(100000, 2000000000),
            name = $"TestCategory_{Guid.NewGuid():N}"
        };

        var response = await client.PostAsJsonAsync(
            "/api/categories",
            category);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_WithUserToken_Returns403()
    {
        using var client = _factory.CreateClient();

        var username =
            $"cat_user_{Guid.NewGuid():N}"[..20];

        var credentials = new
        {
            username,
            password = "TestPassword_12345"
        };

        // Регистрируем обычного пользователя.
        var registration = await client.PostAsJsonAsync(
            "/api/auth/register",
            credentials);

        Assert.Equal(
            HttpStatusCode.Created,
            registration.StatusCode);

        // Выполняем вход и получаем JWT.
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            credentials);

        Assert.Equal(
            HttpStatusCode.OK,
            login.StatusCode);

        using var json = JsonDocument.Parse(
            await login.Content.ReadAsStringAsync());

        var token = json.RootElement
            .GetProperty("accessToken")
            .GetString();

        Assert.False(
            string.IsNullOrWhiteSpace(token));

        // Прикрепляем токен к запросам клиента.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);

        var category = new
        {
            id = Random.Shared.Next(100000, 2000000000),
            name = $"TestCategory_{Guid.NewGuid():N}"
        };

        var response = await client.PostAsJsonAsync(
            "/api/categories",
            category);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_WithAdminToken_Returns201()
    {
        using var client = _factory.CreateClient();

        var username =
            $"cat_admin_{Guid.NewGuid():N}"[..20];

        var credentials = new
        {
            username,
            password = "TestPassword_12345"
        };

        // 1. Регистрируем пользователя.
        var registration = await client.PostAsJsonAsync(
            "/api/auth/register",
            credentials);

        Assert.Equal(
            HttpStatusCode.Created,
            registration.StatusCode);

        // 2. Назначаем пользователю роль Admin
        // в тестовой базе данных.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<StoreDbContext>();

            var normalizedUsername =
                username.ToUpperInvariant();

            var user = await db.AppUsers.SingleAsync(
                u => u.NormalizedUsername == normalizedUsername);

            user.Role = "Admin";

            await db.SaveChangesAsync();
        }

        // 3. Выполняем вход после изменения роли,
        // чтобы JWT содержал роль Admin.
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            credentials);

        Assert.Equal(
            HttpStatusCode.OK,
            login.StatusCode);

        using var json = JsonDocument.Parse(
            await login.Content.ReadAsStringAsync());

        var token = json.RootElement
            .GetProperty("accessToken")
            .GetString();

        Assert.False(
            string.IsNullOrWhiteSpace(token));

        // 4. Добавляем JWT в заголовок авторизации.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);

        // 5. Отправляем запрос на создание категории.
        var category = new
        {
            id = Random.Shared.Next(100000, 2000000000),
            name = $"AdminCategory_{Guid.NewGuid():N}"
        };

        var response = await client.PostAsJsonAsync(
            "/api/categories",
            category);

        // Администратор должен успешно создать категорию.
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
    }
}