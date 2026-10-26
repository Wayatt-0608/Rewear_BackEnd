using MongoDB.Driver;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Services;

/// <summary>
/// Service quản lý danh sách yêu thích (Wishlist) của người dùng.
/// </summary>
/// <remarks>
/// <para><b>Nguyên tắc:</b></para>
/// <list type="bullet">
/// <item>userId LUÔN lấy từ JWT (Controller), không nhận từ request.</item>
/// <item>Wishlist thuộc riêng từng user: mọi query đều lọc theo userId.</item>
/// <item>Thêm/Xóa đều idempotent.</item>
/// <item>Không đụng vào Product.Status / IsActive / StockQuantity (chỉ ĐỌC).</item>
/// <item>Không giữ hàng, không reserve tồn kho, không ảnh hưởng Cart/Order/Payment/Shipping.</item>
/// <item>Không lưu bản sao giá/ảnh: đọc LIVE từ collection Products.</item>
/// </list>
/// </remarks>
public class WishlistService : IWishlistService
{
    private readonly IWishlistRepository _wishlistRepository;
    private readonly IProductRepository _productRepository;

    public WishlistService(
        IWishlistRepository wishlistRepository,
        IProductRepository productRepository)
    {
        _wishlistRepository = wishlistRepository;
        _productRepository = productRepository;
    }

    // ============================================
    // LẤY DANH SÁCH WISHLIST
    // ============================================

    /// <summary>
    /// Lấy danh sách wishlist của user, có phân trang, mới nhất trước.
    /// </summary>
    public async Task<PagedWishlistResponse> GetMyWishlistAsync(string userId, WishlistQueryParameters query)
    {
        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = query.PageSize switch
        {
            < 1 => 20,
            > 100 => 100,   // chặn client xin pageSize quá lớn
            _ => query.PageSize
        };

        var (items, totalCount) = await _wishlistRepository.GetPagedByUserAsync(userId, page, pageSize);

        var responses = new List<WishlistItemResponse>();

        if (items.Count > 0)
        {
            // Batch lookup: 1 query $in thay vì N+1 query theo từng item.
            var productIds = items.Select(i => i.ProductId).Distinct().ToList();
            var products = await _productRepository.GetByIdsAsync(productIds);
            var productMap = products.ToDictionary(p => p.Id);

            foreach (var item in items)
            {
                if (!productMap.TryGetValue(item.ProductId, out var product))
                {
                    // Sản phẩm đã bị xóa cứng khỏi catalog -> chỉ trả thông tin tối thiểu an toàn.
                    responses.Add(new WishlistItemResponse
                    {
                        ProductId = item.ProductId,
                        Title = string.Empty,
                        Price = 0,
                        ImageUrl = null,
                        Size = null,
                        Color = null,
                        IsAvailableForPurchase = false,
                        AddedAt = item.CreatedAt
                    });
                    continue;
                }

                // Sản phẩm đã bán / bị gỡ / hết tồn vẫn GIỮ trong wishlist, chỉ đánh dấu
                // không còn mua được (giống cách Cart giữ món để người dùng tự xóa).
                responses.Add(new WishlistItemResponse
                {
                    ProductId = product.Id,
                    Title = product.Title,
                    Price = product.Price,
                    ImageUrl = product.ImageUrls.FirstOrDefault(),
                    Size = product.Size,
                    Color = product.Color,
                    IsAvailableForPurchase = IsPurchaseEligible(product),
                    AddedAt = item.CreatedAt
                });
            }
        }

        return new PagedWishlistResponse
        {
            Items = responses,
            TotalCount = (int)totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    // ============================================
    // THÊM VÀO WISHLIST
    // ============================================

    /// <summary>
    /// Thêm sản phẩm vào wishlist. Idempotent: thêm trùng KHÔNG tạo bản ghi mới.
    /// </summary>
    public async Task<ApiResponse> AddAsync(string userId, string productId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Fail("Không xác định được người dùng.", ApiErrorCode.Forbidden);

        if (string.IsNullOrWhiteSpace(productId))
            return Fail("Mã sản phẩm không được để trống.");

        var product = await _productRepository.GetByIdAsync(productId);

        // Sản phẩm không tồn tại -> 404.
        if (product == null)
            return Fail("Không tìm thấy sản phẩm.", ApiErrorCode.NotFound);

        // Không cho thêm MỚI sản phẩm không đủ điều kiện hiển thị công khai / mua được.
        if (!IsPurchaseEligible(product))
            return Fail(
                "Sản phẩm hiện không khả dụng để lưu vào danh sách yêu thích.",
                ApiErrorCode.Conflict);

        // Idempotent: đã có thì trả thành công, không tạo bản ghi mới.
        if (await _wishlistRepository.ExistsAsync(userId, productId))
            return Success("Sản phẩm đã có trong danh sách yêu thích.");

        try
        {
            await _wishlistRepository.AddAsync(new WishlistItem
            {
                UserId = userId,
                ProductId = product.Id,
                CreatedAt = DateTime.UtcNow
            });
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
        {
            // Race condition: request chạy song song đã insert trước -> unique index chặn,
            // coi như đã thành công (idempotent).
        }

        return Success("Đã thêm sản phẩm vào danh sách yêu thích.");
    }

    // ============================================
    // XÓA KHỎI WISHLIST
    // ============================================

    /// <summary>
    /// Xóa sản phẩm khỏi wishlist. Idempotent: xóa bản ghi không tồn tại vẫn trả thành công.
    /// </summary>
    public async Task<ApiResponse> RemoveAsync(string userId, string productId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Fail("Không xác định được người dùng.", ApiErrorCode.Forbidden);

        if (string.IsNullOrWhiteSpace(productId))
            return Fail("Mã sản phẩm không được để trống.");

        // Không cần kiểm tra product tồn tại: xóa wishlist không liên quan tới vòng đời Product.
        await _wishlistRepository.RemoveAsync(userId, productId);

        return Success("Đã xóa sản phẩm khỏi danh sách yêu thích.");
    }

    // ============================================
    // KIỂM TRA THUỘC WISHLIST
    // ============================================

    /// <summary>
    /// Kiểm tra 1 sản phẩm có nằm trong wishlist của user không.
    /// </summary>
    public async Task<WishlistCheckResponse> CheckAsync(string userId, string productId)
    {
        bool isWishlisted = false;

        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(productId))
        {
            isWishlisted = await _wishlistRepository.ExistsAsync(userId, productId);
        }

        return new WishlistCheckResponse
        {
            ProductId = productId,
            IsWishlisted = isWishlisted
        };
    }

    // ============================================
    // HELPERS
    // ============================================

    /// <summary>
    /// Quy tắc "còn mua được" của 1 sản phẩm.
    /// </summary>
    /// <remarks>
    /// Hợp nhất tiêu chí từ cả 2 nơi đang dùng trong hệ thống:
    /// - <c>OrderService.ValidateCartAsync</c>: IsActive + Status == Available + đủ tồn kho.
    /// - <c>CartService.BuildItemResponsesAsync</c>: IsActive + Status != Sold/Reserved.
    /// Dùng tiêu chí CHẶT nhất để không bao giờ lộ sản phẩm đã ẩn hoặc không mua được.
    /// </remarks>
    private static bool IsPurchaseEligible(Product product)
        => product.IsActive
           && product.Status == ProductStatus.Available
           && product.StockQuantity > 0;

    private static ApiResponse Success(string message)
        => new ApiResponse { Success = true, Message = message };

    private static ApiResponse Fail(string message, string errorCode = ApiErrorCode.Validation)
        => new ApiResponse { Success = false, Message = message, ErrorCode = errorCode };
}
