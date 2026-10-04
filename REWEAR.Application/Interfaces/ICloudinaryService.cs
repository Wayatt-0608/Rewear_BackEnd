namespace REWEAR.Application.Interfaces;

/// <summary>
/// Kết quả upload hình ảnh lên Cloudinary.
/// </summary>
public class ImageUploadResult
{
    public bool Success { get; set; }
    public string? Url { get; set; }
    public string? PublicId { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Thông tin file cần upload.
/// </summary>
public class ImageUploadRequest
{
    /// <summary>
    /// Stream của file ảnh.
    /// </summary>
    public Stream FileStream { get; set; } = null!;

    /// <summary>
    /// Tên file gốc (vd: "avatar.png").
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Sub-folder trong Cloudinary (vd: "avatars", "products").
    /// </summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>
    /// Tên file trên Cloudinary (không bao gồm extension).
    /// </summary>
    public string PublicId { get; set; } = string.Empty;
}

/// <summary>
/// Interface cho Cloudinary Service - upload/delete ảnh.
/// Không phụ thuộc vào Microsoft.AspNetCore để giữ Application layer thuần.
/// </summary>
public interface ICloudinaryService
{
    /// <summary>
    /// Upload ảnh lên Cloudinary.
    /// </summary>
    Task<ImageUploadResult> UploadImageAsync(ImageUploadRequest request);

    /// <summary>
    /// Xóa ảnh trên Cloudinary.
    /// </summary>
    /// <param name="publicId">PublicId của ảnh cần xóa</param>
    Task<bool> DeleteImageAsync(string publicId);
}