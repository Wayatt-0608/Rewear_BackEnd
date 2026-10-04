using REWEAR.Application.DTOs;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface cho Profile Service.
/// </summary>
public interface IProfileService
{
    // User Profile
    Task<ProfileResponse?> GetProfileAsync(string userId);
    Task<ApiResponse> UpdateProfileAsync(string userId, UpdateProfileRequest request);
    Task<ApiResponse> UpdateAvatarAsync(string userId, string avatarUrl);

    // Address CRUD
    Task<List<AddressResponse>> GetAddressesAsync(string userId);
    Task<AddressResponse?> GetAddressByIdAsync(string addressId, string userId);
    Task<ApiResponse> CreateAddressAsync(string userId, AddressRequest request);
    Task<ApiResponse> UpdateAddressAsync(string addressId, string userId, AddressRequest request);
    Task<ApiResponse> DeleteAddressAsync(string addressId, string userId);
    Task<ApiResponse> SetDefaultAddressAsync(string addressId, string userId);
}
