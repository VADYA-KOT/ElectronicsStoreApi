
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.DTOs.Response;
using ElectronicsStoreApi.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ElectronicsStoreApi.Tests;

public class CategorySpecificationsApiIntegrationTests
{
    private const string Password = "TestPassword_12345";

    // ================================================
    // ПОЛУЧЕНИЕ ХАРАКТЕРИСТИК
    // ================================================

    [Fact]
    public async Task GetAll_ExistingCategory_ReturnsSpecificationsOrderedBySortOrder()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await CreateCategoryAsync(factory);

        await CreateSpecificationAsync(
            factory, categoryId, "storage", "Накопитель", "ГБ", 30);

        await CreateSpecificationAsync(
            factory, categoryId, "processor", "Процессор", null, 10);

        await CreateSpecificationAsync(
            factory, categoryId, "ram", "Оперативная память", "ГБ", 20);

        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/categories/{categoryId}/specifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var specifications =
            await response.Content.ReadFromJsonAsync<
                List<CategorySpecificationResponse>>();

        Assert.NotNull(specifications);
        Assert.Equal(3, specifications.Count);

        Assert.Equal(
            new[] { "processor", "ram", "storage" },
            specifications.Select(s => s.Code).ToArray());

        Assert.Equal(
            categoryId,
            specifications[0].CategoryId);
    }

    [Fact]
    public async Task GetAll_MissingCategory_ReturnsNotFound()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/categories/99999999/specifications");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    // ================================================
    // СОЗДАНИЕ ХАРАКТЕРИСТИК
    // ================================================

    [Fact]
    public async Task Create_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await CreateCategoryAsync(factory);

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/categories/{categoryId}/specifications",
            CreateRequest("ram", "Оперативная память", "ГБ", 10));

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task Create_RegularUser_ReturnsForbidden()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await CreateCategoryAsync(factory);

        using var client =
            await CreateAuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            $"/api/categories/{categoryId}/specifications",
            CreateRequest("ram", "Оперативная память", "ГБ", 10));

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    [Fact]
    public async Task Create_AdminUser_CreatesSpecification()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await CreateCategoryAsync(factory);

        using var client =
            await CreateAuthenticatedClientAsync(factory, admin: true);

        var response = await client.PostAsJsonAsync(
            $"/api/categories/{categoryId}/specifications",
            CreateRequest("RAM", "Оперативная память", "ГБ", 10));

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var specification =
            await response.Content.ReadFromJsonAsync<
                CategorySpecificationResponse>();

        Assert.NotNull(specification);

        Assert.True(specification.Id > 0);
        Assert.Equal(categoryId, specification.CategoryId);
        Assert.Equal("ram", specification.Code);
        Assert.Equal("Оперативная память", specification.Name);
        Assert.Equal("ГБ", specification.Unit);
        Assert.Equal(10, specification.SortOrder);

        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        var saved = await context.CategorySpecifications
            .AsNoTracking()
            .SingleAsync(s => s.Id == specification.Id);

        Assert.Equal("ram", saved.Code);
    }

    [Fact]
    public async Task Create_DuplicateCodeInSameCategory_ReturnsConflict()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await CreateCategoryAsync(factory);

        await CreateSpecificationAsync(
            factory, categoryId, "ram", "Оперативная память", "ГБ", 10);

        using var client =
            await CreateAuthenticatedClientAsync(factory, admin: true);

        var response = await client.PostAsJsonAsync(
            $"/api/categories/{categoryId}/specifications",
            CreateRequest("RAM", "Другая память", "ГБ", 20));

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
    }

    [Fact]
    public async Task Create_SameCodeInDifferentCategories_ReturnsCreated()
    {
        using var factory = new TestApplicationFactory();

        int firstCategoryId = await CreateCategoryAsync(factory);
        int secondCategoryId = await CreateCategoryAsync(factory);

        await CreateSpecificationAsync(
            factory,
            firstCategoryId,
            "ram",
            "Оперативная память",
            "ГБ",
            10);

        using var client =
            await CreateAuthenticatedClientAsync(factory, admin: true);

        var response = await client.PostAsJsonAsync(
            $"/api/categories/{secondCategoryId}/specifications",
            CreateRequest("ram", "Оперативная память", "ГБ", 10));

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
    }

    [Fact]
    public async Task Create_MissingCategory_ReturnsNotFound()
    {
        using var factory = new TestApplicationFactory();

        using var client =
            await CreateAuthenticatedClientAsync(factory, admin: true);

        var response = await client.PostAsJsonAsync(
            "/api/categories/99999999/specifications",
            CreateRequest("ram", "Оперативная память", "ГБ", 10));

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidCode_ReturnsBadRequest()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await CreateCategoryAsync(factory);

        using var client =
            await CreateAuthenticatedClientAsync(factory, admin: true);

        var response = await client.PostAsJsonAsync(
            $"/api/categories/{categoryId}/specifications",
            CreateRequest("ram space", "Оперативная память", "ГБ", 10));

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task Create_NegativeSortOrder_ReturnsBadRequest()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await CreateCategoryAsync(factory);

        using var client =
            await CreateAuthenticatedClientAsync(factory, admin: true);

        var response = await client.PostAsJsonAsync(
            $"/api/categories/{categoryId}/specifications",
            CreateRequest("ram", "Оперативная память", "ГБ", -1));

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    // ================================================
    // РЕДАКТИРОВАНИЕ ХАРАКТЕРИСТИК
    // ================================================

    [Fact]
    public async Task Update_ExistingSpecification_UpdatesProperties()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await CreateCategoryAsync(factory);

        int specificationId = await CreateSpecificationAsync(
            factory,
            categoryId,
            "ram",
            "Память",
            "ГБ",
            10);

        using var client =
            await CreateAuthenticatedClientAsync(factory, admin: true);

        var response = await client.PutAsJsonAsync(
            $"/api/categories/{categoryId}/specifications/{specificationId}",
            CreateRequest("RAM", "Оперативная память", "ГБ", 20));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated =
            await response.Content.ReadFromJsonAsync<
                CategorySpecificationResponse>();

        Assert.NotNull(updated);
        Assert.Equal(specificationId, updated.Id);
        Assert.Equal("ram", updated.Code);
        Assert.Equal("Оперативная память", updated.Name);
        Assert.Equal(20, updated.SortOrder);

        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        var saved = await context.CategorySpecifications
            .AsNoTracking()
            .SingleAsync(s => s.Id == specificationId);

        Assert.Equal("Оперативная память", saved.Name);
        Assert.Equal(20, saved.SortOrder);
    }

    [Fact]
    public async Task Update_MissingSpecification_ReturnsNotFound()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await CreateCategoryAsync(factory);

        using var client =
            await CreateAuthenticatedClientAsync(factory, admin: true);

        var response = await client.PutAsJsonAsync(
            $"/api/categories/{categoryId}/specifications/99999999",
            CreateRequest("ram", "Оперативная память", "ГБ", 10));

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task Update_DuplicateCodeInCategory_ReturnsConflict()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await CreateCategoryAsync(factory);

        await CreateSpecificationAsync(
            factory, categoryId, "ram", "Оперативная память", "ГБ", 10);

        int secondId = await CreateSpecificationAsync(
            factory, categoryId, "storage", "Накопитель", "ГБ", 20);

        using var client =
            await CreateAuthenticatedClientAsync(factory, admin: true);

        var response = await client.PutAsJsonAsync(
            $"/api/categories/{categoryId}/specifications/{secondId}",
            CreateRequest("RAM", "Накопитель", "ГБ", 20));

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
    }

    // ================================================
    // УДАЛЕНИЕ ХАРАКТЕРИСТИК
    // ================================================

    [Fact]
    public async Task Delete_UnusedSpecification_ReturnsNoContent()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await CreateCategoryAsync(factory);

        int specificationId = await CreateSpecificationAsync(
            factory, categoryId, "ram", "Оперативная память", "ГБ", 10);

        using var client =
            await CreateAuthenticatedClientAsync(factory, admin: true);

        var response = await client.DeleteAsync(
            $"/api/categories/{categoryId}/specifications/{specificationId}");

        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);

        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        Assert.False(
            await context.CategorySpecifications
                .AnyAsync(s => s.Id == specificationId));
    }

    [Fact]
    public async Task Delete_UsedSpecification_ReturnsConflict()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await CreateCategoryAsync(factory);

        int specificationId = await CreateSpecificationAsync(
            factory, categoryId, "ram", "Оперативная память", "ГБ", 10);

        using (var scope = factory.Services.CreateScope())
        {
            var context =
                scope.ServiceProvider.GetRequiredService<StoreDbContext>();

            var product = new Product
            {
                Name = "Test Laptop",
                Price = 2500,
                Quantity = 5,
                CategoryId = categoryId
            };

            context.Products.Add(product);
            await context.SaveChangesAsync();

            context.ProductSpecifications.Add(
                new ProductSpecification
                {
                    ProductId = product.Id,
                    CategorySpecificationId = specificationId,
                    Value = "16"
                });

            await context.SaveChangesAsync();
        }

        using var client =
            await CreateAuthenticatedClientAsync(factory, admin: true);

        var response = await client.DeleteAsync(
            $"/api/categories/{categoryId}/specifications/{specificationId}");

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);

        using var verificationScope = factory.Services.CreateScope();

        var verificationContext =
            verificationScope.ServiceProvider
                .GetRequiredService<StoreDbContext>();

        Assert.True(
            await verificationContext.CategorySpecifications
                .AnyAsync(s => s.Id == specificationId));
    }

    [Fact]
    public async Task Delete_MissingSpecification_ReturnsNotFound()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await CreateCategoryAsync(factory);

        using var client =
            await CreateAuthenticatedClientAsync(factory, admin: true);

        var response = await client.DeleteAsync(
            $"/api/categories/{categoryId}/specifications/99999999");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    // ================================================
    // АВТОРИЗАЦИЯ ПРИ ИЗМЕНЕНИИ И УДАЛЕНИИ
    // ================================================

    [Fact]
    public async Task UpdateAndDelete_RegularUser_ReturnForbidden()
    {
        using var factory = new TestApplicationFactory();

        int categoryId = await CreateCategoryAsync(factory);

        int specificationId = await CreateSpecificationAsync(
            factory, categoryId, "ram", "Оперативная память", "ГБ", 10);

        using var client =
            await CreateAuthenticatedClientAsync(factory);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/categories/{categoryId}/specifications/{specificationId}",
            CreateRequest("ram2", "Новая память", "ГБ", 20));

        Assert.Equal(
            HttpStatusCode.Forbidden,
            updateResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync(
            $"/api/categories/{categoryId}/specifications/{specificationId}");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            deleteResponse.StatusCode);
    }

    // ================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ================================================

    private static object CreateRequest(
        string code,
        string name,
        string? unit,
        int sortOrder)
    {
        return new
        {
            code,
            name,
            unit,
            sortOrder
        };
    }

    private static async Task<int> CreateCategoryAsync(
        TestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        var category = new Category
        {
            Name = $"TestCategory_{Guid.NewGuid():N}"
        };

        context.Categories.Add(category);

        await context.SaveChangesAsync();

        return category.Id;
    }

    private static async Task<int> CreateSpecificationAsync(
        TestApplicationFactory factory,
        int categoryId,
        string code,
        string name,
        string? unit,
        int sortOrder)
    {
        using var scope = factory.Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        var specification = new CategorySpecification
        {
            CategoryId = categoryId,
            Code = code,
            Name = name,
            Unit = unit,
            SortOrder = sortOrder
        };

        context.CategorySpecifications.Add(specification);

        await context.SaveChangesAsync();

        return specification.Id;
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        TestApplicationFactory factory,
        bool admin = false)
    {
        var client = factory.CreateClient();

        string username = $"spec_{Guid.NewGuid():N}";

        var credentials = new
        {
            username,
            password = Password
        };

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            credentials);

        Assert.Equal(
            HttpStatusCode.Created,
            registerResponse.StatusCode);

        if (admin)
        {
            using var scope = factory.Services.CreateScope();

            var context =
                scope.ServiceProvider.GetRequiredService<StoreDbContext>();

            var normalizedUsername = username.ToUpperInvariant();

            var user = await context.AppUsers.SingleAsync(
                u => u.NormalizedUsername == normalizedUsername);

            user.Role = "Admin";

            await context.SaveChangesAsync();
        }

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            credentials);

        Assert.Equal(
            HttpStatusCode.OK,
            loginResponse.StatusCode);

        var token =
            await loginResponse.Content.ReadFromJsonAsync<
                AuthTokenResponse>();

        Assert.NotNull(token);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token.AccessToken);

        return client;
    }
}
