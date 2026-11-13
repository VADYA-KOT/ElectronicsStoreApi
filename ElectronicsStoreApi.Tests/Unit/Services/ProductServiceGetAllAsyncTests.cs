
using System;
using System.Linq;
using System.Threading.Tasks;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElectronicsStoreApi.Tests;

public class ProductServiceGetAllAsyncTests
{
    // ==================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ==================================================

    private static StoreDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseInMemoryDatabase(
                $"ProductServiceTests_{Guid.NewGuid():N}")
            .Options;

        return new StoreDbContext(options);
    }

    private static Product CreateProduct(
        int id,
        string name,
        decimal price,
        int quantity,
        int? categoryId = null)
    {
        return new Product
        {
            Id = id,
            Name = name,
            Price = price,
            Quantity = quantity,
            CategoryId = categoryId
        };
    }

    private static Product[] CreateSortProducts()
    {
        return
        [
            CreateProduct(30, "Gamma", 300, 2),
            CreateProduct(10, "Alpha", 100, 5),
            CreateProduct(20, "Beta", 200, 5)
        ];
    }

    // ==================================================
    // ФИЛЬТРАЦИЯ
    // ==================================================

    [Fact]
    public async Task GetAllAsync_CategoryId_ReturnsOnlyProductsFromSelectedCategory()
    {
        await using var context = CreateContext();

        var laptops = new Category
        {
            Id = 1,
            Name = "Laptops"
        };

        var phones = new Category
        {
            Id = 2,
            Name = "Phones"
        };

        context.Categories.AddRange(laptops, phones);
        await context.SaveChangesAsync();

        context.Products.AddRange(
            CreateProduct(1, "Laptop_A", 1500, 5, laptops.Id),
            CreateProduct(2, "Laptop_B", 2000, 3, laptops.Id),
            CreateProduct(3, "Phone_A", 800, 10, phones.Id));

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var result = await service.GetAllAsync(
            search: null,
            categoryId: laptops.Id,
            minPrice: null,
            maxPrice: null,
            maxQuantity: null,
            sortBy: "id",
            descending: false,
            inStockOnly: false,
            page: 1,
            pageSize: 10);

        Assert.Equal(2, result.TotalCount);

        Assert.Equal(
            new[] { "Laptop_A", "Laptop_B" },
            result.Items.Select(p => p.Name).ToArray());

        Assert.All(
            result.Items,
            product => Assert.Equal("Laptops", product.CategoryName));
    }

    [Fact]
    public async Task GetAllAsync_MinPrice_ReturnsProductsAtOrAboveMinimum()
    {
        await using var context = CreateContext();

        context.Products.AddRange(
            CreateProduct(1, "Cheap", 100, 5),
            CreateProduct(2, "Boundary", 200, 5),
            CreateProduct(3, "Expensive", 300, 5));

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var result = await service.GetAllAsync(
            search: null,
            categoryId: null,
            minPrice: 200,
            maxPrice: null,
            maxQuantity: null,
            sortBy: "id",
            descending: false,
            inStockOnly: false,
            page: 1,
            pageSize: 10);

        Assert.Equal(2, result.TotalCount);

        Assert.Equal(
            new[] { "Boundary", "Expensive" },
            result.Items.Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task GetAllAsync_MaxPrice_ReturnsProductsAtOrBelowMaximum()
    {
        await using var context = CreateContext();

        context.Products.AddRange(
            CreateProduct(1, "Cheap", 100, 5),
            CreateProduct(2, "Boundary", 200, 5),
            CreateProduct(3, "Expensive", 300, 5));

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var result = await service.GetAllAsync(
            search: null,
            categoryId: null,
            minPrice: null,
            maxPrice: 200,
            maxQuantity: null,
            sortBy: "id",
            descending: false,
            inStockOnly: false,
            page: 1,
            pageSize: 10);

        Assert.Equal(2, result.TotalCount);

        Assert.Equal(
            new[] { "Cheap", "Boundary" },
            result.Items.Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task GetAllAsync_MaxQuantity_ReturnsProductsAtOrBelowMaximum()
    {
        await using var context = CreateContext();

        context.Products.AddRange(
            CreateProduct(1, "LowQuantity", 100, 2),
            CreateProduct(2, "BoundaryQuantity", 200, 5),
            CreateProduct(3, "HighQuantity", 300, 10));

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var result = await service.GetAllAsync(
            search: null,
            categoryId: null,
            minPrice: null,
            maxPrice: null,
            maxQuantity: 5,
            sortBy: "id",
            descending: false,
            inStockOnly: false,
            page: 1,
            pageSize: 10);

        Assert.Equal(2, result.TotalCount);

        Assert.Equal(
            new[] { "LowQuantity", "BoundaryQuantity" },
            result.Items.Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task GetAllAsync_InStockOnly_ExcludesProductsWithZeroQuantity()
    {
        await using var context = CreateContext();

        context.Products.AddRange(
            CreateProduct(1, "OutOfStock", 100, 0),
            CreateProduct(2, "InStock", 200, 5),
            CreateProduct(3, "AlmostEmpty", 300, 1));

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var result = await service.GetAllAsync(
            search: null,
            categoryId: null,
            minPrice: null,
            maxPrice: null,
            maxQuantity: null,
            sortBy: "id",
            descending: false,
            inStockOnly: true,
            page: 1,
            pageSize: 10);

        Assert.Equal(2, result.TotalCount);

        Assert.Equal(
            new[] { "InStock", "AlmostEmpty" },
            result.Items.Select(p => p.Name).ToArray());

        Assert.All(
            result.Items,
            product => Assert.True(product.Quantity > 0));
    }

    // ==================================================
    // СОРТИРОВКА
    // ==================================================

    [Theory]
    [InlineData("id", false, "Alpha", "Beta", "Gamma")]
    [InlineData("id", true, "Gamma", "Beta", "Alpha")]
    [InlineData("name", false, "Alpha", "Beta", "Gamma")]
    [InlineData("name", true, "Gamma", "Beta", "Alpha")]
    [InlineData("price", false, "Alpha", "Beta", "Gamma")]
    [InlineData("price", true, "Gamma", "Beta", "Alpha")]
    [InlineData("quantity", false, "Gamma", "Alpha", "Beta")]
    [InlineData("quantity", true, "Alpha", "Beta", "Gamma")]
    public async Task GetAllAsync_SupportedSortBy_OrdersResultsCorrectly(
        string sortBy,
        bool descending,
        string first,
        string second,
        string third)
    {
        await using var context = CreateContext();

        context.Products.AddRange(CreateSortProducts());

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var result = await service.GetAllAsync(
            search: null,
            categoryId: null,
            minPrice: null,
            maxPrice: null,
            maxQuantity: null,
            sortBy: sortBy,
            descending: descending,
            inStockOnly: false,
            page: 1,
            pageSize: 10);

        Assert.Equal(3, result.TotalCount);

        Assert.Equal(
            new[] { first, second, third },
            result.Items.Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task GetAllAsync_UnsupportedSortBy_FallsBackToIdAscending()
    {
        await using var context = CreateContext();

        context.Products.AddRange(
            CreateProduct(20, "Product_20", 200, 5),
            CreateProduct(10, "Product_10", 100, 3));

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var result = await service.GetAllAsync(
            search: null,
            categoryId: null,
            minPrice: null,
            maxPrice: null,
            maxQuantity: null,
            sortBy: "unsupported",
            descending: true,
            inStockOnly: false,
            page: 1,
            pageSize: 10);

        Assert.Equal(2, result.TotalCount);

        Assert.Equal(
            new[] { "Product_10", "Product_20" },
            result.Items.Select(p => p.Name).ToArray());
    }

    // ==================================================
    // ПАГИНАЦИЯ
    // ==================================================

    [Fact]
    public async Task GetAllAsync_SecondPage_ReturnsCorrectItemsAndMetadata()
    {
        await using var context = CreateContext();

        context.Products.AddRange(
            CreateProduct(1, "Product_1", 100, 1),
            CreateProduct(2, "Product_2", 200, 2),
            CreateProduct(3, "Product_3", 300, 3),
            CreateProduct(4, "Product_4", 400, 4),
            CreateProduct(5, "Product_5", 500, 5));

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var result = await service.GetAllAsync(
            search: null,
            categoryId: null,
            minPrice: null,
            maxPrice: null,
            maxQuantity: null,
            sortBy: "id",
            descending: false,
            inStockOnly: false,
            page: 2,
            pageSize: 2);

        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.TotalPages);

        Assert.Equal(
            new[] { "Product_3", "Product_4" },
            result.Items.Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task GetAllAsync_PageBeyondAvailableItems_ReturnsEmptyItemsAndCorrectMetadata()
    {
        await using var context = CreateContext();

        context.Products.AddRange(
            CreateProduct(1, "Product_1", 100, 1),
            CreateProduct(2, "Product_2", 200, 2),
            CreateProduct(3, "Product_3", 300, 3));

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var result = await service.GetAllAsync(
            search: null,
            categoryId: null,
            minPrice: null,
            maxPrice: null,
            maxQuantity: null,
            sortBy: "id",
            descending: false,
            inStockOnly: false,
            page: 3,
            pageSize: 2);

        Assert.Equal(3, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.TotalPages);

        Assert.Empty(result.Items);
    }
}
