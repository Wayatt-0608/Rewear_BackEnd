using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Services;

/// <summary>
/// Service xử lý giỏ hàng của người dùng.
/// </summary>
public class CartService : ICartService
{
    private readonly ICartRepository _cartRepository;
    private readonly IProductRepository _productRepository;

    public CartService(ICartRepository cartRepository, IProductRepository productRepository)
    {
        _cartRepository = cartRepository;
        _productRepository = productRepository;
    }

    /// <summary>
    /// Số lượng tối đa cho 1 dòng trong giỏ.
    /// Sản phẩm REWEAR là đồ secondhand nhưng vẫn có thể có nhiều món cùng loại,
    /// nên đặt giới hạn mềm ở mức 5 món để chặn nhập nhằng / lạm dụng.
    /// </summary>
    private const int MAX_QUANTITY_PER_ITEM = 5;

    // ============================================
    // LẤY GIỎ HÀNG
    // ============================================

    /// <summary>
    /// Lấy giỏ hàng kèm thông tin sản phẩm mới nhất.
    /// </summary>
    public async Task<CartResponse> GetCartAsync(string userId)
    {
        var cart = await GetOrCreateCartAsync(userId);
        var items = await BuildItemResponsesAsync(cart);

        var availableSubTotal = items.Where(i => i.IsAvailable).Sum(i => i.LineTotal);

        // Thống kê riêng nhóm ĐÃ TICK — đây là nhóm sẽ vào đơn khi checkout.
        var selectedItems = items.Where(i => i.IsSelected).ToList();
        var selectedAvailable = selectedItems.Where(i => i.IsAvailable).ToList();

        return new CartResponse
        {
            Id = cart.Id,
            UserId = cart.UserId,
            Items = items,
            TotalQuantity = cart.TotalQuantity,
            SubTotal = items.Sum(i => i.LineTotal),
            AvailableSubTotal = availableSubTotal,
            HasUnavailableItems = items.Any(i => !i.IsAvailable),
            HasPriceChanged = items.Any(i => i.PriceChanged),

            SelectedQuantity = selectedItems.Sum(i => i.Quantity),
            SelectedSubTotal = selectedAvailable.Sum(i => i.LineTotal),
            SelectedUnavailableCount = selectedItems.Count(i => !i.IsAvailable),

            CreatedAt = cart.CreatedAt,
            UpdatedAt = cart.UpdatedAt,
            ExpiresAt = cart.ExpiresAt
        };
    }

    // ============================================
    // THÊM VÀO GIỎ
    // ============================================

    /// <summary>
    /// Thêm sản phẩm vào giỏ hàng.
    /// </summary>
    public async Task<ApiResponse> AddToCartAsync(string userId, AddToCartRequest request)
    {
        // ===== VALIDATION =====
        if (string.IsNullOrWhiteSpace(request.ProductId))
            return Fail("Id sản phẩm không được để trống.");

        if (request.Quantity < 1)
            return Fail("Số lượng phải lớn hơn 0.");

        if (request.Quantity > MAX_QUANTITY_PER_ITEM)
            return Fail($"Mỗi sản phẩm chỉ có thể thêm tối đa {MAX_QUANTITY_PER_ITEM} món.");

        // ===== KIỂM TRA SẢN PHẨM =====
        var product = await _productRepository.GetByIdAsync(request.ProductId);
        if (product == null)
            return FailNotFound("Không tìm thấy sản phẩm.");

        if (!product.IsActive)
            return Fail("Sản phẩm không còn khả dụng.");

        if (product.Status != ProductStatus.Available)
            return Fail(BuildUnavailableMessage(product.Status));

        // Không cho thêm nhiều hơn tồn kho thực tế của sản phẩm.
        if (product.StockQuantity < 1)
            return Fail("Sản phẩm đã hết hàng.");

        if (request.Quantity > product.StockQuantity)
            return Fail($"Sản phẩm chỉ còn {product.StockQuantity} món.");

        // ===== KIỂM TRA TRÙNG TRONG GIỎ =====
        var cart = await GetOrCreateCartAsync(userId);

        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == product.Id);

