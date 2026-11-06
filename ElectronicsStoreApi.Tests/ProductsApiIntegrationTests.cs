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

public class ProductsApiIntegrationTests
{
    [Fact]
    public async Task CreateProduct_EmptyName_ReturnsBadRequest()
    {
        using var factory = new TestApplicationFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var request = new
        {
            name = "",
            price = 2500,
            quantity = 5,
            categoryId = (int?)null,
            description = "Integration test"
        };

        var response = await client.PostAsJsonAsync(
            "/api/products", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_UnknownCategory_ReturnsBadRequest()
    {
        using var factory = new TestApplicationFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var request = new
        {
            name = "Test Mouse",
            price = 2500,
            quantity = 5,
            categoryId = 999,
            description = "Integration test"
        };

        var response = await client.PostAsJsonAsync(
            "/api/products", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAndGetProduct_ReturnsCreatedProduct()
    {
        using var factory = new TestApplicationFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        int id = await CreateProductAsync(client);

        var response = await client.GetAsync($"/api/products/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var product =
            await response.Content.ReadFromJsonAsync<ProductResponse>();

        Assert.NotNull(product);
        Assert.Equal(id, product.Id);
        Assert.Equal("Integration Mouse", product.Name);
        Assert.Equal(2500m, product.Price);
    }

    [Fact]
    public async Task GetById_MissingProduct_ReturnsNotFound()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/products/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProduct_ExistingProduct_ReturnsNoContent()
    {
        using var factory = new TestApplicationFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        int id = await CreateProductAsync(client);

        var request = new
        {
            name = "Updated Integration Mouse",
            price = 2800,
            quantity = 7,
            categoryId = (int?)null,
            description = "Updated description"
        };

        var response = await client.PutAsJsonAsync(
            $"/api/products/{id}", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await client.GetAsync($"/api/products/{id}");

        var product =
            await getResponse.Content.ReadFromJsonAsync<ProductResponse>();

        Assert.NotNull(product);
        Assert.Equal("Updated Integration Mouse", product.Name);
        Assert.Equal(2800m, product.Price);
        Assert.Equal(7, product.Quantity);
    }

    [Fact]
    public async Task DeleteProduct_ExistingProduct_ReturnsNoContent()
    {
        using var factory = new TestApplicationFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        int id = await CreateProductAsync(client);

        var deleteResponse = await client.DeleteAsync(
            $"/api/products/{id}");

        Assert.Equal(
            HttpStatusCode.NoContent,
            deleteResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/products/{id}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            getResponse.StatusCode);
    }

    [Fact]
    public async Task GetAll_InvalidPageSize_ReturnsBadRequest()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();

        var response =
            await client.GetAsync("/api/products?pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetByCategory_ReturnsProductsInCategory()
    {
        using var factory = new TestApplicationFactory();

        using (var scope = factory.Services.CreateScope())
        {
            var context =
                scope.ServiceProvider.GetRequiredService<StoreDbContext>();

            context.Categories.Add(new Category
            {
                Id = 4,
                Name = "Аксессуары"
            });

            await context.SaveChangesAsync();
        }

        using var client = await CreateAuthenticatedClientAsync(factory);

        int id = await CreateProductAsync(client, categoryId: 4);

        var response =
            await client.GetAsync("/api/products/by-category/4");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var products =
            await response.Content.ReadFromJsonAsync<List<ProductResponse>>();

        Assert.NotNull(products);
        Assert.Contains(products, p => p.Id == id);
    }

    [Fact]
    public async Task CreateProduct_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();

        var request = new
        {
            name = "Unauthorised test",
            price = 100,
            quantity = 1,
            categoryId = (int?)null,
            description = "No token"
        };

        var response = await client.PostAsJsonAsync(
            "/api/products", request);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    private static async Task<int> CreateProductAsync(
        HttpClient client,
        int? categoryId = null)
    {
        var request = new
        {
            name = "Integration Mouse",
            price = 2500,
            quantity = 5,
            categoryId,
            description = "Integration test"
        };

        var response = await client.PostAsJsonAsync(
            "/api/products", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var product =
            await response.Content.ReadFromJsonAsync<ProductResponse>();

        Assert.NotNull(product);
        Assert.True(product.Id > 0);

        return product.Id;
    }


    [Fact]
    public async Task CreateProduct_RegularUserToken_ReturnsForbidden()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();

        string username = $"user_{Guid.NewGuid():N}";
        const string password = "TestPassword_12345";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { username, password });

        Assert.Equal(
            HttpStatusCode.Created,
            registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username, password });

        Assert.Equal(
            HttpStatusCode.OK,
            loginResponse.StatusCode);

        var token =
            await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        Assert.NotNull(token);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var request = new
        {
            name = "Forbidden Test Product",
            price = 100,
            quantity = 1,
            categoryId = (int?)null,
            description = "Must not be created"
        };

        var response = await client.PostAsJsonAsync(
            "/api/products",
            request);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        TestApplicationFactory factory)
    {
        var client = factory.CreateClient();

        string username = $"test_{Guid.NewGuid():N}";
        const string password = "TestPassword_12345";

        // Регистрируем обычного пользователя.
        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { username, password });

        Assert.Equal(
            HttpStatusCode.Created,
            registerResponse.StatusCode);

        // Только в изолированной тестовой БД повышаем пользователя до Admin.
        using (var scope = factory.Services.CreateScope())
        {
            var context =
                scope.ServiceProvider.GetRequiredService<StoreDbContext>();

            var normalizedUsername = username.ToUpperInvariant();

            var user = await context.AppUsers.SingleAsync(
                u => u.NormalizedUsername == normalizedUsername);

            user.Role = "Admin";

            await context.SaveChangesAsync();
        }

        // Получаем токен уже с административной ролью.
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username, password });

        Assert.Equal(
            HttpStatusCode.OK,
            loginResponse.StatusCode);

        var token =
            await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        Assert.NotNull(token);
        Assert.False(string.IsNullOrWhiteSpace(token.AccessToken));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);

        return client;
    }

}
