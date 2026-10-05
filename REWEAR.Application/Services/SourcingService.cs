using MongoDB.Bson;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
using System.Text.RegularExpressions;

namespace REWEAR.Application.Services;

/// <summary>
/// Service xử lý Business Logic cho Sourcing Request.
/// </summary>
public class SourcingService : ISourcingService
{
    private readonly ISourcingRepository _sourcingRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBrandRepository _brandRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IProductRepository _productRepository;
    private readonly ICloudinaryService _cloudinaryService;

    public SourcingService(
        ISourcingRepository sourcingRepository,
        IUserRepository userRepository,
        IBrandRepository brandRepository,
        ICategoryRepository categoryRepository,
        IProductRepository productRepository,
        ICloudinaryService cloudinaryService)
    {
        _sourcingRepository = sourcingRepository;
        _userRepository = userRepository;
        _brandRepository = brandRepository;
        _categoryRepository = categoryRepository;
        _productRepository = productRepository;
        _cloudinaryService = cloudinaryService;
    }

    /// <inheritdoc />
    public async Task<SourcingResponse> CreateAsync(string userId, CreateSourcingRequest request)
    {
        // ===== VALIDATION =====
        
        // User validation
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId không hợp lệ.");

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new InvalidOperationException("Người dùng không tồn tại.");

        // Title validation
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("Tiêu đề không được để trống.");

        string trimmedTitle = request.Title.Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle))
            throw new ArgumentException("Tiêu đề không được chỉ chứa khoảng trắng.");

        // CustomerExpectedPrice validation (nếu có thì > 0)
        if (request.CustomerExpectedPrice.HasValue && request.CustomerExpectedPrice.Value <= 0)
            throw new ArgumentException("Giá mong muốn phải lớn hơn 0.");

        // Image validation
        if (request.ImageUrls == null || request.ImageUrls.Count == 0)
            throw new ArgumentException("Phải upload ít nhất 1 hình ảnh.");

        if (request.ImageUrls.Count > 5)
            throw new ArgumentException("Tối đa 5 hình ảnh.");

        // Brand validation
        if (string.IsNullOrWhiteSpace(request.BrandId))
            throw new ArgumentException("BrandId không được để trống.");

        if (!ObjectId.TryParse(request.BrandId, out _))
            throw new ArgumentException("BrandId không hợp lệ.");

        var brand = await _brandRepository.GetByIdAsync(request.BrandId);
        if (brand == null)
            throw new InvalidOperationException($"Thương hiệu với Id '{request.BrandId}' không tồn tại.");

        if (!brand.IsActive)
            throw new InvalidOperationException($"Thương hiệu '{brand.Name}' hiện không hoạt động.");

        // Category validation
        if (string.IsNullOrWhiteSpace(request.CategoryId))
            throw new ArgumentException("CategoryId không được để trống.");

        if (!ObjectId.TryParse(request.CategoryId, out _))
            throw new ArgumentException("CategoryId không hợp lệ.");

        var category = await _categoryRepository.GetByIdAsync(request.CategoryId);
        if (category == null)
            throw new InvalidOperationException($"Danh mục với Id '{request.CategoryId}' không tồn tại.");

        if (!category.IsActive)
            throw new InvalidOperationException($"Danh mục '{category.Name}' hiện không hoạt động.");

        // ===== CREATE SOURCING REQUEST =====
        var sourcingRequest = new SourcingRequest
        {
            UserId = userId,
            Title = trimmedTitle,
            Description = request.Description?.Trim(),
            BrandId = request.BrandId,
            CategoryId = request.CategoryId,
            DeclaredCondition = request.DeclaredCondition,
            Size = request.Size?.Trim(),
            Color = request.Color?.Trim(),
            CustomerExpectedPrice = request.CustomerExpectedPrice,
            ImageUrls = request.ImageUrls,
            ImagePublicIds = request.ImagePublicIds,
            Status = SourcingStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _sourcingRepository.CreateAsync(sourcingRequest);

        return await MapToResponse(sourcingRequest);
    }

    /// <inheritdoc />
    public async Task<List<SourcingResponse>> GetByUserIdAsync(string userId)
    {
        var requests = await _sourcingRepository.GetByUserIdAsync(userId);
        var responses = new List<SourcingResponse>();

        foreach (var request in requests)
        {
            responses.Add(await MapToResponse(request));
        }

        return responses;
    }

    /// <inheritdoc />
    public async Task<SourcingResponse?> GetByIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out _))
            return null;

        var request = await _sourcingRepository.GetByIdAsync(id);
        return request != null ? await MapToResponse(request) : null;
    }

    /// <inheritdoc />
    public async Task<List<SourcingResponse>> GetAllAsync(SourcingStatus? status = null)
    {
        var requests = await _sourcingRepository.GetAllAsync(status);
        var responses = new List<SourcingResponse>();

        foreach (var request in requests)
        {
            responses.Add(await MapToResponse(request));
        }

        return responses;
    }

    /// <inheritdoc />
    public async Task<SourcingResponse?> StartReviewAsync(string id)
    {
        if (!ObjectId.TryParse(id, out _))
            return null;

        var request = await _sourcingRepository.GetByIdAsync(id);
        if (request == null)
            return null;

        // Chỉ cho phép: Pending → UnderReview
        if (request.Status != SourcingStatus.Pending)
            throw new InvalidOperationException("Chỉ có thể bắt đầu review request đang ở trạng thái Pending.");

        request.Status = SourcingStatus.UnderReview;
        await _sourcingRepository.UpdateAsync(request);

        return await MapToResponse(request);
    }

    /// <inheritdoc />
    public async Task<SourcingResponse?> OfferPriceAsync(string id, OfferSourcingRequest request)
    {
        if (!ObjectId.TryParse(id, out _))
            return null;

        var sourcingRequest = await _sourcingRepository.GetByIdAsync(id);
        if (sourcingRequest == null)
            return null;

        // Chỉ cho phép: UnderReview → Priced
        if (sourcingRequest.Status != SourcingStatus.UnderReview)
            throw new InvalidOperationException("Chỉ có thể định giá request đang ở trạng thái UnderReview.");

        // Validate OfferedPrice
        if (request.OfferedPrice <= 0)
            throw new ArgumentException("Giá đề nghị phải lớn hơn 0.");

        sourcingRequest.OfferedPrice = request.OfferedPrice;
        sourcingRequest.AdminNote = request.AdminNote?.Trim();
        sourcingRequest.Status = SourcingStatus.Priced;

        await _sourcingRepository.UpdateAsync(sourcingRequest);

        return await MapToResponse(sourcingRequest);
    }

    /// <inheritdoc />
    public async Task<SourcingResponse?> RejectAsync(string id, RejectSourcingRequest request)
    {
        if (!ObjectId.TryParse(id, out _))
            return null;

        var sourcingRequest = await _sourcingRepository.GetByIdAsync(id);
        if (sourcingRequest == null)
            return null;

        // Cho phép: Pending → Rejected, UnderReview → Rejected
        if (sourcingRequest.Status != SourcingStatus.Pending && 
            sourcingRequest.Status != SourcingStatus.UnderReview)
            throw new InvalidOperationException("Chỉ có thể từ chối request đang ở trạng thái Pending hoặc UnderReview.");

        // Validate RejectReason
        if (string.IsNullOrWhiteSpace(request.RejectReason))
            throw new ArgumentException("Lý do từ chối không được để trống.");

        sourcingRequest.RejectReason = request.RejectReason.Trim();
        sourcingRequest.Status = SourcingStatus.Rejected;
        sourcingRequest.CompletedAt = DateTime.UtcNow;

        await _sourcingRepository.UpdateAsync(sourcingRequest);

        return await MapToResponse(sourcingRequest);
    }

    /// <inheritdoc />
    public async Task<SourcingResponse?> AcceptOfferAsync(string id, string userId)
    {
        if (!ObjectId.TryParse(id, out _))
            return null;

        var sourcingRequest = await _sourcingRepository.GetByIdAsync(id);
        if (sourcingRequest == null)
            return null;

        // Ownership check
        if (sourcingRequest.UserId != userId)
            throw new UnauthorizedAccessException("Bạn không có quyền thực hiện thao tác này.");

        // Chỉ cho phép: Priced → Accepted
        if (sourcingRequest.Status != SourcingStatus.Priced)
            throw new InvalidOperationException("Chỉ có thể chấp nhận giá khi request đang ở trạng thái Priced.");

        sourcingRequest.Status = SourcingStatus.Accepted;

        await _sourcingRepository.UpdateAsync(sourcingRequest);

        return await MapToResponse(sourcingRequest);
    }

    /// <inheritdoc />
    public async Task<SourcingResponse?> DeclineOfferAsync(string id, string userId)
    {
        if (!ObjectId.TryParse(id, out _))
            return null;

        var sourcingRequest = await _sourcingRepository.GetByIdAsync(id);
        if (sourcingRequest == null)
            return null;

        // Ownership check
        if (sourcingRequest.UserId != userId)
            throw new UnauthorizedAccessException("Bạn không có quyền thực hiện thao tác này.");

        // Chỉ cho phép: Priced → Declined
        if (sourcingRequest.Status != SourcingStatus.Priced)
            throw new InvalidOperationException("Chỉ có thể từ chối giá khi request đang ở trạng thái Priced.");

        sourcingRequest.Status = SourcingStatus.Declined;
        sourcingRequest.CompletedAt = DateTime.UtcNow;

        await _sourcingRepository.UpdateAsync(sourcingRequest);

        return await MapToResponse(sourcingRequest);
    }

    /// <inheritdoc />
    public async Task<SourcingResponse?> MarkReceivedAsync(string id)
    {
        if (!ObjectId.TryParse(id, out _))
            return null;

        var sourcingRequest = await _sourcingRepository.GetByIdAsync(id);
        if (sourcingRequest == null)
            return null;

        // Chỉ cho phép: Accepted → Received
        if (sourcingRequest.Status != SourcingStatus.Accepted)
            throw new InvalidOperationException("Chỉ có thể đánh dấu đã nhận hàng khi request đang ở trạng thái Accepted.");

        sourcingRequest.Status = SourcingStatus.Received;

        await _sourcingRepository.UpdateAsync(sourcingRequest);

        return await MapToResponse(sourcingRequest);
    }

    /// <inheritdoc />
    public async Task<SourcingResponse?> InspectAsync(string id, InspectSourcingRequest request)
    {
        if (!ObjectId.TryParse(id, out _))
            return null;

        var sourcingRequest = await _sourcingRepository.GetByIdAsync(id);
        if (sourcingRequest == null)
            return null;

        // Chỉ cho phép: Received → Approved
        if (sourcingRequest.Status != SourcingStatus.Received)
            throw new InvalidOperationException("Chỉ có thể kiểm định request đang ở trạng thái Received.");

        sourcingRequest.InspectedCondition = request.InspectedCondition;
        sourcingRequest.AdminNote = request.AdminNote?.Trim();
        sourcingRequest.Status = SourcingStatus.Approved;

        await _sourcingRepository.UpdateAsync(sourcingRequest);

        return await MapToResponse(sourcingRequest);
    }

    /// <inheritdoc />
    public async Task<SourcingResponse?> ConvertToProductAsync(string id, ConvertToProductRequest request)
    {
        if (!ObjectId.TryParse(id, out _))
            return null;

        var sourcingRequest = await _sourcingRepository.GetByIdAsync(id);
        if (sourcingRequest == null)
            return null;

        // Chỉ cho phép: Approved → Completed
        if (sourcingRequest.Status != SourcingStatus.Approved)
            throw new InvalidOperationException("Chỉ có thể convert request đang ở trạng thái Approved.");

        // Validate SellingPrice
        if (request.SellingPrice <= 0)
            throw new ArgumentException("Giá bán phải lớn hơn 0.");

        // ===== CREATE PRODUCT =====
        var product = new Product
        {
            Title = sourcingRequest.Title,
            Slug = await GenerateUniqueSlug(sourcingRequest.Title),
            Description = sourcingRequest.Description ?? string.Empty,
            BrandId = sourcingRequest.BrandId,
            CategoryId = sourcingRequest.CategoryId,
            Price = request.SellingPrice, // Giá bán ra, KHÔNG phải OfferedPrice
            Condition = sourcingRequest.InspectedCondition ?? sourcingRequest.DeclaredCondition,
            Size = sourcingRequest.Size,
            Color = sourcingRequest.Color,
            ImageUrls = sourcingRequest.ImageUrls, // REUSE ImageUrls, không upload lại
            Status = ProductStatus.Available,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _productRepository.CreateAsync(product);

        // ===== UPDATE SOURCING REQUEST =====
        sourcingRequest.ProductId = product.Id;
        sourcingRequest.Status = SourcingStatus.Completed;
        sourcingRequest.CompletedAt = DateTime.UtcNow;
        sourcingRequest.UpdatedAt = DateTime.UtcNow;

        await _sourcingRepository.UpdateAsync(sourcingRequest);

        return await MapToResponse(sourcingRequest);
    }

    /// <summary>
    /// Map SourcingRequest entity sang SourcingResponse DTO.
    /// </summary>
    private async Task<SourcingResponse> MapToResponse(SourcingRequest request)
    {
        var response = new SourcingResponse
        {
            Id = request.Id,
            UserId = request.UserId,
            Title = request.Title,
            Description = request.Description,
            BrandId = request.BrandId,
            CategoryId = request.CategoryId,
            Size = request.Size,
            Color = request.Color,
            DeclaredCondition = request.DeclaredCondition,
            InspectedCondition = request.InspectedCondition,
            CustomerExpectedPrice = request.CustomerExpectedPrice,
            OfferedPrice = request.OfferedPrice,
            ImageUrls = request.ImageUrls,
            Status = request.Status,
            AdminNote = request.AdminNote,
            RejectReason = request.RejectReason,
            ProductId = request.ProductId,
            CreatedAt = request.CreatedAt,
            UpdatedAt = request.UpdatedAt,
            CompletedAt = request.CompletedAt
        };

        // Lấy BrandName
        if (!string.IsNullOrWhiteSpace(request.BrandId))
        {
            var brand = await _brandRepository.GetByIdAsync(request.BrandId);
            response.BrandName = brand?.Name ?? "Unknown Brand";
        }

        // Lấy CategoryName
        if (!string.IsNullOrWhiteSpace(request.CategoryId))
        {
            var category = await _categoryRepository.GetByIdAsync(request.CategoryId);
            response.CategoryName = category?.Name ?? "Unknown Category";
        }

        return response;
    }

    /// <summary>
    /// Generate unique slug từ title.
    /// </summary>
    private async Task<string> GenerateUniqueSlug(string title)
    {
        string baseSlug = GenerateSlug(title);

        if (string.IsNullOrEmpty(baseSlug))
            baseSlug = "product";

        string slug = baseSlug;
        int counter = 1;

        while (true)
        {
            var existing = await _productRepository.GetBySlugAsync(slug);
            
            if (existing == null)
                break;

            slug = $"{baseSlug}-{counter}";
            counter++;
        }

        return slug;
    }

    /// <summary>
    /// Generate slug từ title.
    /// </summary>
    private static string GenerateSlug(string title)
    {
        string slug = title.ToLowerInvariant();
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = Regex.Replace(slug, @"[^a-z0-9\-]", "");
        slug = Regex.Replace(slug, @"-+", "-");
        slug = slug.Trim('-');
        return slug;
    }
}