        // Sản phẩm đã có trong giỏ: cộng dồn số lượng thay vì báo lỗi.
        if (existingItem != null)
        {
            int newQuantity = existingItem.Quantity + request.Quantity;

            if (newQuantity > MAX_QUANTITY_PER_ITEM)
                return Fail($"Sản phẩm này đã có {existingItem.Quantity} món trong giỏ. "
                          + $"Chỉ có thể thêm tối đa {MAX_QUANTITY_PER_ITEM - existingItem.Quantity} món nữa.");

            // Tổng sau khi cộng dồn vẫn phải <= tồn kho thực tế.
            if (newQuantity > product.StockQuantity)
                return Fail($"Sản phẩm chỉ còn {product.StockQuantity} món, không thể thêm {request.Quantity} món nữa.");

            existingItem.Quantity = newQuantity;
            existingItem.PriceSnapshot = product.Price;
            // Thêm số lượng thì coi như user muốn mua -> chọn luôn để tránh
            // trường hợp món đang bỏ tick bị lọt vào đơn ngoài ý muốn.
            existingItem.IsSelected = true;

            await SaveAsync(cart);

            var updatedCart = await GetCartAsync(userId);

            return new ApiResponse
            {
                Success = true,
                Message = $"Đã thêm vào giỏ. Sản phẩm này hiện có {newQuantity} món trong giỏ hàng.",
                Data = updatedCart
            };
        }

        // ===== THÊM VÀO GIỎ =====
        cart.Items.Add(new CartItem
        {
            ProductId = product.Id,
            Quantity = request.Quantity,
            PriceSnapshot = product.Price,
            IsSelected = true,   // thêm vào giỏ = chọn sẵn, khớp hành vi sàn thương mại
            AddedAt = DateTime.UtcNow
        });

        await SaveAsync(cart);

        var response = await GetCartAsync(userId);

