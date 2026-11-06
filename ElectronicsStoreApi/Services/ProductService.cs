using ElectronicsStoreApi.Data;
using ElectronicsStoreApi.DTOs.Requests;
using ElectronicsStoreApi.DTOs.Response;
using ElectronicsStoreApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ElectronicsStoreApi.Services;

public enum ProductServiceStatus
{
    Success,
    ProductNotFound,
    CategoryNotFound,
    InvalidPrice,
    InvalidSpecifications
}

public sealed record ProductServiceResult(
    ProductServiceStatus Status,
    ProductResponse? Product = null);

public class ProductService
{
    private readonly StoreDbContext _context;

    public ProductService(StoreDbContext context)
    {
        _context = context;
    }

    // ==================================================
    // ПОЛУЧЕНИЕ СПИСКА ТОВАРОВ
    // ==================================================

    public async Task<ProductListResponse> GetAllAsync(
        string? search,
        int? categoryId,
        decimal? minPrice,
        decimal? maxPrice,
        int? maxQuantity,
        string sortBy,
        bool descending,
        bool inStockOnly,
        int page,
        int pageSize)
    {
        IQueryable<Product> query =
            _context.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();

            query = query.Where(p =>
                EF.Functions.ILike(p.Name, $"%{term}%"));
        }

        if (categoryId.HasValue)
        {
            query = query.Where(p =>
                p.CategoryId == categoryId.Value);
        }

        if (minPrice.HasValue)
        {
            query = query.Where(p =>
                p.Price >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            query = query.Where(p =>
                p.Price <= maxPrice.Value);
        }

        if (maxQuantity.HasValue)
        {
            query = query.Where(p =>
                p.Quantity <= maxQuantity.Value);
        }

        if (inStockOnly)
        {
            query = query.Where(p => p.Quantity > 0);
        }

        int totalCount = await query.CountAsync();

        query = (sortBy, descending) switch
        {
            ("id", false) =>
                query.OrderBy(p => p.Id),

            ("id", true) =>
                query.OrderByDescending(p => p.Id),

            ("name", false) =>
                query.OrderBy(p => p.Name).ThenBy(p => p.Id),

            ("name", true) =>
                query.OrderByDescending(p => p.Name).ThenBy(p => p.Id),

            ("price", false) =>
                query.OrderBy(p => p.Price).ThenBy(p => p.Id),

            ("price", true) =>
                query.OrderByDescending(p => p.Price).ThenBy(p => p.Id),

            ("quantity", false) =>
                query.OrderBy(p => p.Quantity).ThenBy(p => p.Id),

            ("quantity", true) =>
                query.OrderByDescending(p => p.Quantity).ThenBy(p => p.Id),

            _ => query.OrderBy(p => p.Id)
        };

        var products = await query
            .Include(p => p.Category)
            .Include(p => p.Specifications)
                .ThenInclude(s => s.CategorySpecification)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new ProductListResponse
        {
            Items = products.Select(MapToResponse).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(
                totalCount / (double)pageSize)
        };
    }

    // ==================================================
    // ПОЛУЧЕНИЕ ОДНОГО ТОВАРА
    // ==================================================

