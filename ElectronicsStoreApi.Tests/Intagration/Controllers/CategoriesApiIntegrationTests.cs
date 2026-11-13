
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

public class CategoriesApiIntegrationTests
{
    // ================================================
    // GET CATEGORIES
    // ================================================

    [Fact]
    public async Task GetAll_ReturnsCategoriesWithProductCounts()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await SeedCategoryAsync(
            factory,
            $"Audio_{Guid.NewGuid():N}");

        await SeedProductsAsync(factory, categoryId, 2);

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var categories =
            await response.Content.ReadFromJsonAsync<List<CategoryResponse>>();

        Assert.NotNull(categories);

        var category = Assert.Single(
            categories,
            c => c.Id == categoryId);

        Assert.Equal(2, category.ProductCount);
    }

    [Fact]
    public async Task GetById_ExistingCategory_ReturnsCategory()
    {
        using var factory = new TestApplicationFactory();

        string name = $"Audio_{Guid.NewGuid():N}";

        int categoryId = await SeedCategoryAsync(factory, name);

        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/categories/{categoryId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var category =
            await response.Content.ReadFromJsonAsync<CategoryResponse>();

        Assert.NotNull(category);
        Assert.Equal(categoryId, category.Id);
        Assert.Equal(name, category.Name);
        Assert.Equal(0, category.ProductCount);
    }

    [Fact]
    public async Task GetById_MissingCategory_ReturnsNotFound()
    {
        using var factory = new TestApplicationFactory();

        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/categories/999999");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    // ================================================
    // CREATE CATEGORY
    // ================================================

    [Fact]
    public async Task Create_AdminTrimsNameAndReturnsCreated()
    {
        using var factory = new TestApplicationFactory();

        using var client = await CreateAdminClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/categories",
            new
            {
                name = "  Audio Devices  "
            });

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var category =
            await response.Content.ReadFromJsonAsync<CategoryResponse>();

        Assert.NotNull(category);
        Assert.True(category.Id > 0);
        Assert.Equal("Audio Devices", category.Name);
        Assert.Equal(0, category.ProductCount);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        using var factory = new TestApplicationFactory();

        string name = $"Audio_{Guid.NewGuid():N}";

        await SeedCategoryAsync(factory, name);

        using var client = await CreateAdminClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/categories",
            new { name });

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
    }

    [Fact]
    public async Task Create_WhitespaceName_ReturnsBadRequest()
    {
        using var factory = new TestApplicationFactory();

        using var client = await CreateAdminClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/categories",
            new
            {
                name = "   "
            });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task Create_NameTooLong_ReturnsBadRequest()
    {
        using var factory = new TestApplicationFactory();

        using var client = await CreateAdminClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/categories",
            new
            {
                name = new string('A', 101)
            });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    // ================================================
    // UPDATE CATEGORY
    // ================================================

    [Fact]
    public async Task Update_ExistingCategory_UpdatesNameAndPreservesProductCount()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await SeedCategoryAsync(
            factory,
            $"Old_{Guid.NewGuid():N}");

        await SeedProductsAsync(factory, categoryId, 2);

        using var client = await CreateAdminClientAsync(factory);

        var response = await client.PutAsJsonAsync(
            $"/api/categories/{categoryId}",
            new
            {
                name = "  Updated Audio  "
            });

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var category =
            await response.Content.ReadFromJsonAsync<CategoryResponse>();

        Assert.NotNull(category);
        Assert.Equal(categoryId, category.Id);
        Assert.Equal("Updated Audio", category.Name);
        Assert.Equal(2, category.ProductCount);
    }

    [Fact]
    public async Task Update_DuplicateName_ReturnsConflict()
    {
        using var factory = new TestApplicationFactory();

        int firstId = await SeedCategoryAsync(
            factory,
            $"First_{Guid.NewGuid():N}");

        int secondId = await SeedCategoryAsync(
            factory,
            $"Second_{Guid.NewGuid():N}");

        string secondName;

        using (var scope = factory.Services.CreateScope())
        {
            var context =
                scope.ServiceProvider.GetRequiredService<StoreDbContext>();

            secondName = await context.Categories
                .Where(c => c.Id == secondId)
                .Select(c => c.Name)
                .SingleAsync();
        }

        using var client = await CreateAdminClientAsync(factory);

        var response = await client.PutAsJsonAsync(
            $"/api/categories/{firstId}",
            new
            {
                name = secondName
            });

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
    }

    [Fact]
    public async Task Update_MissingCategory_ReturnsNotFound()
    {
        using var factory = new TestApplicationFactory();

        using var client = await CreateAdminClientAsync(factory);

        var response = await client.PutAsJsonAsync(
            "/api/categories/999999",
            new
            {
                name = "Updated Category"
            });

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    // ================================================
    // DELETE CATEGORY
    // ================================================

    [Fact]
    public async Task Delete_EmptyCategory_ReturnsNoContent()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await SeedCategoryAsync(
            factory,
            $"Delete_{Guid.NewGuid():N}");

        using var client = await CreateAdminClientAsync(factory);

        var deleteResponse = await client.DeleteAsync(
            $"/api/categories/{categoryId}");

        Assert.Equal(
            HttpStatusCode.NoContent,
            deleteResponse.StatusCode);

        var getResponse = await client.GetAsync(
            $"/api/categories/{categoryId}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_CategoryWithProducts_ReturnsConflict()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await SeedCategoryAsync(
            factory,
            $"Used_{Guid.NewGuid():N}");

        await SeedProductsAsync(factory, categoryId, 1);

        using var client = await CreateAdminClientAsync(factory);

        var response = await client.DeleteAsync(
            $"/api/categories/{categoryId}");

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);

        // Категория должна остаться в базе данных.
        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        Assert.True(
            await context.Categories.AnyAsync(
                c => c.Id == categoryId));
    }

    [Fact]
    public async Task Delete_MissingCategory_ReturnsNotFound()
    {
        using var factory = new TestApplicationFactory();

        using var client = await CreateAdminClientAsync(factory);

        var response = await client.DeleteAsync(
            "/api/categories/999999");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    // ================================================
    // AUTHORIZATION
    // ================================================

    [Fact]
    public async Task UpdateAndDelete_RegularUser_ReturnForbidden()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await SeedCategoryAsync(
            factory,
            $"Protected_{Guid.NewGuid():N}");

        using var client = await CreateRegularUserClientAsync(factory);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/categories/{categoryId}",
            new
            {
                name = "Changed Name"
            });

        Assert.Equal(
            HttpStatusCode.Forbidden,
            updateResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync(
            $"/api/categories/{categoryId}");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            deleteResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateAndDelete_WithoutToken_ReturnUnauthorized()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await SeedCategoryAsync(
            factory,
            $"Protected_{Guid.NewGuid():N}");

        using var client = factory.CreateClient();

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/categories/{categoryId}",
            new
            {
                name = "Changed Name"
            });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            updateResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync(
            $"/api/categories/{categoryId}");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            deleteResponse.StatusCode);
    }

    // ================================================
    // TEST DATA HELPERS
    // ================================================

    private static async Task<int> SeedCategoryAsync(
        TestApplicationFactory factory,
        string name)
    {
        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        var category = new Category
        {
            Name = name
        };

        context.Categories.Add(category);

        await context.SaveChangesAsync();

        return category.Id;
    }

    private static async Task SeedProductsAsync(
        TestApplicationFactory factory,
        int categoryId,
        int count)
    {
        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        for (int i = 0; i < count; i++)
        {
            context.Products.Add(new Product
            {
                Name = $"Test Product {Guid.NewGuid():N}",
                Price = 100m,
                Quantity = 5,
                CategoryId = categoryId
            });
        }

        await context.SaveChangesAsync();
    }

    // ================================================
    // AUTHENTICATION HELPERS
    // ================================================

    private static async Task<HttpClient> CreateRegularUserClientAsync(
        TestApplicationFactory factory)
    {
        var client = factory.CreateClient();

        string username = $"user_{Guid.NewGuid():N}";
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

        return client;
    }

    private static async Task<HttpClient> CreateAdminClientAsync(
        TestApplicationFactory factory)
    {
        var client = factory.CreateClient();

        string username = $"admin_{Guid.NewGuid():N}";
        const string password = "TestPassword_12345";

        // Сначала регистрируем обычного пользователя.
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

        // В изолированной тестовой БД назначаем роль Admin.
        using (var scope = factory.Services.CreateScope())
        {
            var context =
                scope.ServiceProvider.GetRequiredService<StoreDbContext>();

            var user = await context.AppUsers.SingleAsync(
                u => u.NormalizedUsername ==
                     username.ToUpperInvariant());

            user.Role = "Admin";

            await context.SaveChangesAsync();
        }

        // Выполняем вход после изменения роли.
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

        return client;
    }
}
