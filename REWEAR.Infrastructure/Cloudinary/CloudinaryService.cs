using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Logging;
using REWEAR.Application.Interfaces;

// Alias để tránh xung đột với CloudinaryDotNet.Actions.ImageUploadResult
using ImageUploadResult = REWEAR.Application.Interfaces.ImageUploadResult;
using ImageUploadRequest = REWEAR.Application.Interfaces.ImageUploadRequest;

namespace REWEAR.Infrastructure.Cloudinary;

/// <summary>
/// Cloudinary Service - upload và xóa hình ảnh trên Cloudinary.
/// </summary>
public class CloudinaryService : ICloudinaryService
{
    private readonly CloudinaryDotNet.Cloudinary _cloudinary;
    private readonly CloudinarySettings _settings;
    private readonly ILogger<CloudinaryService> _logger;

    // Allowed image types
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    public CloudinaryService(
        CloudinarySettings settings,
        ILogger<CloudinaryService> logger)
    {
        _settings = settings;
        _logger = logger;

        var account = new Account(
            _settings.CloudName,
            _settings.ApiKey,
            _settings.ApiSecret);

        _cloudinary = new CloudinaryDotNet.Cloudinary(account);
        _cloudinary.Api.Secure = true; // Dùng HTTPS
    }

    public async Task<ImageUploadResult> UploadImageAsync(ImageUploadRequest request)
    {
        try
        {
            // ===== Validation =====
            if (request.FileStream == null || request.FileStream.Length == 0)
            {
                return new ImageUploadResult
                {
                    Success = false,
                    ErrorMessage = "File rỗng hoặc không hợp lệ."
                };
            }

            // Check extension
            var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
            {
                return new ImageUploadResult
                {
                    Success = false,
                    ErrorMessage = $"Định dạng không hỗ trợ: {extension}. Chỉ chấp nhận: {string.Join(", ", AllowedExtensions)}"
                };
            }

            // Check size
            if (request.FileStream.Length > MaxFileSizeBytes)
            {
                return new ImageUploadResult
                {
                    Success = false,
                    ErrorMessage = $"File quá lớn ({request.FileStream.Length / 1024 / 1024}MB). Tối đa 5MB."
                };
            }

            // ===== Upload to Cloudinary =====
            // Folder path: {rootFolder}/{subFolder} (vd: rewear/avatars)
            var fullFolder = $"{_settings.Folder}/{request.Folder}";

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(request.FileName, request.FileStream),
                Folder = fullFolder,
                PublicId = request.PublicId,
                Overwrite = true,
                UniqueFilename = false,
                Transformation = new Transformation()
                        .Quality("auto")
                        .FetchFormat("auto")
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            if (uploadResult.Error != null)
            {
                _logger.LogError("Cloudinary upload error: {Error}", uploadResult.Error.Message);
                return new ImageUploadResult
                {
                    Success = false,
                    ErrorMessage = uploadResult.Error.Message
                };
            }

            _logger.LogInformation(
                "Uploaded image to Cloudinary: {Url} (PublicId: {PublicId})",
                uploadResult.SecureUrl,
                uploadResult.PublicId);

            return new ImageUploadResult
            {
                Success = true,
                Url = uploadResult.SecureUrl?.ToString(),
                PublicId = uploadResult.PublicId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while uploading to Cloudinary");
            return new ImageUploadResult
            {
                Success = false,
                ErrorMessage = $"Lỗi server khi upload: {ex.Message}"
            };
        }
    }

    public async Task<bool> DeleteImageAsync(string publicId)
    {
        try
        {
            if (string.IsNullOrEmpty(publicId))
                return false;

            var deleteParams = new DeletionParams(publicId);
            var result = await _cloudinary.DestroyAsync(deleteParams);

            var success = result.Result == "ok";
            if (!success)
            {
                _logger.LogWarning(
                    "Failed to delete image from Cloudinary: {PublicId}, Status: {Result}",
                    publicId,
                    result.Result);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while deleting from Cloudinary");
            return false;
        }
    }
}