using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using MongoDB.Bson;
using System.Text.RegularExpressions;

namespace REWEAR.Application.Services;

/// <summary>
/// Service xử lý Business Logic cho Category.
/// </summary>
public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _categoryRepository;

    public CategoryService(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    /// <summary>
    /// Lấy tất cả danh mục.
    /// </summary>
    public async Task<List<CategoryResponse>> GetAllAsync()
    {
        return await GetAllAsync(null);
    }

    /// <summary>
    /// Lấy tất cả danh mục với filter theo trạng thái IsActive.
    /// </summary>
    public async Task<List<CategoryResponse>> GetAllAsync(bool? isActive)
    {
        var categories = await _categoryRepository.GetAllAsync(isActive);
        return categories.Select(MapToResponse).ToList();
    }

    /// <summary>
    /// Lấy danh mục theo Id.
    /// </summary>
    public async Task<CategoryResponse?> GetByIdAsync(string id)
    {
        // Validate MongoDB ObjectId
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        var category = await _categoryRepository.GetByIdAsync(id);
        return category != null ? MapToResponse(category) : null;
    }

    /// <summary>
    /// Tạo danh mục mới.
    /// </summary>
    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request)
    {
        // ===== VALIDATION =====
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Tên danh mục không được để trống.");
        }

        // Trim và validate
        string trimmedName = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ArgumentException("Tên danh mục không được chỉ chứa khoảng trắng.");
        }

        // Kiểm tra trùng tên (case-insensitive)
        var existingCategory = await _categoryRepository.GetByNameAsync(trimmedName);
        if (existingCategory != null)
        {
            throw new InvalidOperationException($"Danh mục '{trimmedName}' đã tồn tại.");
        }

        // ===== TẠO CATEGORY =====
        var category = new Category
        {
            Name = trimmedName,
            Slug = GenerateSlug(trimmedName),
            Description = request.Description?.Trim(),
            ImageUrl = request.ImageUrl?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _categoryRepository.CreateAsync(category);

        return MapToResponse(category);
    }

    /// <summary>
    /// Cập nhật danh mục.
    /// </summary>
    public async Task<CategoryResponse?> UpdateAsync(string id, UpdateCategoryRequest request)
    {
        // Validate MongoDB ObjectId
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        // Tìm category hiện tại
        var category = await _categoryRepository.GetByIdAsync(id);
        if (category == null)
        {
            return null;
        }

        // ===== VALIDATION =====
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Tên danh mục không được để trống.");
        }

        string trimmedName = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ArgumentException("Tên danh mục không được chỉ chứa khoảng trắng.");
        }

        // Kiểm tra trùng tên với category khác (case-insensitive)
        var existingCategory = await _categoryRepository.GetByNameAsync(trimmedName);
        if (existingCategory != null && existingCategory.Id != id)
        {
            throw new InvalidOperationException($"Danh mục '{trimmedName}' đã tồn tại.");
        }

        // ===== CẬP NHẬT =====
        string oldName = category.Name;
        category.Name = trimmedName;
        
        // Regenerate slug nếu tên thay đổi
        if (oldName != trimmedName)
        {
            category.Slug = GenerateSlug(trimmedName);
        }
        
        category.Description = request.Description?.Trim();
        category.ImageUrl = request.ImageUrl?.Trim();
        category.IsActive = request.IsActive;
        category.UpdatedAt = DateTime.UtcNow;

        await _categoryRepository.UpdateAsync(category);

        return MapToResponse(category);
    }

    /// <summary>
    /// Xóa danh mục (soft delete - đặt IsActive = false).
    /// </summary>
    public async Task<bool> DeleteAsync(string id)
    {
        // Validate MongoDB ObjectId
        if (!ObjectId.TryParse(id, out _))
        {
            return false;
        }

        // Tìm category hiện tại
        var category = await _categoryRepository.GetByIdAsync(id);
        if (category == null)
        {
            return false;
        }

        // Soft delete: đặt IsActive = false
        category.IsActive = false;
        category.UpdatedAt = DateTime.UtcNow;

        await _categoryRepository.UpdateAsync(category);

        return true;
    }

    /// <summary>
    /// Khôi phục danh mục đã bị soft delete (đặt IsActive = true).
    /// Idempotent: nếu category đã active thì vẫn trả success.
    /// </summary>
    public async Task<CategoryResponse?> RestoreAsync(string id)
    {
        // Validate MongoDB ObjectId
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        // Tìm category hiện tại
        var category = await _categoryRepository.GetByIdAsync(id);
        if (category == null)
        {
            return null;
        }

        // Khôi phục: đặt IsActive = true
        // Idempotent: nếu đã active thì vẫn ok
        category.IsActive = true;
        category.UpdatedAt = DateTime.UtcNow;

        await _categoryRepository.UpdateAsync(category);

        return MapToResponse(category);
    }

    /// <summary>
    /// Generate slug từ tên danh mục.
    /// Ví dụ: "Tops" → "tops", "Vintage Tops" → "vintage-tops"
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
    /// Map Category entity sang CategoryResponse DTO.
    /// </summary>
    private static CategoryResponse MapToResponse(Category category)
    {
        return new CategoryResponse
        {
            Id = category.Id,
            Name = category.Name,
            Slug = category.Slug,
            Description = category.Description,
            ImageUrl = category.ImageUrl,
            IsActive = category.IsActive,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt
        };
    }
}
