using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.DTOs.Requests;
using ElectronicsStoreApi.Models;
using ElectronicsStoreApi.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

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

    // ================================================
    // СОЗДАНИЕ ТОВАРОВ С ХАРАКТЕРИСТИКАМИ
    // ================================================

    [Fact]
    public async Task CreateAsync_ValidSpecifications_CreatesProductAndReturnsMetadata()
    {
        await using var context = CreateContext();

        var category = await CreateTestCategoryAsync(context);

        var ram = await CreateTestSpecificationAsync(
            context, category.Id, "ram", "Оперативная память", "ГБ", 10);

        var storage = await CreateTestSpecificationAsync(
            context, category.Id, "storage", "Накопитель", "ГБ", 20);

        var service = new ProductService(context);

        var request = new CreateProductRequest
        {
            Name = "Test Laptop",
            Price = 2500,
            Quantity = 5,
            CategoryId = category.Id,

            Specifications = new List<ProductSpecificationRequest>
            {
                new()
                {
                    CategorySpecificationId = storage.Id,
                    Value = "512 ГБ"
                },
                new()
                {
                    CategorySpecificationId = ram.Id,
                    Value = " 16 ГБ "
                }
            }
        };

        var result = await service.CreateAsync(request);

        Assert.Equal(ProductServiceStatus.Success, result.Status);
        Assert.NotNull(result.Product);

        Assert.Equal(2, result.Product.Specifications.Count);

        Assert.Equal(
            new[] { "ram", "storage" },
            result.Product.Specifications.Select(s => s.Code).ToArray());

        var ramResult = result.Product.Specifications[0];

        Assert.Equal(ram.Id, ramResult.CategorySpecificationId);
        Assert.Equal("Оперативная память", ramResult.Name);
        Assert.Equal("ГБ", ramResult.Unit);
        Assert.Equal("16 ГБ", ramResult.Value);

        var storageResult = result.Product.Specifications[1];

        Assert.Equal(storage.Id, storageResult.CategorySpecificationId);
        Assert.Equal("Накопитель", storageResult.Name);
        Assert.Equal("512 ГБ", storageResult.Value);

        Assert.Equal(
            2,
            await context.ProductSpecifications.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_SpecificationFromAnotherCategory_ReturnsInvalidSpecifications()
    {
        await using var context = CreateContext();

        var firstCategory = await CreateTestCategoryAsync(context);
        var secondCategory = await CreateTestCategoryAsync(context);

        var foreignSpecification = await CreateTestSpecificationAsync(
            context,
            secondCategory.Id,
            "ram",
            "Оперативная память",
            "ГБ",
            10);

        var service = new ProductService(context);

        var request = new CreateProductRequest
        {
            Name = "Test Laptop",
            Price = 2500,
            Quantity = 5,
            CategoryId = firstCategory.Id,

            Specifications = new List<ProductSpecificationRequest>
            {
                new()
                {
                    CategorySpecificationId = foreignSpecification.Id,
                    Value = "16 ГБ"
                }
            }
        };

        var result = await service.CreateAsync(request);

        Assert.Equal(
            ProductServiceStatus.InvalidSpecifications,
            result.Status);

        Assert.False(await context.Products.AnyAsync());
        Assert.False(await context.ProductSpecifications.AnyAsync());
    }

    [Fact]
    public async Task CreateAsync_DuplicateSpecifications_ReturnsInvalidSpecifications()
    {
        await using var context = CreateContext();

        var category = await CreateTestCategoryAsync(context);

        var specification = await CreateTestSpecificationAsync(
            context, category.Id, "ram", "Оперативная память", "ГБ", 10);

        var service = new ProductService(context);

        var request = new CreateProductRequest
        {
            Name = "Test Laptop",
            Price = 2500,
            Quantity = 5,
            CategoryId = category.Id,

            Specifications = new List<ProductSpecificationRequest>
            {
                new()
                {
                    CategorySpecificationId = specification.Id,
                    Value = "16"
                },
                new()
                {
                    CategorySpecificationId = specification.Id,
                    Value = "32"
                }
            }
        };

        var result = await service.CreateAsync(request);

        Assert.Equal(
            ProductServiceStatus.InvalidSpecifications,
            result.Status);

        Assert.False(await context.Products.AnyAsync());
    }

    [Fact]
    public async Task CreateAsync_EmptySpecificationValue_ReturnsInvalidSpecifications()
    {
        await using var context = CreateContext();

        var category = await CreateTestCategoryAsync(context);

        var specification = await CreateTestSpecificationAsync(
            context, category.Id, "ram", "Оперативная память", "ГБ", 10);

        var service = new ProductService(context);

        var request = new CreateProductRequest
        {
            Name = "Test Laptop",
            Price = 2500,
            Quantity = 5,
            CategoryId = category.Id,

            Specifications = new List<ProductSpecificationRequest>
            {
                new()
                {
                    CategorySpecificationId = specification.Id,
                    Value = "   "
                }
            }
        };

        var result = await service.CreateAsync(request);

        Assert.Equal(
            ProductServiceStatus.InvalidSpecifications,
            result.Status);

        Assert.False(await context.Products.AnyAsync());
    }

    [Fact]
    public async Task CreateAsync_SpecificationsWithoutCategory_ReturnsInvalidSpecifications()
    {
        await using var context = CreateContext();

        var category = await CreateTestCategoryAsync(context);

        var specification = await CreateTestSpecificationAsync(
            context, category.Id, "ram", "Оперативная память", "ГБ", 10);

        var service = new ProductService(context);

        var request = new CreateProductRequest
        {
            Name = "Test Laptop",
            Price = 2500,
            Quantity = 5,
            CategoryId = null,

            Specifications = new List<ProductSpecificationRequest>
            {
                new()
                {
                    CategorySpecificationId = specification.Id,
                    Value = "16 ГБ"
                }
            }
        };

        var result = await service.CreateAsync(request);

        Assert.Equal(
            ProductServiceStatus.InvalidSpecifications,
            result.Status);

        Assert.False(await context.Products.AnyAsync());
    }

    [Fact]
    public async Task CreateAsync_SpecificationValueTooLong_ReturnsInvalidSpecifications()
    {
        await using var context = CreateContext();

        var category = await CreateTestCategoryAsync(context);

        var specification = await CreateTestSpecificationAsync(
            context, category.Id, "ram", "Оперативная память", "ГБ", 10);

        var service = new ProductService(context);

        var request = new CreateProductRequest
        {
            Name = "Test Laptop",
            Price = 2500,
            Quantity = 5,
            CategoryId = category.Id,

            Specifications = new List<ProductSpecificationRequest>
            {
                new()
                {
                    CategorySpecificationId = specification.Id,
                    Value = new string('A', 501)
                }
            }
        };

        var result = await service.CreateAsync(request);

        Assert.Equal(
            ProductServiceStatus.InvalidSpecifications,
            result.Status);

        Assert.False(await context.Products.AnyAsync());
    }

    // ================================================
    // ОБНОВЛЕНИЕ ХАРАКТЕРИСТИК ТОВАРА
    // ================================================

    [Fact]
    public async Task UpdateAsync_ValidSpecifications_ReplacesExistingSpecifications()
    {
        await using var context = CreateContext();

        var category = await CreateTestCategoryAsync(context);

        var ram = await CreateTestSpecificationAsync(
            context, category.Id, "ram", "Оперативная память", "ГБ", 10);

        var storage = await CreateTestSpecificationAsync(
            context, category.Id, "storage", "Накопитель", "ГБ", 20);

        var product = new Product
        {
            Id = 4,
            Name = "Old Laptop",
            Price = 2000,
            Quantity = 3,
            CategoryId = category.Id
        };

        context.Products.Add(product);

        context.ProductSpecifications.Add(
            new ProductSpecification
            {
                ProductId = product.Id,
                CategorySpecificationId = ram.Id,
                Value = "8"
            });

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var request = new UpdateProductRequest
        {
            Name = "Updated Laptop",
            Price = 2500,
            Quantity = 5,
            CategoryId = category.Id,

            Specifications = new List<ProductSpecificationRequest>
            {
                new()
                {
                    CategorySpecificationId = storage.Id,
                    Value = "512"
                }
            }
        };

        var status = await service.UpdateAsync(4, request);

        Assert.Equal(ProductServiceStatus.Success, status);

        var savedSpecifications = await context.ProductSpecifications
            .AsNoTracking()
            .Where(s => s.ProductId == product.Id)
            .ToListAsync();

        var savedSpecification = Assert.Single(savedSpecifications);

        Assert.Equal(storage.Id, savedSpecification.CategorySpecificationId);
        Assert.Equal("512", savedSpecification.Value);
    }

    [Fact]
    public async Task UpdateAsync_EmptySpecifications_RemovesExistingSpecifications()
    {
        await using var context = CreateContext();

        var category = await CreateTestCategoryAsync(context);

        var ram = await CreateTestSpecificationAsync(
            context, category.Id, "ram", "Оперативная память", "ГБ", 10);

        var product = new Product
        {
            Id = 4,
            Name = "Old Laptop",
            Price = 2000,
            Quantity = 3,
            CategoryId = category.Id
        };

        context.Products.Add(product);

        context.ProductSpecifications.Add(
            new ProductSpecification
            {
                ProductId = product.Id,
                CategorySpecificationId = ram.Id,
                Value = "16"
            });

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var request = new UpdateProductRequest
        {
            Name = "Updated Laptop",
            Price = 2500,
            Quantity = 5,
            CategoryId = category.Id,
            Specifications = new List<ProductSpecificationRequest>()
        };

        var status = await service.UpdateAsync(4, request);

        Assert.Equal(ProductServiceStatus.Success, status);

        Assert.False(
            await context.ProductSpecifications
                .AnyAsync(s => s.ProductId == product.Id));
    }

    [Fact]
    public async Task UpdateAsync_InvalidSpecifications_DoesNotChangeExistingData()
    {
        await using var context = CreateContext();

        var category = await CreateTestCategoryAsync(context);
        var anotherCategory = await CreateTestCategoryAsync(context);

        var ram = await CreateTestSpecificationAsync(
            context, category.Id, "ram", "Оперативная память", "ГБ", 10);

        var foreignSpecification = await CreateTestSpecificationAsync(
            context, anotherCategory.Id, "storage", "Накопитель", "ГБ", 20);

        var product = new Product
        {
            Id = 4,
            Name = "Old Laptop",
            Price = 2000,
            Quantity = 3,
            CategoryId = category.Id
        };

        context.Products.Add(product);

        context.ProductSpecifications.Add(
            new ProductSpecification
            {
                ProductId = product.Id,
                CategorySpecificationId = ram.Id,
                Value = "16"
            });

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var request = new UpdateProductRequest
        {
            Name = "New Laptop",
            Price = 3000,
            Quantity = 10,
            CategoryId = category.Id,

            Specifications = new List<ProductSpecificationRequest>
            {
                new()
                {
                    CategorySpecificationId = foreignSpecification.Id,
                    Value = "512"
                }
            }
        };

        var status = await service.UpdateAsync(4, request);

        Assert.Equal(
            ProductServiceStatus.InvalidSpecifications,
            status);

        var unchangedProduct = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == 4);

        Assert.Equal("Old Laptop", unchangedProduct.Name);
        Assert.Equal(2000m, unchangedProduct.Price);
        Assert.Equal(3, unchangedProduct.Quantity);

        var unchangedSpecification = await context.ProductSpecifications
            .AsNoTracking()
            .SingleAsync(s => s.ProductId == product.Id);

        Assert.Equal(ram.Id, unchangedSpecification.CategorySpecificationId);
        Assert.Equal("16", unchangedSpecification.Value);
    }

    // ================================================
    // ПОЛУЧЕНИЕ ХАРАКТЕРИСТИК ТОВАРА
    // ================================================

    [Fact]
    public async Task GetByIdAsync_ReturnsSpecificationsInConfiguredOrder()
    {
        await using var context = CreateContext();

        var category = await CreateTestCategoryAsync(context);

        var storage = await CreateTestSpecificationAsync(
            context, category.Id, "storage", "Накопитель", "ГБ", 20);

        var ram = await CreateTestSpecificationAsync(
            context, category.Id, "ram", "Оперативная память", "ГБ", 10);

        var product = new Product
        {
            Id = 4,
            Name = "Test Laptop",
            Price = 2500,
            Quantity = 5,
            CategoryId = category.Id
        };

        context.Products.Add(product);

        context.ProductSpecifications.AddRange(
            new ProductSpecification
            {
                ProductId = product.Id,
                CategorySpecificationId = storage.Id,
                Value = "512"
            },
            new ProductSpecification
            {
                ProductId = product.Id,
                CategorySpecificationId = ram.Id,
                Value = "16"
            });

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var result = await service.GetByIdAsync(product.Id);

        Assert.NotNull(result);
        Assert.Equal(2, result.Specifications.Count);

        Assert.Equal(
            new[] { "ram", "storage" },
            result.Specifications.Select(s => s.Code).ToArray());

        Assert.Equal(
            new[] { "16", "512" },
            result.Specifications.Select(s => s.Value).ToArray());
    }

    // ================================================
    // ПРОВЕРКА СТАРОЙ ЦЕНЫ
    // ================================================

    [Fact]
    public async Task CreateAsync_InvalidOldPrice_ReturnsInvalidPrice()
    {
        await using var context = CreateContext();

        var service = new ProductService(context);

        var request = new CreateProductRequest
        {
            Name = "Test Laptop",
            Price = 2500,
            OldPrice = 2500,
            Quantity = 5
        };

        var result = await service.CreateAsync(request);

        Assert.Equal(
            ProductServiceStatus.InvalidPrice,
            result.Status);

        Assert.False(await context.Products.AnyAsync());
    }

    [Fact]
    public async Task UpdateAsync_InvalidOldPrice_DoesNotChangeProduct()
    {
        await using var context = CreateContext();

        context.Products.Add(new Product
        {
            Id = 4,
            Name = "Old Laptop",
            Price = 2000,
            Quantity = 3
        });

        await context.SaveChangesAsync();

        var service = new ProductService(context);

        var request = new UpdateProductRequest
        {
            Name = "Updated Laptop",
            Price = 2500,
            OldPrice = 2400,
            Quantity = 10
        };

        var status = await service.UpdateAsync(4, request);

        Assert.Equal(
            ProductServiceStatus.InvalidPrice,
            status);

        var unchangedProduct = await context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == 4);

        Assert.Equal("Old Laptop", unchangedProduct.Name);
        Assert.Equal(2000m, unchangedProduct.Price);
        Assert.Equal(3, unchangedProduct.Quantity);
    }

    // ================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ДЛЯ НОВЫХ ТЕСТОВ
    // ================================================

    private static async Task<Category> CreateTestCategoryAsync(
        StoreDbContext context)
    {
        var category = new Category
        {
            Name = $"TestCategory_{Guid.NewGuid():N}"
        };

        context.Categories.Add(category);

        await context.SaveChangesAsync();

        return category;
    }

    private static async Task<CategorySpecification>
        CreateTestSpecificationAsync(
            StoreDbContext context,
            int categoryId,
            string code,
            string name,
            string? unit,
            int sortOrder)
    {
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

        return specification;
    }

}