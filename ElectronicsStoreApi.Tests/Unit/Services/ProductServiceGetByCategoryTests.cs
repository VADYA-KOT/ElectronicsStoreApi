
using System;
using System.Linq;
using System.Threading.Tasks;
using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElectronicsStoreApi.Tests;

public class ProductServiceGetByCategoryTests
{
    private static StoreDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new StoreDbContext(options);
    }

    [Fact]
    public async Task GetByCategoryAsync_ExistingCategory_ReturnsOnlyItsProductsOrderedById()
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
            new Product
            {
                Id = 30,
                Name = "Laptop_B",
                Price = 2000,
                Quantity = 5,
                CategoryId = laptops.Id
            },
            new Product
            {
                Id = 10,
                Name = "Laptop_A",
                Price = 1500,
                Quantity = 3,
                CategoryId = laptops.Id
            },
            new Product
            {
                Id = 20,
                Name = "Phone_A",
                Price = 800,
                Quantity = 10,
                CategoryId = phones.Id
            });

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var result = await service.GetByCategoryAsync(laptops.Id);

        Assert.Equal(2, result.Count);

        Assert.Equal(
            new[] { 10, 30 },
            result.Select(p => p.Id).ToArray());

        Assert.Equal(
            new[] { "Laptop_A", "Laptop_B" },
            result.Select(p => p.Name).ToArray());

        Assert.All(
            result,
            product => Assert.Equal("Laptops", product.CategoryName));
    }

    [Fact]
    public async Task GetByCategoryAsync_CategoryWithoutProducts_ReturnsEmptyList()
    {
        await using var context = CreateContext();

        var emptyCategory = new Category
        {
            Id = 1,
            Name = "EmptyCategory"
        };

        var anotherCategory = new Category
        {
            Id = 2,
            Name = "AnotherCategory"
        };

        context.Categories.AddRange(emptyCategory, anotherCategory);

        context.Products.Add(new Product
        {
            Id = 10,
            Name = "AnotherProduct",
            Price = 1000,
            Quantity = 5,
            CategoryId = anotherCategory.Id
        });

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var result = await service.GetByCategoryAsync(emptyCategory.Id);

        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