    public async Task<ProductResponse?> GetByIdAsync(int id)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Specifications)
                .ThenInclude(s => s.CategorySpecification)
            .FirstOrDefaultAsync(p => p.Id == id);

        return product is null
            ? null
            : MapToResponse(product);
    }

    // ==================================================
    // ПОЛУЧЕНИЕ ТОВАРОВ КАТЕГОРИИ
    // ==================================================

    public async Task<List<ProductResponse>> GetByCategoryAsync(
        int categoryId)
    {
        var products = await _context.Products
            .AsNoTracking()
            .Where(p => p.CategoryId == categoryId)
            .Include(p => p.Category)
            .Include(p => p.Specifications)
                .ThenInclude(s => s.CategorySpecification)
            .OrderBy(p => p.Id)
            .ToListAsync();

        return products
            .Select(MapToResponse)
            .ToList();
    }

    // ==================================================
    // ПРЕОБРАЗОВАНИЕ PRODUCT В PRODUCTRESPONSE
    // ==================================================

    private static ProductResponse MapToResponse(Product product)
    {
        return new ProductResponse
        {
            Id = product.Id,
            Name = product.Name,
            Price = product.Price,
            OldPrice = product.OldPrice,
            ImageUrl = product.ImageUrl,
            Quantity = product.Quantity,
            CategoryId = product.CategoryId,
            CategoryName = product.Category?.Name,
            Description = product.Description,

            Specifications = product.Specifications
                .OrderBy(s =>
                    s.CategorySpecification?.SortOrder ?? int.MaxValue)
                .ThenBy(s =>
                    s.CategorySpecification?.Id ?? int.MaxValue)
                .Select(s => new ProductSpecificationResponse
                {
                    CategorySpecificationId =
                        s.CategorySpecificationId,

                    Code = s.CategorySpecification?.Code ?? string.Empty,

                    Name = s.CategorySpecification?.Name ?? string.Empty,

                    Unit = s.CategorySpecification?.Unit,

                    Value = s.Value,

                    SortOrder =
                        s.CategorySpecification?.SortOrder ?? 0
                })
                .ToList()
        };
    }

    // ==================================================
    // ПРОВЕРКА ЦЕНЫ
    // ==================================================

    private static bool HasInvalidOldPrice(
        decimal price,
        decimal? oldPrice)
    {
        return oldPrice.HasValue &&
               oldPrice.Value <= price;
    }

    // ==================================================
    // ПРОВЕРКА ХАРАКТЕРИСТИК
    // ==================================================

    private async Task<bool> AreSpecificationsValidAsync(
        int? categoryId,
        IReadOnlyCollection<ProductSpecificationRequest>? specifications)
    {
        if (specifications is null || specifications.Count == 0)
        {
            return true;
        }

        // Характеристики нельзя добавить товару без категории.
        if (!categoryId.HasValue)
        {
            return false;
        }

        // Проверяем заполненность и корректность значений.
        if (specifications.Any(s =>
            s is null ||
            s.CategorySpecificationId <= 0 ||
            string.IsNullOrWhiteSpace(s.Value) ||
            s.Value.Trim().Length > 500))
        {
            return false;
        }

        // Проверяем отсутствие дубликатов.
        int[] requestedIds = specifications
            .Select(s => s.CategorySpecificationId)
            .ToArray();

        if (requestedIds.Distinct().Count() != requestedIds.Length)
        {
            return false;
        }

        // Проверяем, что все характеристики принадлежат
        // указанной категории товара.
        int matchingCount = await _context.CategorySpecifications
            .CountAsync(s =>
                s.CategoryId == categoryId.Value &&
                requestedIds.Contains(s.Id));

        return matchingCount == requestedIds.Length;
    }

    // ==================================================
    // СОЗДАНИЕ ТОВАРА
    // ==================================================

    public async Task<ProductServiceResult> CreateAsync(
        CreateProductRequest request)
    {
        if (HasInvalidOldPrice(request.Price, request.OldPrice))
        {
            return new ProductServiceResult(
                ProductServiceStatus.InvalidPrice);
        }

        string? categoryName = null;

        if (request.CategoryId.HasValue)
        {
            categoryName = await _context.Categories
                .Where(c => c.Id == request.CategoryId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync();

            if (categoryName is null)
            {
                return new ProductServiceResult(
                    ProductServiceStatus.CategoryNotFound);
            }
        }

        var specifications = request.Specifications
            ?? new List<ProductSpecificationRequest>();

        bool specificationsValid =
            await AreSpecificationsValidAsync(
                request.CategoryId,
                specifications);

        if (!specificationsValid)
        {
            return new ProductServiceResult(
                ProductServiceStatus.InvalidSpecifications);
        }

        var product = new Product
        {
            Name = request.Name,
            Price = request.Price,
            OldPrice = request.OldPrice,
            Quantity = request.Quantity,
            CategoryId = request.CategoryId,
            Description = request.Description,

            Specifications = specifications
                .Select(s => new ProductSpecification
                {
                    CategorySpecificationId =
                        s.CategorySpecificationId,

                    Value = s.Value.Trim()
                })
                .ToList()
        };

        _context.Products.Add(product);

        await _context.SaveChangesAsync();

        // Повторно получаем товар вместе с сохранёнными
        // характеристиками и определениями характеристик.
        var createdProduct = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Specifications)
                .ThenInclude(s => s.CategorySpecification)
            .FirstAsync(p => p.Id == product.Id);

        return new ProductServiceResult(
            ProductServiceStatus.Success,
            MapToResponse(createdProduct));
    }

    // ==================================================
    // ОБНОВЛЕНИЕ ТОВАРА
    // ==================================================

    public async Task<ProductServiceStatus> UpdateAsync(
        int id,
        UpdateProductRequest request)
    {
        var product = await _context.Products
            .Include(p => p.Specifications)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product is null)
        {
            return ProductServiceStatus.ProductNotFound;
        }

        if (HasInvalidOldPrice(request.Price, request.OldPrice))
        {
            return ProductServiceStatus.InvalidPrice;
        }

        // Проверяем существование категории.
        if (request.CategoryId.HasValue)
        {
            bool categoryExists = await _context.Categories
                .AnyAsync(c => c.Id == request.CategoryId.Value);

            if (!categoryExists)
            {
                return ProductServiceStatus.CategoryNotFound;
            }
        }

        var specifications = request.Specifications
            ?? new List<ProductSpecificationRequest>();

        bool specificationsValid =
            await AreSpecificationsValidAsync(
                request.CategoryId,
                specifications);

        if (!specificationsValid)
        {
            return ProductServiceStatus.InvalidSpecifications;
        }

        // Обновляем основные поля товара.
        product.Name = request.Name;
        product.Price = request.Price;
        product.OldPrice = request.OldPrice;
        product.Quantity = request.Quantity;
        product.CategoryId = request.CategoryId;
        product.Description = request.Description;

        // Удаляем предыдущие значения характеристик.
        _context.ProductSpecifications.RemoveRange(
            product.Specifications);

        product.Specifications.Clear();

        // Добавляем новые значения характеристик.
        foreach (var specification in specifications)
        {
            product.Specifications.Add(new ProductSpecification
            {
                CategorySpecificationId =
                    specification.CategorySpecificationId,

                Value = specification.Value.Trim()
            });
        }

        await _context.SaveChangesAsync();

        return ProductServiceStatus.Success;
    }

    // ==================================================
    // УДАЛЕНИЕ ТОВАРА
    // ==================================================

    public async Task<ProductServiceStatus> DeleteAsync(int id)
    {
        var product = await _context.Products.FindAsync(id);

        if (product is null)
        {
            return ProductServiceStatus.ProductNotFound;
        }

        _context.Products.Remove(product);

        await _context.SaveChangesAsync();

        return ProductServiceStatus.Success;
    }
}