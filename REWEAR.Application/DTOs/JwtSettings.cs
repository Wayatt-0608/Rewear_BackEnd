namespace REWEAR.Application.DTOs;

/// <summary>
/// Class chứa cấu hình JWT để truyền giữa các layer.
/// Tránh phụ thuộc vào Microsoft.Extensions.Configuration.
/// </summary>
public class JwtSettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "RewearAPI";
    public string Audience { get; set; } = "RewearApp";
    public int ExpiryHours { get; set; } = 24;
}