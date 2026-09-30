using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using MongoDB.Bson;
using System.Text.RegularExpressions;

namespace REWEAR.Application.Services;

/// <summary>
/// Service xử lý Business Logic cho Brand.
/// </summary>
public class BrandService : IBrandService
{
    private readonly IBrandRepository _brandRepository;

    public BrandService(IBrandRepository brandRepository)
    {
        _brandRepository = brandRepository;
    }

    /// <summary>
    /// Lấy tất cả thương hiệu.
    /// </summary>
    public async Task<List<BrandResponse>> GetAllAsync()
    {
        return await GetAllAsync(null);
    }

    /// <summary>
    /// Lấy tất cả thương hiệu với filter theo trạng thái IsActive.
    /// </summary>
    public async Task<List<BrandResponse>> GetAllAsync(bool? isActive)
    {
        var brands = await _brandRepository.GetAllAsync(isActive);
        return brands.Select(MapToResponse).ToList();
    }

    /// <summary>
    /// Lấy thương hiệu theo Id.
    /// </summary>
    public async Task<BrandResponse?> GetByIdAsync(string id)
    {
        // Validate MongoDB ObjectId
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        var brand = await _brandRepository.GetByIdAsync(id);
        return brand != null ? MapToResponse(brand) : null;
    }

    /// <summary>
    /// Tạo thương hiệu mới.
    /// </summary>
    public async Task<BrandResponse> CreateAsync(CreateBrandRequest request)
    {
        // ===== VALIDATION =====
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Tên thương hiệu không được để trống.");
        }

        // Trim và validate
        string trimmedName = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ArgumentException("Tên thương hiệu không được chỉ chứa khoảng trắng.");
        }

        // Kiểm tra trùng tên (case-insensitive)
        var existingBrand = await _brandRepository.GetByNameAsync(trimmedName);
        if (existingBrand != null)
        {
            throw new InvalidOperationException($"Thương hiệu '{trimmedName}' đã tồn tại.");
        }

        // ===== TẠO BRAND =====
        var brand = new Brand
        {
            Name = trimmedName,
            Slug = GenerateSlug(trimmedName),
            Description = request.Description?.Trim(),
            LogoUrl = request.LogoUrl?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _brandRepository.CreateAsync(brand);

        return MapToResponse(brand);
    }

    /// <summary>
    /// Cập nhật thương hiệu.
    /// </summary>
    public async Task<BrandResponse?> UpdateAsync(string id, UpdateBrandRequest request)
    {
        // Validate MongoDB ObjectId
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        // Tìm brand hiện tại
        var brand = await _brandRepository.GetByIdAsync(id);
        if (brand == null)
        {
            return null;
        }

        // ===== VALIDATION =====
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Tên thương hiệu không được để trống.");
        }

        string trimmedName = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ArgumentException("Tên thương hiệu không được chỉ chứa khoảng trắng.");
        }

        // Kiểm tra trùng tên với brand khác (case-insensitive)
        var existingBrand = await _brandRepository.GetByNameAsync(trimmedName);
        if (existingBrand != null && existingBrand.Id != id)
        {
            throw new InvalidOperationException($"Thương hiệu '{trimmedName}' đã tồn tại.");
        }

        // ===== CẬP NHẬT =====
        string oldName = brand.Name;
        brand.Name = trimmedName;
        
        // Regenerate slug nếu tên thay đổi
        if (oldName != trimmedName)
        {
            brand.Slug = GenerateSlug(trimmedName);
        }
        
        brand.Description = request.Description?.Trim();
        brand.LogoUrl = request.LogoUrl?.Trim();
        brand.IsActive = request.IsActive;
        brand.UpdatedAt = DateTime.UtcNow;

        await _brandRepository.UpdateAsync(brand);

        return MapToResponse(brand);
    }

    /// <summary>
    /// Xóa thương hiệu (soft delete - đặt IsActive = false).
    /// </summary>
    public async Task<bool> DeleteAsync(string id)
    {
        // Validate MongoDB ObjectId
        if (!ObjectId.TryParse(id, out _))
        {
            return false;
        }

        // Tìm brand hiện tại
        var brand = await _brandRepository.GetByIdAsync(id);
        if (brand == null)
        {
            return false;
        }

        // Soft delete: đặt IsActive = false
        brand.IsActive = false;
        brand.UpdatedAt = DateTime.UtcNow;

        await _brandRepository.UpdateAsync(brand);

        return true;
    }

    /// <summary>
    /// Khôi phục thương hiệu đã bị soft delete (đặt IsActive = true).
    /// Idempotent: nếu brand đã active thì vẫn trả success.
    /// </summary>
    public async Task<BrandResponse?> RestoreAsync(string id)
    {
        // Validate MongoDB ObjectId
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        // Tìm brand hiện tại
        var brand = await _brandRepository.GetByIdAsync(id);
        if (brand == null)
        {
            return null;
        }

        // Khôi phục: đặt IsActive = true
        // Idempotent: nếu đã active thì vẫn ok
        brand.IsActive = true;
        brand.UpdatedAt = DateTime.UtcNow;

        await _brandRepository.UpdateAsync(brand);

        return MapToResponse(brand);
    }

    /// <summary>
    /// Generate slug từ tên thương hiệu.
    /// Ví dụ: "Levi's" → "levis", "New Balance" → "new-balance"
    /// </summary>
    private static string GenerateSlug(string name)
    {
        // Lowercase
        string slug = name.ToLowerInvariant();

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
    /// Map Brand entity sang BrandResponse DTO.
    /// </summary>
    private static BrandResponse MapToResponse(Brand brand)
    {
        return new BrandResponse
        {
            Id = brand.Id,
            Name = brand.Name,
            Slug = brand.Slug,
            Description = brand.Description,
            LogoUrl = brand.LogoUrl,
            IsActive = brand.IsActive,
            CreatedAt = brand.CreatedAt,
            UpdatedAt = brand.UpdatedAt
        };
    }
}
