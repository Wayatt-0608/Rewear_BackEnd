using Microsoft.IdentityModel.Tokens;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;

namespace REWEAR.Application.Services;

/// <summary>
/// Service xử lý User Profile & Address.
/// </summary>
public class ProfileService : IProfileService
{
    private readonly IUserRepository _userRepository;
    private readonly IAddressRepository _addressRepository;

    public ProfileService(IUserRepository userRepository, IAddressRepository addressRepository)
    {
        _userRepository = userRepository;
        _addressRepository = addressRepository;
    }

    // ============================================
    // USER PROFILE
    // ============================================

    /// <summary>
    /// Lấy thông tin profile của user.
    /// </summary>
    public async Task<ProfileResponse?> GetProfileAsync(string userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null) return null;

        return new ProfileResponse
        {
            Id = user.Id,
            FullName = user.FullName,
            Gender = user.Gender ?? string.Empty,
            Email = user.Email,
            AvatarUrl = user.AvatarUrl,
            CreatedAt = user.CreatedAt
        };
    }

    /// <summary>
    /// Cập nhật thông tin cá nhân.
    /// </summary>
    public async Task<ApiResponse> UpdateProfileAsync(string userId, UpdateProfileRequest request)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            return new ApiResponse { Success = false, Message = "Không tìm thấy người dùng." };

        // Cập nhật các trường được phép
        if (!string.IsNullOrWhiteSpace(request.FullName))
            user.FullName = request.FullName.Trim();

        if (request.Gender != null)
            user.Gender = request.Gender;

        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);

        return new ApiResponse
        {
            Success = true,
            Message = "Cập nhật thông tin thành công."
        };
    }

    /// <summary>
    /// Cập nhật avatar.
    /// </summary>
    public async Task<ApiResponse> UpdateAvatarAsync(string userId, string avatarUrl)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            return new ApiResponse { Success = false, Message = "Không tìm thấy người dùng." };

        user.AvatarUrl = avatarUrl;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);

        return new ApiResponse
        {
            Success = true,
            Message = "Cập nhật avatar thành công.",
            Data = new { AvatarUrl = avatarUrl }
        };
    }

    // ============================================
    // ADDRESS CRUD
    // ============================================

    /// <summary>
    /// Lấy danh sách địa chỉ của user.
    /// </summary>
    public async Task<List<AddressResponse>> GetAddressesAsync(string userId)
    {
        var addresses = await _addressRepository.GetByUserIdAsync(userId);
        return addresses.Select(MapToResponse).ToList();
    }

    /// <summary>
    /// Lấy chi tiết 1 địa chỉ.
    /// </summary>
    public async Task<AddressResponse?> GetAddressByIdAsync(string addressId, string userId)
    {
        var address = await _addressRepository.GetByIdAsync(addressId);
        if (address == null || address.UserId != userId)
            return null;
        return MapToResponse(address);
    }

    /// <summary>
    /// Thêm địa chỉ mới.
    /// </summary>
    public async Task<ApiResponse> CreateAddressAsync(string userId, AddressRequest request)
    {
        // Validate
        if (string.IsNullOrWhiteSpace(request.RecipientName))
            return new ApiResponse { Success = false, Message = "Tên người nhận không được để trống." };

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return new ApiResponse { Success = false, Message = "Số điện thoại không được để trống." };

        if (!Regex.IsMatch(request.PhoneNumber, @"^(0[0-9]{9,10})$"))
            return new ApiResponse { Success = false, Message = "Số điện thoại không hợp lệ." };

        if (string.IsNullOrWhiteSpace(request.Province) || string.IsNullOrWhiteSpace(request.District) ||
            string.IsNullOrWhiteSpace(request.Ward) || string.IsNullOrWhiteSpace(request.StreetAddress))
            return new ApiResponse { Success = false, Message = "Địa chỉ không được để trống." };

        var address = new Address
        {
            UserId = userId,
            RecipientName = request.RecipientName.Trim(),
            PhoneNumber = request.PhoneNumber.Trim(),
            Province = request.Province.Trim(),
            District = request.District.Trim(),
            Ward = request.Ward.Trim(),
            StreetAddress = request.StreetAddress.Trim(),
            Note = request.Note?.Trim(),
            IsDefault = request.IsDefault
        };

        // Nếu là địa chỉ mặc định, xóa mặc định cũ
        if (address.IsDefault)
        {
            await _addressRepository.ClearDefaultForUserAsync(userId);
        }

        // Kiểm tra nếu đây là địa chỉ đầu tiên thì đặt mặc định
        var existingAddresses = await _addressRepository.GetByUserIdAsync(userId);
        if (existingAddresses.Count == 0)
            address.IsDefault = true;

        await _addressRepository.CreateAsync(address);

        return new ApiResponse
        {
            Success = true,
            Message = "Thêm địa chỉ thành công.",
            Data = MapToResponse(address)
        };
    }

    /// <summary>
    /// Cập nhật địa chỉ.
    /// </summary>
    public async Task<ApiResponse> UpdateAddressAsync(string addressId, string userId, AddressRequest request)
    {
        var address = await _addressRepository.GetByIdAsync(addressId);
        if (address == null || address.UserId != userId)
            return new ApiResponse { Success = false, Message = "Không tìm thấy địa chỉ." };

        // Validate
        if (string.IsNullOrWhiteSpace(request.RecipientName))
            return new ApiResponse { Success = false, Message = "Tên người nhận không được để trống." };

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return new ApiResponse { Success = false, Message = "Số điện thoại không được để trống." };

        if (!Regex.IsMatch(request.PhoneNumber, @"^(0[0-9]{9,10})$"))
            return new ApiResponse { Success = false, Message = "Số điện thoại không hợp lệ." };

        if (string.IsNullOrWhiteSpace(request.Province) || string.IsNullOrWhiteSpace(request.District) ||
            string.IsNullOrWhiteSpace(request.Ward) || string.IsNullOrWhiteSpace(request.StreetAddress))
            return new ApiResponse { Success = false, Message = "Địa chỉ không được để trống." };

        // Nếu đặt mặc định, xóa mặc định cũ
        if (request.IsDefault && !address.IsDefault)
        {
            await _addressRepository.ClearDefaultForUserAsync(userId);
        }

        address.RecipientName = request.RecipientName.Trim();
        address.PhoneNumber = request.PhoneNumber.Trim();
        address.Province = request.Province.Trim();
        address.District = request.District.Trim();
        address.Ward = request.Ward.Trim();
        address.StreetAddress = request.StreetAddress.Trim();
        address.Note = request.Note?.Trim();
        address.IsDefault = request.IsDefault;
        address.UpdatedAt = DateTime.UtcNow;

        await _addressRepository.UpdateAsync(address);

        return new ApiResponse
        {
            Success = true,
            Message = "Cập nhật địa chỉ thành công.",
            Data = MapToResponse(address)
        };
    }

    /// <summary>
    /// Xóa địa chỉ.
    /// </summary>
    public async Task<ApiResponse> DeleteAddressAsync(string addressId, string userId)
    {
        var address = await _addressRepository.GetByIdAsync(addressId);
        if (address == null || address.UserId != userId)
            return new ApiResponse { Success = false, Message = "Không tìm thấy địa chỉ." };

        await _addressRepository.DeleteAsync(addressId);

        // Nếu xóa địa chỉ mặc định, đặt cái khác làm mặc định
        if (address.IsDefault)
        {
            var remaining = await _addressRepository.GetByUserIdAsync(userId);
            if (remaining.Count > 0)
            {
                remaining[0].IsDefault = true;
                remaining[0].UpdatedAt = DateTime.UtcNow;
                await _addressRepository.UpdateAsync(remaining[0]);
            }
        }

        return new ApiResponse
        {
            Success = true,
            Message = "Xóa địa chỉ thành công."
        };
    }

    /// <summary>
    /// Đặt địa chỉ mặc định.
    /// </summary>
    public async Task<ApiResponse> SetDefaultAddressAsync(string addressId, string userId)
    {
        var address = await _addressRepository.GetByIdAsync(addressId);
        if (address == null || address.UserId != userId)
            return new ApiResponse { Success = false, Message = "Không tìm thấy địa chỉ." };

        // Xóa mặc định cũ
        await _addressRepository.ClearDefaultForUserAsync(userId);

        // Đặt mới
        address.IsDefault = true;
        address.UpdatedAt = DateTime.UtcNow;
        await _addressRepository.UpdateAsync(address);

        return new ApiResponse
        {
            Success = true,
            Message = "Đặt địa chỉ mặc định thành công."
        };
    }

    // ============================================
    // HELPER
    // ============================================

    private static AddressResponse MapToResponse(Address address)
    {
        return new AddressResponse
        {
            Id = address.Id,
            RecipientName = address.RecipientName,
            PhoneNumber = address.PhoneNumber,
            Province = address.Province,
            District = address.District,
            Ward = address.Ward,
            StreetAddress = address.StreetAddress,
            FullAddress = address.FullAddress,
            Note = address.Note,
            IsDefault = address.IsDefault,
            CreatedAt = address.CreatedAt,
            UpdatedAt = address.UpdatedAt
        };
    }
}