        return new ApiResponse
        {
            Success = true,
            Message = "Đã thêm sản phẩm vào giỏ hàng.",
            Data = response
        };
    }

    // ============================================
    // CẬP NHẬT SỐ LƯỢNG
    // ============================================

    /// <summary>
    /// Cập nhật số lượng của một dòng trong giỏ.
    /// </summary>
    public async Task<ApiResponse> UpdateItemAsync(string userId, string itemId, UpdateCartItemRequest request)
    {
        if (request.Quantity < 1)
            return Fail("Số lượng phải lớn hơn 0.");

        if (request.Quantity > MAX_QUANTITY_PER_ITEM)
            return Fail($"Mỗi sản phẩm chỉ có thể có tối đa {MAX_QUANTITY_PER_ITEM} món.");

        var cart = await _cartRepository.GetByUserIdAsync(userId);
        if (cart == null)
            return FailNotFound("Không tìm thấy giỏ hàng.");

        var item = cart.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null)
            return FailNotFound("Không tìm thấy sản phẩm trong giỏ hàng.");

        // Không cho đặt số lượng vượt tồn kho thực tế của sản phẩm.
        var product = await _productRepository.GetByIdAsync(item.ProductId);
        if (product != null && request.Quantity > product.StockQuantity)
            return Fail($"Sản phẩm chỉ còn {product.StockQuantity} món.");

        item.Quantity = request.Quantity;

        await SaveAsync(cart);

        var response = await GetCartAsync(userId);

        return new ApiResponse
        {
            Success = true,
            Message = "Đã cập nhật giỏ hàng.",
            Data = response
        };
    }

    // ============================================
    // TICK CHỌN SẢN PHẨM (mô hình giống Shopee)
    // ============================================

    /// <summary>
    /// Tick / bỏ tick một dòng giỏ hàng.
    /// </summary>
    public async Task<ApiResponse> SelectItemAsync(string userId, string itemId, SelectCartItemRequest request)
    {
        var cart = await _cartRepository.GetByUserIdAsync(userId);
        if (cart == null)
            return FailNotFound("Không tìm thấy giỏ hàng.");

        var item = cart.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null)
            return FailNotFound("Không tìm thấy sản phẩm trong giỏ hàng.");

        item.IsSelected = request.IsSelected;

        await SaveAsync(cart);
        var response = await GetCartAsync(userId);

        return new ApiResponse
        {
            Success = true,
            Message = request.IsSelected
                ? "Đã chọn sản phẩm để thanh toán."
                : "Đã bỏ chọn sản phẩm.",
            Data = response
        };
    }

    /// <summary>
    /// Tick / bỏ tick toàn bộ giỏ hàng.
    /// </summary>
    public async Task<ApiResponse> SelectAllItemsAsync(string userId, SelectAllCartItemsRequest request)
    {
        var cart = await _cartRepository.GetByUserIdAsync(userId);
        if (cart == null || cart.Items.Count == 0)
            return Fail("Giỏ hàng đang trống.");

        // Nếu yêu cầu chỉ áp dụng cho nhóm còn khả dụng, cần tra catalog để phân loại.
        HashSet<string>? availableProductIds = null;

        if (request.OnlyAvailable.HasValue)
        {
            availableProductIds = new HashSet<string>();

            foreach (var item in cart.Items)
            {
                var product = await _productRepository.GetByIdAsync(item.ProductId);
                if (product != null && product.IsActive && product.Status == ProductStatus.Available)
                    availableProductIds.Add(item.ProductId);
            }
        }

        int affected = 0;

        foreach (var item in cart.Items)
        {
            // Bỏ qua nhóm không thuộc phạm vi yêu cầu.
            if (availableProductIds != null && !availableProductIds.Contains(item.ProductId))
                continue;

            if (item.IsSelected != request.IsSelected)
                affected++;

            item.IsSelected = request.IsSelected;
        }

        await SaveAsync(cart);
        var response = await GetCartAsync(userId);

        return new ApiResponse
        {
            Success = true,
            Message = request.IsSelected
                ? $"Đã chọn {affected} sản phẩm để thanh toán."
                : $"Đã bỏ chọn {affected} sản phẩm.",
            Data = response
        };
    }

    // ============================================
    // XÓA KHỎI GIỎ
    // ============================================

    /// <summary>
    /// Xóa một dòng khỏi giỏ hàng.
    /// </summary>
    public async Task<ApiResponse> RemoveItemAsync(string userId, string itemId)
    {
        var cart = await _cartRepository.GetByUserIdAsync(userId);
        if (cart == null)
            return FailNotFound("Không tìm thấy giỏ hàng.");

        var item = cart.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null)
            return FailNotFound("Không tìm thấy sản phẩm trong giỏ hàng.");

        cart.Items.Remove(item);

        await SaveAsync(cart);

        var response = await GetCartAsync(userId);

        return new ApiResponse
        {
            Success = true,
            Message = "Đã xóa sản phẩm khỏi giỏ hàng.",
            Data = response
        };
    }

    // ============================================
    // XÓA TOÀN BỘ GIỎ
    // ============================================

    /// <summary>
    /// Xóa toàn bộ sản phẩm trong giỏ (giữ lại document giỏ rỗng).
    /// </summary>
    public async Task<ApiResponse> ClearCartAsync(string userId)
    {
        var cart = await _cartRepository.GetByUserIdAsync(userId);
        if (cart == null)
        {
            await _cartRepository.CreateAsync(new Cart { UserId = userId });
            return new ApiResponse { Success = true, Message = "Giỏ hàng đã trống." };
        }

        int removedCount = cart.Items.Count;
        cart.Items.Clear();

        Touch(cart);
        await _cartRepository.UpdateAsync(cart);

        return new ApiResponse
        {
            Success = true,
            Message = removedCount > 0
                ? $"Đã xóa {removedCount} sản phẩm khỏi giỏ hàng."
                : "Giỏ hàng đã trống."
        };
    }

    // ============================================
    // SỐ LƯỢNG TRONG GIỎ (badge)
    // ============================================

    /// <summary>
    /// Trả về tổng số lượng sản phẩm trong giỏ.
    /// </summary>
    public async Task<int> GetCartItemCountAsync(string userId)
    {
        var cart = await _cartRepository.GetByUserIdAsync(userId);
        return cart?.TotalQuantity ?? 0;
    }

    // ============================================
    // HELPER
    // ============================================

    /// <summary>
    /// Lấy giỏ của user, tự tạo mới nếu chưa có.
    /// </summary>
    private async Task<Cart> GetOrCreateCartAsync(string userId)
    {
        var cart = await _cartRepository.GetByUserIdAsync(userId);
        if (cart != null)
            return cart;

        cart = new Cart { UserId = userId };
        await _cartRepository.CreateAsync(cart);

        return cart;
    }

    /// <summary>
    /// Đánh dấu giỏ vừa thay đổi: cập nhật UpdatedAt và đẩy hạn sử dụng ra xa.
    /// </summary>
    private static void Touch(Cart cart)
    {
        var now = DateTime.UtcNow;
        cart.UpdatedAt = now;
        cart.ExpiresAt = now.AddDays(Cart.EXPIRATION_DAYS);
    }

    /// <summary>
    /// Dọn rác rồi lưu giỏ xuống DB.
    /// Việc dọn rác được gộp vào mỗi lần ghi để tránh giỏ phình vô hạn theo thời gian
    /// mà không cần chạy job dọn riêng.
    /// </summary>
    private async Task SaveAsync(Cart cart)
    {
        await RemoveDeadItems(cart);
        Touch(cart);
        await _cartRepository.UpdateAsync(cart);
    }

    /// <summary>
    /// Loại bỏ những dòng giỏ không còn khả năng hiển thị hoặc mua được:
    ///   - Sản phẩm đã bị xóa khỏi catalog (không còn tên/ảnh để hiện ra UI).
    ///
    /// Còn các trường hợp "sản phẩm còn tồn tại nhưng đã bán / bị gỡ / hết hàng"
    /// thì VẪN GIỮ trong giỏ (kèm cờ IsAvailable = false để FE gạch báo) vì:
    ///   1. Người dùng cần nhìn thấy món đó để tự xóa, tránh tưởng mất tiền/kỳ lạ.
    ///   2. Nhiều mô hình marketplace (Shopee, Lazada) cũng giữ và gạch báo.
    ///   3. TTL index (expiresAt) vẫn tự dọn nếu user không vào giỏ nữa.
    /// </summary>
    private async Task RemoveDeadItems(Cart cart)
    {
        // Danh sách productId cần tra lại. Tập hợp trước để tránh tra trùng.
        var candidateProductIds = cart.Items
            .Select(i => i.ProductId)
            .Distinct()
            .ToList();

        var productIdsToRemove = new HashSet<string>();

        foreach (var productId in candidateProductIds)
        {
            var product = await _productRepository.GetByIdAsync(productId);
            if (product == null)
                productIdsToRemove.Add(productId);
        }

        if (productIdsToRemove.Count > 0)
            cart.Items.RemoveAll(i => productIdsToRemove.Contains(i.ProductId));
    }

    /// <summary>
    /// Join danh sách item của giỏ với thông tin sản phẩm hiện tại trong catalog.
    /// Đồng thời đánh dấu những item đã không còn khả dụng / bị đổi giá.
    /// </summary>
    private async Task<List<CartItemResponse>> BuildItemResponsesAsync(Cart cart)
    {
        var responses = new List<CartItemResponse>();

        foreach (var item in cart.Items)
        {
            var product = await _productRepository.GetByIdAsync(item.ProductId);

            // Sản phẩm đã bị xóa khỏi catalog
            if (product == null)
            {
                responses.Add(new CartItemResponse
                {
                    Id = item.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    IsSelected = false,   // món không còn tồn tại thì không thể chọn để mua
                    PriceSnapshot = item.PriceSnapshot,
                    CurrentPrice = 0,
                    LineTotal = 0,
                    IsAvailable = false,
                    UnavailableReason = "Sản phẩm không còn tồn tại.",
                    ProductStatus = "Unknown",
                    ProductTitle = "(Sản phẩm không còn tồn tại)",
                    StockQuantity = 0,
                    AddedAt = item.AddedAt
                });
                continue;
            }

            // Sản phẩm soft-delete hoặc đã bán
            string? unavailableReason = null;
            if (!product.IsActive)
                unavailableReason = "Sản phẩm đã bị gỡ khỏi cửa hàng.";
            else if (product.Status == ProductStatus.Sold)
                unavailableReason = "Sản phẩm đã được bán.";
            else if (product.Status == ProductStatus.Reserved)
                unavailableReason = "Sản phẩm đang được giữ cho người khác.";

            bool isAvailable = unavailableReason == null;
            var priceChanged = product.Price != item.PriceSnapshot;

            // Sản phẩm không còn mua được thì bỏ tick luôn, để checkout không bị vướng
            // vào món mà user đã không thể mua (giữ món trong giỏ để tự xóa/cảnh báo).
            bool isSelected = item.IsSelected && isAvailable;

            responses.Add(new CartItemResponse
            {
                Id = item.Id,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                IsSelected = isSelected,
                ProductTitle = product.Title,
                ProductSlug = product.Slug,
                ProductImageUrl = product.ImageUrls.FirstOrDefault(),
                Size = product.Size,
                Color = product.Color,
                Condition = product.Condition.ToString(),
                ProductStatus = product.Status.ToString(),
                StockQuantity = product.StockQuantity,
                CurrentPrice = product.Price,
                PriceSnapshot = item.PriceSnapshot,
                PriceChanged = priceChanged,
                IsAvailable = isAvailable,
                UnavailableReason = unavailableReason,
                LineTotal = product.Price * item.Quantity,
                AddedAt = item.AddedAt
            });
        }

        return responses;
    }

    /// <summary>
    /// Tạo thông báo lỗi dựa trên trạng thái thương mại của sản phẩm.
    /// </summary>
    private static string BuildUnavailableMessage(ProductStatus status) => status switch
    {
        ProductStatus.Sold => "Sản phẩm đã được bán.",
        ProductStatus.Reserved => "Sản phẩm đang được giữ cho người khác.",
        _ => "Sản phẩm hiện không còn khả dụng."
    };

    /// <summary>
    /// Tạo ApiResponse thất bại.
    /// </summary>
    private static ApiResponse Fail(string message, string errorCode = ApiErrorCode.Validation)
        => new ApiResponse { Success = false, Message = message, ErrorCode = errorCode };

    /// <summary>
    /// Tạo ApiResponse thất bại do không tìm thấy tài nguyên.
    /// </summary>
    private static ApiResponse FailNotFound(string message)
        => new ApiResponse { Success = false, Message = message, ErrorCode = ApiErrorCode.NotFound };
}
