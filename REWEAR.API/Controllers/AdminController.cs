using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;

namespace REWEAR.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly IProfileService _profileService;

    public AdminController(IUserRepository userRepository, IProfileService profileService)
    {
        _userRepository = userRepository;
        _profileService = profileService;
    }

    /// <summary>
    /// Lấy danh sách người dùng (Admin)
    /// </summary>
    [HttpGet("users")]
    [Authorize(Policy = Permission.UserRead)]
    public async Task<IActionResult> GetAllUsers()
    {
        // TODO: Implement pagination nếu cần
        var users = await _userRepository.GetAllAsync();
        
        var userList = users.Select(u => new {
            u.Id,
            u.FullName,
            u.Email,
            u.PhoneNumber,
            Role = u.Role.ToString(),
            u.IsVerified,
            u.CreatedAt
        });

        return Ok(new ApiResponse { Success = true, Data = userList });
    }

    /// <summary>
    /// Lấy chi tiết một người dùng (Admin)
    /// </summary>
    [HttpGet("users/{id}")]
    [Authorize(Policy = Permission.UserRead)]
    public async Task<IActionResult> GetUserById(string id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
            return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy người dùng." });

        var userInfo = new {
            user.Id,
            user.FullName,
            user.Email,
            user.PhoneNumber,
            user.BirthDate,
            user.Gender,
            user.AvatarUrl,
            Role = user.Role.ToString(),
            user.IsVerified,
            user.CreatedAt,
            user.UpdatedAt
        };

        return Ok(new ApiResponse { Success = true, Data = userInfo });
    }

    /// <summary>
    /// Cập nhật vai trò của người dùng (Admin)
    /// </summary>
    [HttpPut("users/{id}/role")]
    [Authorize(Policy = Permission.UserUpdateRole)]
    public async Task<IActionResult> UpdateUserRole(string id, [FromBody] UpdateRoleRequest request)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
            return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy người dùng." });

        // Validate role
        if (!Enum.TryParse<UserRole>(request.Role, true, out var newRole))
            return BadRequest(new ApiResponse 
            { 
                Success = false, 
                Message = "Role không hợp lệ. Các role hợp lệ: Member, Admin, Staff, Shipper" 
            });

        var oldRole = user.Role;
        user.Role = newRole;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);

        return Ok(new ApiResponse
        {
            Success = true,
            Message = $"Đã cập nhật role từ '{oldRole}' thành '{newRole}'.",
            Data = new { UserId = user.Id, OldRole = oldRole.ToString(), NewRole = newRole.ToString() }
        });
    }

    /// <summary>
    /// Xóa người dùng (Admin)
    /// </summary>
    [HttpDelete("users/{id}")]
    [Authorize(Policy = Permission.UserDelete)]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
            return NotFound(new ApiResponse { Success = false, Message = "Không tìm thấy người dùng." });

        // Không cho xóa chính mình
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id == currentUserId)
            return BadRequest(new ApiResponse { Success = false, Message = "Không thể xóa tài khoản của chính mình." });

        await _userRepository.DeleteAsync(id);

        return Ok(new ApiResponse
        {
            Success = true,
            Message = $"Đã xóa tài khoản của user {user.FullName}."
        });
    }
}

public class UpdateRoleRequest
{
    public string Role { get; set; } = string.Empty;
}
