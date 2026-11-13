
using System;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElectronicsStoreApi.Tests;

public class ProductServicePostgreSqlSearchTests
{
    [Fact]
    public async Task GetAllAsync_SearchByProductName_ReturnsMatchingProduct()
    {
        await using var context = await CreateContextAsync();

        string token = Guid.NewGuid().ToString("N");

        var category = new Category
        {
            Name = $"UnrelatedCategory_{Guid.NewGuid():N}"
        };

        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var matchingProduct = new Product
        {
            Name = $"GamingMouse_{token}",
            Price = 150,
            Quantity = 5,
            CategoryId = category.Id
        };

        context.Products.AddRange(
            matchingProduct,
            new Product
            {
                Name = $"Keyboard_{Guid.NewGuid():N}",
                Price = 100,
                Quantity = 3,
                CategoryId = category.Id
            });

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var result = await service.GetAllAsync(
            search: token.ToUpperInvariant(),
            categoryId: null,
            minPrice: null,
            maxPrice: null,
            maxQuantity: null,
            sortBy: "id",
            descending: false,
            inStockOnly: false,
            page: 1,
            pageSize: 100);

        var product = Assert.Single(result.Items);

        Assert.Equal(matchingProduct.Name, product.Name);
    }

    [Fact]
    public async Task GetAllAsync_SearchByCategoryName_ReturnsMatchingProduct()
    {
        await using var context = await CreateContextAsync();

        string token = Guid.NewGuid().ToString("N");

        var category = new Category
        {
            Name = $"GamingAccessories_{token}"
        };

        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var matchingProduct = new Product
        {
            Name = $"OrdinaryProduct_{Guid.NewGuid():N}",
            Price = 200,
            Quantity = 4,
            CategoryId = category.Id
        };

        var productWithoutCategory = new Product
        {
            Name = $"UncategorizedProduct_{Guid.NewGuid():N}",
            Price = 100,
            Quantity = 2,
            CategoryId = null
        };

        context.Products.AddRange(
            matchingProduct,
            productWithoutCategory);

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var result = await service.GetAllAsync(
            search: token,
            categoryId: null,
            minPrice: null,
            maxPrice: null,
            maxQuantity: null,
            sortBy: "id",
            descending: false,
            inStockOnly: false,
            page: 1,
            pageSize: 100);

        var product = Assert.Single(result.Items);

        Assert.Equal(matchingProduct.Name, product.Name);
    }

    [Fact]
    public async Task GetAllAsync_SearchWithSurroundingWhitespace_TrimsSearchTerm()
    {
        await using var context = await CreateContextAsync();

        string token = Guid.NewGuid().ToString("N");

        context.Products.Add(new Product
        {
            Name = $"TrimSearch_{token}",
            Price = 300,
            Quantity = 5
        });

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var result = await service.GetAllAsync(
            search: $"  {token}  ",
            categoryId: null,
            minPrice: null,
            maxPrice: null,
            maxQuantity: null,
            sortBy: "id",
            descending: false,
            inStockOnly: false,
            page: 1,
            pageSize: 100);

        var product = Assert.Single(result.Items);

        Assert.Equal($"TrimSearch_{token}", product.Name);
    }

    [Fact]
    public async Task GetAllAsync_SearchWithoutMatches_ReturnsEmptyResult()
    {
        await using var context = await CreateContextAsync();

        var service = new ProductService(context);

        var result = await service.GetAllAsync(
            search: $"DefinitelyMissing_{Guid.NewGuid():N}",
            categoryId: null,
            minPrice: null,
            maxPrice: null,
            maxQuantity: null,
            sortBy: "id",
            descending: false,
            inStockOnly: false,
            page: 1,
            pageSize: 100);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    private static async Task<StoreDbContext> CreateContextAsync()
    {
        string? connectionString =
            Environment.GetEnvironmentVariable(
                "ELECTRONICS_STORE_TEST_CONNECTION");

        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            "Не задана переменная ELECTRONICS_STORE_TEST_CONNECTION.");

        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseNpgsql(connectionString!)
            .Options;

        var context = new StoreDbContext(options);

        try
        {
            await context.Database.EnsureCreatedAsync();
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }
}
