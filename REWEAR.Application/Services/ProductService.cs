using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
using MongoDB.Bson;
using System.Text.RegularExpressions;

namespace REWEAR.Application.Services;

/// <summary>
/// Service xử lý Business Logic cho Product.
/// </summary>
public class ProductService : IProductService
{
    private readonly IProductRepository _productRepository;
    private readonly IBrandRepository _brandRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ISourcingRepository _sourcingRepository;

    public ProductService(
        IProductRepository productRepository,
        IBrandRepository brandRepository,
        ICategoryRepository categoryRepository,
        ISourcingRepository sourcingRepository)
    {
        _productRepository = productRepository;
        _brandRepository = brandRepository;
        _categoryRepository = categoryRepository;
        _sourcingRepository = sourcingRepository;
    }

    /// <summary>
    /// Lấy tất cả sản phẩm.
    /// </summary>
    public async Task<List<ProductResponse>> GetAllAsync()
    {
        var products = await _productRepository.GetAllAsync();
        var responses = new List<ProductResponse>();

        foreach (var product in products)
        {
            var response = await MapToResponse(product);
            responses.Add(response);
        }

        return responses;
    }

    /// <summary>
    /// Lấy tất cả sản phẩm với filter theo trạng thái IsActive.
    /// </summary>
    public async Task<List<ProductResponse>> GetAllAsync(bool? isActive)
    {
        var products = await _productRepository.GetAllAsync(isActive);
        var responses = new List<ProductResponse>();

        foreach (var product in products)
        {
            var response = await MapToResponse(product);
            responses.Add(response);
        }

        return responses;
    }

    /// <summary>
    /// Lấy tất cả sản phẩm với query parameters (filter/search).
    /// Validation: kiểm tra format của BrandId, CategoryId, và giá trị price range.
    /// </summary>
    public async Task<List<ProductResponse>> GetAllAsync(ProductQueryParameters query)
    {
        // ===== VALIDATION =====
        
        // BrandId validation - phải là ObjectId hợp lệ nếu được truyền
        if (!string.IsNullOrWhiteSpace(query.BrandId))
        {
            if (!ObjectId.TryParse(query.BrandId, out _))
            {
                throw new ArgumentException("BrandId không hợp lệ.");
            }
        }

        // CategoryId validation - phải là ObjectId hợp lệ nếu được truyền
        if (!string.IsNullOrWhiteSpace(query.CategoryId))
        {
            if (!ObjectId.TryParse(query.CategoryId, out _))
            {
                throw new ArgumentException("CategoryId không hợp lệ.");
            }
        }

        // Price range validation
        if (query.MinPrice.HasValue && query.MinPrice.Value < 0)
        {
            throw new ArgumentException("MinPrice không được nhỏ hơn 0.");
        }

        if (query.MaxPrice.HasValue && query.MaxPrice.Value <= 0)
        {
            throw new ArgumentException("MaxPrice phải lớn hơn 0.");
        }

        if (query.MinPrice.HasValue && query.MaxPrice.HasValue)
        {
            if (query.MinPrice.Value > query.MaxPrice.Value)
            {
                throw new ArgumentException("MinPrice không được lớn hơn MaxPrice.");
            }
        }

        // ===== QUERY =====
        var products = await _productRepository.GetAllAsync(query);
        var responses = new List<ProductResponse>();

        foreach (var product in products)
        {
            var response = await MapToResponse(product);
            responses.Add(response);
        }

        return responses;
    }

    /// <summary>
    /// Lấy sản phẩm theo Id.
    /// </summary>
    public async Task<ProductResponse?> GetByIdAsync(string id)
    {
        // Validate MongoDB ObjectId
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        var product = await _productRepository.GetByIdAsync(id);
        return product != null ? await MapToResponse(product) : null;
    }

