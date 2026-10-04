namespace REWEAR.Domain.Entities;

/// <summary>
/// Các quyền (permission) trong hệ thống.
/// Dùng để phân quyền chi tiết hơn role: một role có thể có nhiều quyền,
/// và mỗi API chỉ yêu cầu đúng quyền cần thiết.
/// </summary>
public static class Permission
{
    // ====== QUẢN LÝ NGƯỜI DÙNG ======
    public const string UserRead = "user:read";
    public const string UserUpdate = "user:update";
    public const string UserDelete = "user:delete";
    public const string UserUpdateRole = "user:update-role";

    // ====== QUẢN LÝ SẢN PHẨM ======
    public const string ProductRead = "product:read";
    public const string ProductCreate = "product:create";
    public const string ProductUpdate = "product:update";
    public const string ProductDelete = "product:delete";

    // ====== QUẢN LÝ ĐƠN HÀNG ======
    public const string OrderRead = "order:read";
    public const string OrderUpdate = "order:update";
    public const string OrderCancel = "order:cancel";

    // ====== VẬN CHUYỂN ======
    public const string ShippingRead = "shipping:read";
    public const string ShippingUpdate = "shipping:update";

    // ====== VOUCHER ======
    public const string VoucherManage = "voucher:manage";

    // ====== THỐNG KÊ ======
    public const string DashboardView = "dashboard:view";

    /// <summary>
    /// Danh sách tất cả permission đang có trong hệ thống.
    /// Được dùng để sinh policy động trong Program.cs.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        UserRead, UserUpdate, UserDelete, UserUpdateRole,
        ProductRead, ProductCreate, ProductUpdate, ProductDelete,
        OrderRead, OrderUpdate, OrderCancel,
        ShippingRead, ShippingUpdate,
        VoucherManage,
        DashboardView
    };
}

/// <summary>
/// Bảng mapping: role nào có quyền nào.
/// Admin có toàn bộ quyền. Các role khác chỉ có quyền cần thiết cho công việc.
/// </summary>
public static class RolePermissions
{
    private static readonly Dictionary<UserRole, HashSet<string>> _map = new()
    {
        // Admin: toàn bộ quyền
        [UserRole.Admin] = new HashSet<string>(Permission.All),

        // Staff: quản lý sản phẩm và đơn hàng, xem người dùng và thống kê
        [UserRole.Staff] = new HashSet<string>
        {
            Permission.UserRead,
            Permission.ProductRead, Permission.ProductCreate, Permission.ProductUpdate,
            Permission.OrderRead, Permission.OrderUpdate,
            Permission.DashboardView
        },

        // Shipper: xem đơn hàng được giao và cập nhật trạng thái vận chuyển
        [UserRole.Shipper] = new HashSet<string>
        {
            Permission.OrderRead,
            Permission.ShippingRead, Permission.ShippingUpdate
        },

        // Member: không có quyền quản trị nào
        [UserRole.Member] = new HashSet<string>(Array.Empty<string>())
    };

    /// <summary>
    /// Lấy danh sách permission của một role.
    /// </summary>
    public static IReadOnlyCollection<string> GetPermissions(UserRole role)
        => _map.TryGetValue(role, out var permissions) ? permissions : Array.Empty<string>();

    /// <summary>
    /// Lấy danh sách role name được phép thực hiện một permission.
    /// Dùng để sinh policy: RequireRole(roles).
    /// </summary>
    public static string[] GetRolesFor(string permission)
        => _map
            .Where(kvp => kvp.Value.Contains(permission))
            .Select(kvp => kvp.Key.ToString())
            .ToArray();
}
