using REWEAR.Application.DTOs;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface cho Sourcing Service - business logic layer.
/// </summary>
public interface ISourcingService
{
    /// <summary>
    /// Tạo sourcing request mới (kèm upload ảnh).
    /// </summary>
    Task<SourcingResponse> CreateAsync(string userId, CreateSourcingRequest request);

    /// <summary>
    /// Lấy tất cả sourcing request của một user.
    /// </summary>
    Task<List<SourcingResponse>> GetByUserIdAsync(string userId);

    /// <summary>
    /// Lấy sourcing request theo Id.
    /// </summary>
    Task<SourcingResponse?> GetByIdAsync(string id);

    /// <summary>
    /// Lấy tất cả sourcing request (Staff/Admin).
    /// </summary>
    Task<List<SourcingResponse>> GetAllAsync(SourcingStatus? status = null);

    /// <summary>
    /// Staff/Admin: Bắt đầu review.
    /// </summary>
    Task<SourcingResponse?> StartReviewAsync(string id);

    /// <summary>
    /// Staff/Admin: Định giá.
    /// </summary>
    Task<SourcingResponse?> OfferPriceAsync(string id, OfferSourcingRequest request);

    /// <summary>
    /// Staff/Admin: Từ chối.
    /// </summary>
    Task<SourcingResponse?> RejectAsync(string id, RejectSourcingRequest request);

    /// <summary>
    /// Customer: Đồng ý giá.
    /// </summary>
    Task<SourcingResponse?> AcceptOfferAsync(string id, string userId);

    /// <summary>
    /// Customer: Từ chối giá.
    /// </summary>
    Task<SourcingResponse?> DeclineOfferAsync(string id, string userId);

    /// <summary>
    /// Staff/Admin: Đánh dấu đã nhận hàng.
    /// </summary>
    Task<SourcingResponse?> MarkReceivedAsync(string id);

    /// <summary>
    /// Staff/Admin: Kiểm định và chấp nhận.
    /// </summary>
    Task<SourcingResponse?> InspectAsync(string id, InspectSourcingRequest request);

    /// <summary>
    /// Staff/Admin: Convert sang Product.
    /// </summary>
    Task<SourcingResponse?> ConvertToProductAsync(string id, ConvertToProductRequest request);
}
