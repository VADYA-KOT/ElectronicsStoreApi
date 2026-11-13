
using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElectronicsStoreApi.Tests;

public class ProductsFilteringIntegrationTests
    : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;

    public ProductsFilteringIntegrationTests(
        TestApplicationFactory factory)
    {
        _factory = factory;
    }

    // ==================================================
    // КОМБИНИРОВАННАЯ ФИЛЬТРАЦИЯ И ПАГИНАЦИЯ
    // ==================================================

    [Fact]
    public async Task GetProducts_FiltersSortsAndPaginatesCorrectly()
    {
        int categoryId = await CreateCategoryWithProductsAsync(
            new Product
            {
                Name = "FilterTest_Cheap",
                Price = 10m,
                Quantity = 0
            },
            new Product
            {
                Name = "FilterTest_Medium",
                Price = 25m,
                Quantity = 5
            },
            new Product
            {
                Name = "FilterTest_Expensive",
                Price = 45m,
                Quantity = 10
            });

        using var client = _factory.CreateClient();

        var firstPage = await GetProductsAsync(
            client,
            $"/api/products?categoryId={categoryId}" +
            "&minPrice=20&maxPrice=50" +
            "&sortBy=price&descending=true" +
            "&page=1&pageSize=1");

        Assert.Equal(
            2,
            firstPage.RootElement.GetProperty("totalCount").GetInt32());

        Assert.Equal(
            2,
            firstPage.RootElement.GetProperty("totalPages").GetInt32());

        var firstItems = firstPage.RootElement.GetProperty("items");

        Assert.Single(firstItems.EnumerateArray());

        Assert.Equal(
            45m,
            firstItems[0].GetProperty("price").GetDecimal());

        var secondPage = await GetProductsAsync(
            client,
            $"/api/products?categoryId={categoryId}" +
            "&minPrice=20&maxPrice=50" +
            "&sortBy=price&descending=true" +
            "&page=2&pageSize=1");

        Assert.Equal(
            2,
            secondPage.RootElement.GetProperty("totalCount").GetInt32());

        var secondItems = secondPage.RootElement.GetProperty("items");

        Assert.Single(secondItems.EnumerateArray());

        Assert.Equal(
            25m,
            secondItems[0].GetProperty("price").GetDecimal());
    }

    // ==================================================
    // ФИЛЬТРАЦИЯ ПО МАКСИМАЛЬНОМУ КОЛИЧЕСТВУ
    // ==================================================

    [Fact]
    public async Task GetProducts_MaxQuantity_FiltersProductsCorrectly()
    {
        int categoryId = await CreateCategoryWithProductsAsync(
            new Product
            {
                Name = "Quantity_Zero",
                Price = 100m,
                Quantity = 0
            },
            new Product
            {
                Name = "Quantity_Five",
                Price = 200m,
                Quantity = 5
            },
            new Product
            {
                Name = "Quantity_Ten",
                Price = 300m,
                Quantity = 10
            });

        using var client = _factory.CreateClient();

        var json = await GetProductsAsync(
            client,
            $"/api/products?categoryId={categoryId}&maxQuantity=5");

        var root = json.RootElement;
        var items = root.GetProperty("items");

        Assert.Equal(2, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, items.GetArrayLength());

        foreach (var item in items.EnumerateArray())
        {
            Assert.True(
                item.GetProperty("quantity").GetInt32() <= 5);
        }
    }

    // ==================================================
    // ФИЛЬТРАЦИЯ ТОВАРОВ В НАЛИЧИИ
    // ==================================================

    [Fact]
    public async Task GetProducts_InStockOnly_ExcludesProductsWithoutStock()
    {
        int categoryId = await CreateCategoryWithProductsAsync(
            new Product
            {
                Name = "Stock_Zero",
                Price = 100m,
                Quantity = 0
            },
            new Product
            {
                Name = "Stock_Five",
                Price = 200m,
                Quantity = 5
            },
            new Product
            {
                Name = "Stock_Ten",
                Price = 300m,
                Quantity = 10
            });

        using var client = _factory.CreateClient();

        var json = await GetProductsAsync(
            client,
            $"/api/products?categoryId={categoryId}&inStockOnly=true");

        var root = json.RootElement;
        var items = root.GetProperty("items");

        Assert.Equal(2, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, items.GetArrayLength());

        foreach (var item in items.EnumerateArray())
        {
            Assert.True(
                item.GetProperty("quantity").GetInt32() > 0);
        }
    }

    // ==================================================
    // СОРТИРОВКА
    // ==================================================

    [Theory]
    [InlineData("id", false, "Zulu|Alpha|Mike")]
    [InlineData("id", true, "Mike|Alpha|Zulu")]

    [InlineData("name", false, "Alpha|Mike|Zulu")]
    [InlineData("name", true, "Zulu|Mike|Alpha")]

    [InlineData("price", false, "Mike|Zulu|Alpha")]
    [InlineData("price", true, "Alpha|Zulu|Mike")]

    [InlineData("quantity", false, "Alpha|Zulu|Mike")]
    [InlineData("quantity", true, "Mike|Zulu|Alpha")]
    public async Task GetProducts_SortsCorrectly(
        string sortBy,
        bool descending,
        string expectedNames)
    {
        int categoryId = await CreateCategoryWithProductsAsync(
            new Product
            {
                Name = "Zulu",
                Price = 20m,
                Quantity = 5
            },
            new Product
            {
                Name = "Alpha",
                Price = 30m,
                Quantity = 1
            },
            new Product
            {
                Name = "Mike",
                Price = 10m,
                Quantity = 9
            });

        using var client = _factory.CreateClient();

        var json = await GetProductsAsync(
            client,
            $"/api/products?categoryId={categoryId}" +
            $"&sortBy={sortBy}" +
            $"&descending={descending.ToString().ToLowerInvariant()}" +
            "&page=1&pageSize=10");

        string[] expected = expectedNames.Split('|');

        string[] actual = ReadProductNames(json);

        Assert.Equal(expected, actual);
    }

    // ==================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ==================================================

    private async Task<int> CreateCategoryWithProductsAsync(
        params Product[] products)
    {
        int categoryId = Random.Shared.Next(
            100000,
            2000000000);

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<StoreDbContext>();

        db.Categories.Add(new Category
        {
            Id = categoryId,
            Name = $"FilterTest_{Guid.NewGuid():N}"
        });

        await db.SaveChangesAsync();

        foreach (var product in products)
        {
            product.CategoryId = categoryId;
        }

        db.Products.AddRange(products);

        await db.SaveChangesAsync();

        return categoryId;
    }

    private static async Task<JsonDocument> GetProductsAsync(
        HttpClient client,
        string requestUri)
    {
        using var response = await client.GetAsync(requestUri);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        string content =
            await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(content);
    }

    private static string[] ReadProductNames(JsonDocument json)
    {
        return json.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(item =>
                item.GetProperty("name").GetString()!)
            .ToArray();
    }
}
