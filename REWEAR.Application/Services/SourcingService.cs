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
    private readonly ICommerceUnitOfWork _transactions;
    private readonly CircularFashionService _operations;
    private readonly ILifecycleStore _store;
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
        ICloudinaryService cloudinaryService, ICommerceUnitOfWork transactions, CircularFashionService operations, ILifecycleStore store)
    {
        _transactions = transactions; _operations = operations; _store = store;
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
        if (request.CustomerExpectedPrice.HasValue && (request.CustomerExpectedPrice.Value <= 0 || decimal.Truncate(request.CustomerExpectedPrice.Value) != request.CustomerExpectedPrice.Value))
            throw new ArgumentException("Giá mong muốn phải là số nguyên VND lớn hơn 0.");
        if (!Enum.IsDefined(request.DeclaredCondition)) throw new ArgumentException("Tình trạng khai báo không hợp lệ.");

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
    public Task<SourcingResponse?> StartReviewAsync(string id) => _transactions.ExecuteAsync(() => StartReviewAsyncCore(id));

    private async Task<SourcingResponse?> StartReviewAsyncCore(string id)
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
    public Task<SourcingResponse?> OfferPriceAsync(string id, OfferSourcingRequest request, string actorId = "") => _transactions.ExecuteAsync(() => OfferPriceAsyncCore(id, request, actorId));

    private async Task<SourcingResponse?> OfferPriceAsyncCore(string id, OfferSourcingRequest request, string actorId = "")
    {
        if (!ObjectId.TryParse(id, out _))
            return null;

        var sourcingRequest = await _sourcingRepository.GetByIdAsync(id);
        if (sourcingRequest == null)
            return null;

        // Chỉ cho phép: UnderReview → Priced
        if (sourcingRequest.Status is not (SourcingStatus.UnderReview or SourcingStatus.Priced))
            throw new InvalidOperationException("Chỉ có thể định giá request đang ở trạng thái UnderReview.");

        // Validate OfferedPrice
        if (request.OfferedPrice <= 0 || decimal.Truncate(request.OfferedPrice) != request.OfferedPrice)
            throw new ArgumentException("Giá đề nghị phải là số nguyên VND lớn hơn 0.");

        if (decimal.Truncate(request.OfferedPrice) != request.OfferedPrice) throw new ArgumentException("Giá phải là số nguyên VND.");
        sourcingRequest.OfferVersion++;
        sourcingRequest.OfferHistory.Add(new SourcingOffer { Version = sourcingRequest.OfferVersion, Amount = request.OfferedPrice, ActorId = actorId });
        sourcingRequest.OfferedPrice = request.OfferedPrice;
        sourcingRequest.AdminNote = request.AdminNote?.Trim();
        sourcingRequest.Status = SourcingStatus.Priced;

        await _sourcingRepository.UpdateAsync(sourcingRequest);

        return await MapToResponse(sourcingRequest);
    }

    /// <inheritdoc />
    public Task<SourcingResponse?> RejectAsync(string id, RejectSourcingRequest request) => _transactions.ExecuteAsync(() => RejectAsyncCore(id, request));

    private async Task<SourcingResponse?> RejectAsyncCore(string id, RejectSourcingRequest request)
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
    public Task<SourcingResponse?> AcceptOfferAsync(string id, string userId, int? offerVersion = null) => _transactions.ExecuteAsync(() => AcceptOfferAsyncCore(id, userId, offerVersion));

    private async Task<SourcingResponse?> AcceptOfferAsyncCore(string id, string userId, int? offerVersion = null)
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
        if (sourcingRequest.Status != SourcingStatus.Priced && !(sourcingRequest.Status == SourcingStatus.Received && sourcingRequest.RequiresRevisedAcceptance))
            throw new InvalidOperationException("Yêu cầu không chờ đồng ý giá.");
        if (sourcingRequest.OfferVersion > 0 && offerVersion != sourcingRequest.OfferVersion) throw new InvalidOperationException("Báo giá đã thay đổi, vui lòng tải lại trước khi đồng ý.");

        sourcingRequest.AcceptedOfferPrice = sourcingRequest.OfferedPrice;
        if (sourcingRequest.OfferHistory.LastOrDefault() is { } offer) offer.AcceptedAt = DateTime.UtcNow;
        if (sourcingRequest.RequiresRevisedAcceptance)
        {
            sourcingRequest.RequiresRevisedAcceptance = false; sourcingRequest.Status = SourcingStatus.Approved;
            var item = await _store.GetAsync<InventoryItem>(id) ?? throw new InvalidOperationException("Chưa có bản ghi kho.");
            var from = item.State; item.State = "AwaitingPayout"; await _store.SaveAsync(item);
            await _store.InsertAsync(new InventoryMovement { InventoryId = id, FromState = from, ToState = item.State, ActorId = userId, Reason = "Người gửi đồng ý báo giá sau kiểm định." });
        }
        else sourcingRequest.Status = SourcingStatus.Accepted;

        await _sourcingRepository.UpdateAsync(sourcingRequest);

        return await MapToResponse(sourcingRequest);
    }

    /// <inheritdoc />
    public Task<SourcingResponse?> DeclineOfferAsync(string id, string userId) => _transactions.ExecuteAsync(() => DeclineOfferAsyncCore(id, userId));

    private async Task<SourcingResponse?> DeclineOfferAsyncCore(string id, string userId)
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
        if (sourcingRequest.Status != SourcingStatus.Priced && !sourcingRequest.RequiresRevisedAcceptance)
            throw new InvalidOperationException("Yêu cầu không chờ quyết định giá.");
        if (sourcingRequest.RequiresRevisedAcceptance)
        {
            var item = await _store.GetAsync<InventoryItem>(id) ?? throw new InvalidOperationException("Chưa có bản ghi kho.");
            item.State = "ReturnToSenderPending"; await _store.SaveAsync(item);
            await _store.InsertAsync(new InventoryMovement { InventoryId = id, FromState = "AwaitingCustomerAcceptance", ToState = item.State, ActorId = userId, Reason = "Người gửi từ chối giá sau kiểm định." });
            sourcingRequest.RequiresRevisedAcceptance = false;
        }

        sourcingRequest.Status = SourcingStatus.Declined;
        sourcingRequest.CompletedAt = DateTime.UtcNow;

        await _sourcingRepository.UpdateAsync(sourcingRequest);

        return await MapToResponse(sourcingRequest);
    }

    /// <inheritdoc />
    public async Task<SourcingResponse?> MarkReceivedAsync(string id, string actorId = "")
    {
        await _operations.ReceiveAsync(id, actorId);
        return await GetByIdAsync(id);
    }

    /// <inheritdoc />
    public async Task<SourcingResponse?> InspectAsync(string id, InspectSourcingRequest request, string actorId = "")
    {
        await _operations.InspectSourceAsync(id, request, actorId);
        return await GetByIdAsync(id);
    }

    /// <inheritdoc />
    public async Task<SourcingResponse?> ConvertToProductAsync(string id, ConvertToProductRequest request, string actorId = "")
    {
        await _operations.PublishResaleAsync(id, request.SellingPrice, actorId);
        return await GetByIdAsync(id);
    }

    /// <summary>
    /// Map SourcingRequest entity sang SourcingResponse DTO.
    /// </summary>
    private async Task<SourcingResponse> MapToResponse(SourcingRequest request)
    {
        var response = new SourcingResponse
        {
            Id = request.Id,
            OfferVersion = request.OfferVersion, RequiresRevisedAcceptance = request.RequiresRevisedAcceptance,
            AcceptedOfferPrice = request.AcceptedOfferPrice, InspectedBy = request.InspectedBy, InspectedAt = request.InspectedAt,
            OfferHistory = request.OfferHistory, Payout = await _store.GetAsync<PurchasePayout>(request.Id),
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
