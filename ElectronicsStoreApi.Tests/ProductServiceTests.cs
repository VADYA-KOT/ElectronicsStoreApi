using Microsoft.EntityFrameworkCore;
using Xunit;
using System.Linq;
using ElectronicsStoreApi.Services;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.DTOs.Requests;


namespace ElectronicsStoreApi.Tests;

public class ProductServiceTests
{

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesProduct()
    {
        await using var context = CreateContext();

        context.Categories.Add(new Category
        {
            Id = 4,
            Name = "Accessories"
        });

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var request = new CreateProductRequest
        {
            Name = "Test Mouse",
            Price = 2500,
            Quantity = 5,
            CategoryId = 4,
            Description = "Test description"
        };

        var result = await service.CreateAsync(request);

        Assert.Equal(ProductServiceStatus.Success, result.Status);
        Assert.NotNull(result.Product);
        Assert.Equal("Test Mouse", result.Product.Name);
        Assert.Equal(4, result.Product.CategoryId);
        Assert.Single(await context.Products.ToListAsync());
    }

    [Fact]
    public async Task UpdateAsync_ExistingProduct_UpdatesProperties()
    {
        await using var context = CreateContext();

        context.Categories.Add(new Category
        {
            Id = 4,
            Name = "Accessories"
        });

        context.Products.Add(new Product
        {
            Id = 4,
            Name = "Old Mouse",
            Price = 1000,
            Quantity = 2,
            CategoryId = 4
        });

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var request = new UpdateProductRequest
        {
            Name = "Updated Mouse",
            Price = 2500,
            Quantity = 10,
            CategoryId = 4,
            Description = "Updated description"
        };

        var status = await service.UpdateAsync(4, request);

        Assert.Equal(ProductServiceStatus.Success, status);

        var product = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == 4);

        Assert.Equal("Updated Mouse", product.Name);
        Assert.Equal(2500m, product.Price);
        Assert.Equal(10, product.Quantity);
    }

    [Fact]
    public async Task UpdateAsync_MissingProduct_ReturnsNotFound()
    {
        await using var context = CreateContext();

        var service = new ProductService(context);

        var request = new UpdateProductRequest
        {
            Name = "Updated Mouse",
            Price = 2500,
            Quantity = 10
        };

        var status = await service.UpdateAsync(9999, request);

        Assert.Equal(
            ProductServiceStatus.ProductNotFound,
            status);
    }

    [Fact]
    public async Task UpdateAsync_MissingCategory_ReturnsCategoryNotFound()
    {
        await using var context = CreateContext();

        context.Products.Add(new Product
        {
            Id = 4,
            Name = "Old Mouse",
            Price = 1000,
            Quantity = 2
        });

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var request = new UpdateProductRequest
        {
            Name = "Updated Mouse",
            Price = 2500,
            Quantity = 10,
            CategoryId = 999
        };

        var status = await service.UpdateAsync(4, request);

        Assert.Equal(
            ProductServiceStatus.CategoryNotFound,
            status);

        var product = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == 4);

        Assert.Equal("Old Mouse", product.Name);
        Assert.Equal(1000m, product.Price);
    }

    private static StoreDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new StoreDbContext(options);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingProduct_ReturnsProduct()
    {
        await using var context = CreateContext();

        context.Products.Add(new Product
        {
            Id = 4,
            Name = "USB Hub",
            Price = 2100,
            Quantity = 8
        });

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var result = await service.GetByIdAsync(4);

        Assert.NotNull(result);
        Assert.Equal(4, result.Id);
        Assert.Equal("USB Hub", result.Name);
        Assert.Equal(2100m, result.Price);
    }

    [Fact]
    public async Task GetByIdAsync_MissingProduct_ReturnsNull()
    {
        await using var context = CreateContext();

        var service = new ProductService(context);

        var result = await service.GetByIdAsync(9999);

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_ExistingProduct_DeletesProduct()
    {
        await using var context = CreateContext();

        context.Products.Add(new Product
        {
            Id = 4,
            Name = "USB Hub",
            Price = 2100,
            Quantity = 8
        });

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var status = await service.DeleteAsync(4);

        Assert.Equal(ProductServiceStatus.Success, status);

        bool stillExists = await context.Products
            .AnyAsync(p => p.Id == 4);

        Assert.False(stillExists);
    }

    [Fact]
    public async Task DeleteAsync_MissingProduct_ReturnsNotFound()
    {
        await using var context = CreateContext();

        var service = new ProductService(context);

        var status = await service.DeleteAsync(9999);

        Assert.Equal(
            ProductServiceStatus.ProductNotFound,
            status);
    }

    [Fact]
    public async Task CreateAsync_MissingCategory_ReturnsCategoryNotFound()
    {
        await using var context = CreateContext();

        var service = new ProductService(context);

        var request = new CreateProductRequest
        {
            Name = "Test Mouse",
            Price = 2500,
            Quantity = 5,
            CategoryId = 999
        };

        var result = await service.CreateAsync(request);

        Assert.Equal(
            ProductServiceStatus.CategoryNotFound,
            result.Status);

        Assert.False(await context.Products.AnyAsync());
    }
}