    /// <summary>
    /// Tạo sản phẩm mới.
    /// </summary>
    public async Task<ProductResponse> CreateAsync(CreateProductRequest request)
    {
        // ===== VALIDATION =====
        
        // Title validation
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("Tiêu đề sản phẩm không được để trống.");
        }

        string trimmedTitle = request.Title.Trim();

        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            throw new ArgumentException("Tiêu đề sản phẩm không được chỉ chứa khoảng trắng.");
        }

        // Price validation
        if (request.Price <= 0)
        {
            throw new ArgumentException("Giá sản phẩm phải lớn hơn 0.");
        }

        // Brand validation
        if (string.IsNullOrWhiteSpace(request.BrandId))
        {
            throw new ArgumentException("BrandId không được để trống.");
        }

        if (!ObjectId.TryParse(request.BrandId, out _))
        {
            throw new ArgumentException("BrandId không hợp lệ.");
        }

        var brand = await _brandRepository.GetByIdAsync(request.BrandId);
        if (brand == null)
        {
            throw new InvalidOperationException($"Thương hiệu với Id '{request.BrandId}' không tồn tại.");
        }

        if (!brand.IsActive)
        {
            throw new InvalidOperationException($"Thương hiệu '{brand.Name}' hiện không hoạt động.");
        }

        // Category validation
        if (string.IsNullOrWhiteSpace(request.CategoryId))
        {
            throw new ArgumentException("CategoryId không được để trống.");
        }

        if (!ObjectId.TryParse(request.CategoryId, out _))
        {
            throw new ArgumentException("CategoryId không hợp lệ.");
        }

        var category = await _categoryRepository.GetByIdAsync(request.CategoryId);
        if (category == null)
        {
            throw new InvalidOperationException($"Danh mục với Id '{request.CategoryId}' không tồn tại.");
        }

        if (!category.IsActive)
        {
            throw new InvalidOperationException($"Danh mục '{category.Name}' hiện không hoạt động.");
        }

        // ===== TẠO PRODUCT =====
        var product = new Product
        {
            Title = trimmedTitle,
            Slug = await GenerateUniqueSlug(trimmedTitle),
            Description = request.Description?.Trim() ?? string.Empty,
            BrandId = request.BrandId,
            CategoryId = request.CategoryId,
            Price = request.Price,
            Condition = request.Condition,
            Size = request.Size?.Trim(),
            Color = request.Color?.Trim(),
            ImageUrls = request.ImageUrls ?? new List<string>(),
            StockQuantity = request.StockQuantity < 1 ? 1 : request.StockQuantity,
            Status = ProductStatus.Available,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _productRepository.CreateAsync(product);

        return await MapToResponse(product);
    }

    /// <summary>
    /// Cập nhật sản phẩm.
    /// </summary>
    public async Task<ProductResponse?> UpdateAsync(string id, UpdateProductRequest request)
    {
        // Validate MongoDB ObjectId
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        // Tìm product hiện tại
        var product = await _productRepository.GetByIdAsync(id);
        if (product == null)
        {
            return null;
        }

        // ===== VALIDATION =====
        
        // Title validation
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("Tiêu đề sản phẩm không được để trống.");
        }

        string trimmedTitle = request.Title.Trim();

        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            throw new ArgumentException("Tiêu đề sản phẩm không được chỉ chứa khoảng trắng.");
        }

        // Price validation
        if (request.Price <= 0)
        {
            throw new ArgumentException("Giá sản phẩm phải lớn hơn 0.");
        }

        // Stock validation: phải >= 1. Sản phẩm hết hàng được quản lý bằng
        // Status = Sold, nên tồn kho = 0 là trạng thái không hợp lệ khi sửa.
        if (request.StockQuantity < 1)
        {
            throw new ArgumentException("Số lượng tồn kho phải lớn hơn 0.");
        }

        // Brand validation
        if (string.IsNullOrWhiteSpace(request.BrandId))
        {
            throw new ArgumentException("BrandId không được để trống.");
        }

        if (!ObjectId.TryParse(request.BrandId, out _))
        {
            throw new ArgumentException("BrandId không hợp lệ.");
        }

        var brand = await _brandRepository.GetByIdAsync(request.BrandId);
        if (brand == null)
        {
            throw new InvalidOperationException($"Thương hiệu với Id '{request.BrandId}' không tồn tại.");
        }

        if (!brand.IsActive)
        {
            throw new InvalidOperationException($"Thương hiệu '{brand.Name}' hiện không hoạt động.");
        }

        // Category validation
        if (string.IsNullOrWhiteSpace(request.CategoryId))
        {
            throw new ArgumentException("CategoryId không được để trống.");
        }

        if (!ObjectId.TryParse(request.CategoryId, out _))
        {
            throw new ArgumentException("CategoryId không hợp lệ.");
        }

        var category = await _categoryRepository.GetByIdAsync(request.CategoryId);
        if (category == null)
        {
            throw new InvalidOperationException($"Danh mục với Id '{request.CategoryId}' không tồn tại.");
        }

        if (!category.IsActive)
        {
            throw new InvalidOperationException($"Danh mục '{category.Name}' hiện không hoạt động.");
        }

        // ===== CẬP NHẬT =====
        string oldTitle = product.Title;
        product.Title = trimmedTitle;
        
        // Regenerate slug nếu title thay đổi
        if (oldTitle != trimmedTitle)
        {
            product.Slug = await GenerateUniqueSlug(trimmedTitle, id);
        }
        
        product.Description = request.Description?.Trim() ?? string.Empty;
        product.BrandId = request.BrandId;
        product.CategoryId = request.CategoryId;
        product.Price = request.Price;
        product.Condition = request.Condition;
        product.Size = request.Size?.Trim();
        product.Color = request.Color?.Trim();
        product.ImageUrls = request.ImageUrls ?? new List<string>();
        product.StockQuantity = request.StockQuantity;
        // KHÔNG update Status và IsActive ở đây
        product.UpdatedAt = DateTime.UtcNow;

        await _productRepository.UpdateAsync(product);

        return await MapToResponse(product);
    }

    /// <summary>
    /// Cập nhật sản phẩm với xử lý ảnh nâng cao.
    /// </summary>
    /// <remarks>
    /// QUY TẮC XỬ LÝ ẢNH:
    /// - Images == null && KeepImageUrls == null → Giữ nguyên ảnh cũ
    /// - Images == null && KeepImageUrls != null → Giữ chỉ các ảnh trong KeepImageUrls
    /// - Images != null → Upload ảnh mới, kết hợp với KeepImageUrls
    /// - ClearAllImages = true → Xóa tất cả ảnh cũ
    /// </remarks>
    public async Task<ProductResponse?> UpdateWithImagesAsync(
        string id,
        UpdateProductRequest request,
        List<string> newImageUrls,
        List<string>? keepImageUrls,
        bool clearAllImages)
    {
        // Validate MongoDB ObjectId
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        // Tìm product hiện tại
        var product = await _productRepository.GetByIdAsync(id);
        if (product == null)
        {
            return null;
        }

        // ===== VALIDATION =====
        // Title validation
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("Tiêu đề sản phẩm không được để trống.");
        }

        string trimmedTitle = request.Title.Trim();

        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            throw new ArgumentException("Tiêu đề sản phẩm không được chỉ chứa khoảng trắng.");
        }

        // Price validation
        if (request.Price <= 0)
        {
            throw new ArgumentException("Giá sản phẩm phải lớn hơn 0.");
        }

        // Stock validation
        if (request.StockQuantity < 1)
        {
            throw new ArgumentException("Số lượng tồn kho phải lớn hơn 0.");
        }

        // Brand validation
        if (string.IsNullOrWhiteSpace(request.BrandId))
        {
            throw new ArgumentException("BrandId không được để trống.");
        }

        if (!ObjectId.TryParse(request.BrandId, out _))
        {
            throw new ArgumentException("BrandId không hợp lệ.");
        }

        var brand = await _brandRepository.GetByIdAsync(request.BrandId);
        if (brand == null)
        {
            throw new InvalidOperationException($"Thương hiệu với Id '{request.BrandId}' không tồn tại.");
        }

        if (!brand.IsActive)
        {
            throw new InvalidOperationException($"Thương hiệu '{brand.Name}' hiện không hoạt động.");
        }

        // Category validation
        if (string.IsNullOrWhiteSpace(request.CategoryId))
        {
            throw new ArgumentException("CategoryId không được để trống.");
        }

        if (!ObjectId.TryParse(request.CategoryId, out _))
        {
            throw new ArgumentException("CategoryId không hợp lệ.");
        }

        var category = await _categoryRepository.GetByIdAsync(request.CategoryId);
        if (category == null)
        {
            throw new InvalidOperationException($"Danh mục với Id '{request.CategoryId}' không tồn tại.");
        }

        if (!category.IsActive)
        {
            throw new InvalidOperationException($"Danh mục '{category.Name}' hiện không hoạt động.");
        }

        // ===== XỬ LÝ ẢNH =====
        List<string> finalImageUrls;

        // Case 1: No image changes requested - keep all old images
        if (newImageUrls.Count == 0 && keepImageUrls == null && !clearAllImages)
        {
            finalImageUrls = product.ImageUrls;
        }
        // Case 2: Clear all images (must have new images to replace)
        else if (clearAllImages)
        {
            // When clearing all images, must have new images to replace
            if (newImageUrls.Count < 1)
            {
                throw new ArgumentException("Phải upload ảnh mới khi xóa tất cả ảnh cũ.");
            }

            if (newImageUrls.Count > 5)
            {
                throw new ArgumentException("Tối đa 5 hình ảnh cho mỗi sản phẩm.");
            }

            finalImageUrls = newImageUrls;
        }
        // Case 3: Keep specific images only (no new images)
        else if (newImageUrls.Count == 0 && keepImageUrls != null)
        {
            // Validate that keepImageUrls are valid (belong to this product)
            var validKeepUrls = keepImageUrls
                .Where(url => product.ImageUrls.Contains(url))
                .ToList();

            // Check total images limit (1-5)
            if (validKeepUrls.Count < 1)
            {
                throw new ArgumentException("Sản phẩm phải có ít nhất 1 hình ảnh.");
            }

            if (validKeepUrls.Count > 5)
            {
                throw new ArgumentException("Sản phẩm tối đa 5 hình ảnh.");
            }

            finalImageUrls = validKeepUrls;
        }
        // Case 4: Add new images + keep some old images
        else
        {
            List<string> urlsToKeep = keepImageUrls ?? new List<string>();

            // Validate keepImageUrls - only allow URLs that belong to this product
            var validKeepUrls = urlsToKeep
                .Where(url => product.ImageUrls.Contains(url))
                .ToList();

            // Calculate total
            int totalImages = validKeepUrls.Count + newImageUrls.Count;

            if (totalImages < 1)
            {
                throw new ArgumentException("Sản phẩm phải có ít nhất 1 hình ảnh.");
            }

            if (totalImages > 5)
            {
                throw new ArgumentException($"Tổng số hình ảnh vượt quá giới hạn (tối đa 5).");
            }

            finalImageUrls = new List<string>(validKeepUrls);
            finalImageUrls.AddRange(newImageUrls);
        }

        // ===== CẬP NHẬT =====
        string oldTitle = product.Title;
        product.Title = trimmedTitle;

        // Regenerate slug nếu title thay đổi
        if (oldTitle != trimmedTitle)
        {
            product.Slug = await GenerateUniqueSlug(trimmedTitle, id);
        }

        product.Description = request.Description?.Trim() ?? string.Empty;
        product.BrandId = request.BrandId;
        product.CategoryId = request.CategoryId;
        product.Price = request.Price;
        product.Condition = request.Condition;
        product.Size = request.Size?.Trim();
        product.Color = request.Color?.Trim();
        product.ImageUrls = finalImageUrls;
        product.StockQuantity = request.StockQuantity;
        // KHÔNG update Status và IsActive ở đây
        product.UpdatedAt = DateTime.UtcNow;

        await _productRepository.UpdateAsync(product);

        // Return list of old image URLs that are no longer used (for cleanup by caller)
        return await MapToResponse(product);
    }

    /// <summary>
    /// Kiểm tra ảnh cũ có cần xóa không.
    /// Chỉ xóa nếu ảnh không còn được sử dụng bởi product khác hoặc sourcing nào.
    /// </summary>
    public async Task<List<string>> GetUnusedImagesToDeleteAsync(string productId, List<string> currentImageUrls)
    {
        var product = await _productRepository.GetByIdAsync(productId);
        if (product == null)
        {
            return new List<string>();
        }

        var unusedImages = new List<string>();

        foreach (var oldUrl in product.ImageUrls)
        {
            // Skip if URL is still in use
            if (currentImageUrls.Contains(oldUrl))
            {
                continue;
            }

            // Check if used by other products
            bool usedByOtherProduct = await _productRepository.IsImageUrlInUseByOtherProductAsync(oldUrl, productId);
            if (usedByOtherProduct)
            {
                continue;
            }

            // Check if used by any sourcing request
            bool usedBySourcing = await _sourcingRepository.IsImageUrlInUseAsync(oldUrl);
            if (usedBySourcing)
            {
                continue;
            }

            // This image is no longer used - add to delete list
            unusedImages.Add(oldUrl);
        }

        return unusedImages;
    }

    /// <summary>
    /// Xóa sản phẩm (soft delete - đặt IsActive = false).
    /// </summary>
    public async Task<bool> DeleteAsync(string id)
    {
        // Validate MongoDB ObjectId
        if (!ObjectId.TryParse(id, out _))
        {
            return false;
        }

        // Tìm product hiện tại
        var product = await _productRepository.GetByIdAsync(id);
        if (product == null)
        {
            return false;
        }

        // Soft delete: đặt IsActive = false
        product.IsActive = false;
        product.UpdatedAt = DateTime.UtcNow;

        await _productRepository.UpdateAsync(product);

        return true;
    }

    /// <summary>
    /// Khôi phục sản phẩm đã bị soft delete (đặt IsActive = true).
    /// Idempotent: nếu product đã active thì vẫn trả success.
    /// </summary>
    public async Task<ProductResponse?> RestoreAsync(string id)
    {
        // Validate MongoDB ObjectId
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        // Tìm product hiện tại
        var product = await _productRepository.GetByIdAsync(id);
        if (product == null)
        {
            return null;
        }

        // Kiểm tra Brand còn active không
        if (!string.IsNullOrWhiteSpace(product.BrandId))
        {
            var brand = await _brandRepository.GetByIdAsync(product.BrandId);
            if (brand == null)
            {
                throw new InvalidOperationException($"Không thể khôi phục sản phẩm: Thương hiệu liên kết không tồn tại.");
            }
            if (!brand.IsActive)
            {
                throw new InvalidOperationException($"Không thể khôi phục sản phẩm: Thương hiệu '{brand.Name}' đang không hoạt động.");
            }
        }

        // Kiểm tra Category còn active không
        if (!string.IsNullOrWhiteSpace(product.CategoryId))
        {
            var category = await _categoryRepository.GetByIdAsync(product.CategoryId);
            if (category == null)
            {
                throw new InvalidOperationException($"Không thể khôi phục sản phẩm: Danh mục liên kết không tồn tại.");
            }
            if (!category.IsActive)
            {
                throw new InvalidOperationException($"Không thể khôi phục sản phẩm: Danh mục '{category.Name}' đang không hoạt động.");
            }
        }

        // Khôi phục: đặt IsActive = true
        // Idempotent: nếu đã active thì vẫn ok
        product.IsActive = true;
        product.UpdatedAt = DateTime.UtcNow;

        await _productRepository.UpdateAsync(product);

        return await MapToResponse(product);
    }

    /// <summary>
    /// Cập nhật trạng thái thương mại của sản phẩm.
    /// </summary>
    public async Task<ProductResponse?> UpdateStatusAsync(string id, ProductStatus status)
    {
        // Validate MongoDB ObjectId
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        // Tìm product hiện tại
        var product = await _productRepository.GetByIdAsync(id);
        if (product == null)
        {
            return null;
        }

        // Product phải active mới đổi status
        if (!product.IsActive)
        {
            throw new InvalidOperationException("Không thể cập nhật trạng thái sản phẩm không hoạt động.");
        }

        // Cập nhật status
        product.Status = status;
        product.UpdatedAt = DateTime.UtcNow;

        await _productRepository.UpdateAsync(product);

        return await MapToResponse(product);
    }

    /// <summary>
    /// Generate unique slug từ title.
    /// Đảm bảo không trùng với slug của product khác.
    /// </summary>
    private async Task<string> GenerateUniqueSlug(string title, string? excludeProductId = null)
    {
        // Generate base slug
        string baseSlug = GenerateSlug(title);

        if (string.IsNullOrEmpty(baseSlug))
        {
            baseSlug = "product";
        }

        string slug = baseSlug;
        int counter = 1;

        // Check for existing slug
        while (true)
        {
            var existing = await _productRepository.GetBySlugAsync(slug);
            
            if (existing == null)
            {
                // Slug is available
                break;
            }

            // Nếu đang update và slug là của chính product đó thì ok
            if (excludeProductId != null && existing.Id == excludeProductId)
            {
                break;
            }

            // Tạo slug mới với suffix
            slug = $"{baseSlug}-{counter}";
            counter++;
        }

        return slug;
    }

    /// <summary>
    /// Generate slug từ title.
    /// Ví dụ: "Levi's 501 Vintage Jeans" → "levis-501-vintage-jeans"
    /// </summary>
    private static string GenerateSlug(string title)
    {
        // Lowercase
        string slug = title.ToLowerInvariant();

        // Replace spaces với hyphen
        slug = Regex.Replace(slug, @"\s+", "-");

        // Remove special characters (giữ chữ cái, số, và hyphen)
        slug = Regex.Replace(slug, @"[^a-z0-9\-]", "");

        // Remove multiple hyphens
        slug = Regex.Replace(slug, @"-+", "-");

        // Trim hyphens ở đầu và cuối
        slug = slug.Trim('-');

        return slug;
    }

    /// <summary>
    /// Map Product entity sang ProductResponse DTO (kèm BrandName, CategoryName).
    /// </summary>
    private async Task<ProductResponse> MapToResponse(Product product)
    {
        var response = new ProductResponse
        {
            Id = product.Id,
            Title = product.Title,
            Slug = product.Slug,
            Description = product.Description,
            BrandId = product.BrandId,
            CategoryId = product.CategoryId,
            Price = product.Price,
            Condition = product.Condition,
            Size = product.Size,
            Color = product.Color,
            ImageUrls = product.ImageUrls,
            Status = product.Status,
            IsActive = product.IsActive,
            StockQuantity = product.StockQuantity,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt
        };

        // Lấy BrandName
        if (!string.IsNullOrWhiteSpace(product.BrandId))
        {
            var brand = await _brandRepository.GetByIdAsync(product.BrandId);
            response.BrandName = brand?.Name ?? "Unknown Brand";
        }

        // Lấy CategoryName
        if (!string.IsNullOrWhiteSpace(product.CategoryId))
        {
            var category = await _categoryRepository.GetByIdAsync(product.CategoryId);
            response.CategoryName = category?.Name ?? "Unknown Category";
        }

        return response;
    }
}
