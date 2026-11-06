using System;
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

    [Fact]
    public async Task GetProducts_FiltersSortsAndPaginatesCorrectly()
    {
        // 1. Создаём отдельную категорию для тестовых товаров.
        var categoryId = Random.Shared.Next(100000, 2000000000);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<StoreDbContext>();

            db.Categories.Add(new Category
            {
                Id = categoryId,
                Name = $"FilterTest_{Guid.NewGuid():N}"
            });

            await db.SaveChangesAsync();

            // 2. Добавляем товары с разными ценами и остатками.
            db.Products.AddRange(
                new Product
                {
                    Name = "FilterTest_Cheap",
                    Price = 10m,
                    Quantity = 0,
                    CategoryId = categoryId
                },
                new Product
                {
                    Name = "FilterTest_Medium",
                    Price = 25m,
                    Quantity = 5,
                    CategoryId = categoryId
                },
                new Product
                {
                    Name = "FilterTest_Expensive",
                    Price = 45m,
                    Quantity = 10,
                    CategoryId = categoryId
                });

            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();

        // 3. Фильтруем товары по категории и диапазону цен.
        // Сортируем по цене от большей к меньшей.
        // Запрашиваем первую страницу размером в один товар.
        var firstPageResponse = await client.GetAsync(
            $"/api/products?categoryId={categoryId}" +
            "&minPrice=20&maxPrice=50" +
            "&sortBy=price&descending=true" +
            "&page=1&pageSize=1");

        Assert.Equal(
            HttpStatusCode.OK,
            firstPageResponse.StatusCode);

        using var firstPageJson = JsonDocument.Parse(
            await firstPageResponse.Content.ReadAsStringAsync());

        var firstPage = firstPageJson.RootElement;

        Assert.Equal(
            2,
            firstPage.GetProperty("totalCount").GetInt32());

        Assert.Equal(
            2,
            firstPage.GetProperty("totalPages").GetInt32());

        var firstItems = firstPage.GetProperty("items");

        Assert.Equal(1, firstItems.GetArrayLength());

        Assert.Equal(
            45m,
            firstItems[0].GetProperty("price").GetDecimal());

        // 4. Проверяем вторую страницу.
        var secondPageResponse = await client.GetAsync(
            $"/api/products?categoryId={categoryId}" +
            "&minPrice=20&maxPrice=50" +
            "&sortBy=price&descending=true" +
            "&page=2&pageSize=1");

        Assert.Equal(
            HttpStatusCode.OK,
            secondPageResponse.StatusCode);

        using var secondPageJson = JsonDocument.Parse(
            await secondPageResponse.Content.ReadAsStringAsync());

        var secondPage = secondPageJson.RootElement;

        Assert.Equal(
            2,
            secondPage.GetProperty("totalCount").GetInt32());

        var secondItems = secondPage.GetProperty("items");

        Assert.Equal(1, secondItems.GetArrayLength());

        Assert.Equal(
            25m,
            secondItems[0].GetProperty("price").GetDecimal());
    }
}