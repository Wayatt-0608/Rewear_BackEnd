// Tool tạm: seed dữ liệu user vào MongoDB (dev/test).
//
// Tài khoản tạo ra (mật khẩu chung: 12345678):
//   admin@gmail.com  -> Admin
//   staff@gmail.com  -> Staff
//   ship@gmail.com   -> Shipper
//   <họ><tênđệm><tên>@gmail.com -> Member
//
// Quy tắc email member (viết tắt đầy đủ họ + tên đệm + tên):
//   "Phan Quốc Lâm" -> "phanquoclam@gmail.com"
//   "Nguyễn Văn An" -> "nguyenvanan@gmail.com"
// Khi trùng email -> thêm số đa dạng: "phanquoclam2@gmail.com", "phanquoclam3@gmail.com"...
using MongoDB.Bson;
using MongoDB.Driver;
using System.Globalization;
using System.Text;

internal static class UserSeed
{
    private const string Password = "12345678";

    public static async Task SeedAsync(IMongoDatabase db, int memberCount, bool replaceAll = false)
    {
        var users = db.GetCollection<BsonDocument>("Users");

        // BCrypt.Net-Next 4.2.0 - cùng thư viện AuthService dùng để hash khi đăng ký.
        string hash = BCrypt.Net.BCrypt.HashPassword(Password, BCrypt.Net.BCrypt.GenerateSalt(11));
        Console.WriteLine($"Hash BCrypt (cost 11): {hash}");

        var now = DateTime.UtcNow;
        var docs = new List<BsonDocument>();

        // Enum UserRole được Mongo driver serialize thành int
        // (0=Member, 1=Admin, 2=Staff, 3=Shipper), khớp format document User đã lưu.
        BsonDocument MakeUser(string fullName, string email, int role)
        {
            return new BsonDocument
            {
                { "_id", ObjectId.GenerateNewId() },
                { "OtpAttempts", 0 },
                { "fullName", fullName },
                { "email", email.ToLower().Trim() },
                { "passwordHash", hash },
                { "isVerified", true },
                { "otpCode", BsonNull.Value },
                { "otpExpiresAt", BsonNull.Value },
                { "gender", BsonNull.Value },
                { "avatarUrl", BsonNull.Value },
                { "role", role },
                { "createdAt", now },
                { "updatedAt", now }
            };
        }

        docs.Add(MakeUser("Quản Trị Viên", "admin@gmail.com", 1));
        docs.Add(MakeUser("Nhân Viên Bán Hàng", "staff@gmail.com", 2));
        docs.Add(MakeUser("Shipper Giao Hàng", "ship@gmail.com", 3));

        foreach (var (fullName, email) in BuildMembers(memberCount))
            docs.Add(MakeUser(fullName, email, 0));

        // Xóa các user cùng email trước để seed chạy lại được (unique index trên email).
        var emails = docs.Select(d => d["email"].AsString).ToList();
        var deleted = await users.DeleteManyAsync(Builders<BsonDocument>.Filter.In("email", emails));
        if (deleted.DeletedCount > 0)
            Console.WriteLine($"Đã xóa {deleted.DeletedCount} user cũ trùng email để seed lại được.");

        // replaceAll: xóa TOÀN BỘ user cũ (dùng khi đổi quy tắc sinh email,
        // để không sót lại user theo format cũ).
        if (replaceAll)
        {
            var all = await users.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
            Console.WriteLine($"Đã xóa toàn bộ {all.DeletedCount} user cũ (replaceAll).");
        }

        await users.InsertManyAsync(docs);
        Console.WriteLine($"Đã seed {docs.Count} user vào collection 'Users' của database:");
        Console.WriteLine("  admin@gmail.com  (Admin)");
        Console.WriteLine("  staff@gmail.com  (Staff)");
        Console.WriteLine("  ship@gmail.com   (Shipper)");
        Console.WriteLine($"  {memberCount} member dạng <ho><tendem><ten>@gmail.com (Member)");
        Console.WriteLine($"Mật khẩu chung: {Password}");
    }

    /// <summary>
    /// Xem trước danh sách member sẽ tạo mà không ghi vào database.
    /// </summary>
    public static void Preview(int count)
    {
        var members = BuildMembers(count);
        Console.WriteLine($"Sẽ tạo {members.Count} member:");
        foreach (var (fullName, email) in members)
            Console.WriteLine($"  {fullName,-26} {email}");
    }

    /// <summary>
    /// Sinh danh sách (họ tên, email) cho member.
    /// Email = họ + tên đệm + tên, bỏ dấu, lowercase, bỏ khoảng trắng.
    /// Nếu trùng -> thêm số đa dạng (2, 3, 4...).
    /// </summary>
    private static List<(string FullName, string Email)> BuildMembers(int count)
    {
        string[] ho =
        {
            "Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Huỳnh", "Phan", "Vũ", "Võ", "Đặng",
            "Bùi", "Đỗ", "Hồ", "Ngô", "Dương", "Lý", "Đinh", "Tô", "Cao", "Mai",
            "Tạ", "Chu", "Đoàn", "Lâm", "Lưu", "Trịnh", "Kiều", "Bạch", "Đào", "Hà"
        };

        string[] tenDem =
        {
            "Văn", "Thị", "Hữu", "Đức", "Ngọc", "Quốc", "Anh", "Thu", "Minh", "Thanh",
            "Hải", "Xuân", "Hoài", "Nguyên", "Bảo", "Tùng", "Công", "Đình", "Nhật", "Quang",
            "Trung", "Vĩnh", "Tấn", "Đại", "Việt", "Khắc", "Phước", "Nhựt", "Thành", "Đông"
        };

        string[] ten =
        {
            "An", "Bình", "Cường", "Dũng", "Giang", "Hà", "Hùng", "Khánh", "Long", "Mai",
            "Nam", "Nhung", "Oanh", "Phúc", "Quang", "Sơn", "Trang", "Tuấn", "Vy", "Yến",
            "Anh", "Duy", "Hằng", "Khoa", "Linh", "My", "Nhi", "Phong", "Quỳnh", "Trúc",
            "Tâm", "Thảo", "Tuệ", "Vân", "Châu", "Đạt", "Hồng", "Khôi", "Loan", "Ngân"
        };

        var result = new List<(string FullName, string Email)>();
        var usedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < count; i++)
        {
            // Ba thành phần dùng bước nhảy khác nhau (các số nguyên tố tương đối với độ dài mảng)
            // nên họ / tên đệm / tên đều thay đổi liên tục -> tên đa dạng, ít lặp.
            string fullName = $"{ho[i % ho.Length]} {tenDem[(i * 7 + 3) % tenDem.Length]} {ten[(i * 11 + 5) % ten.Length]}";
            string baseEmail = Slugify(fullName);

            // Số thứ tự chỉ thêm khi email đã bị trùng.
            string email = $"{baseEmail}@gmail.com";
            int suffix = 2;
            while (!usedEmails.Add(email))
            {
                email = $"{baseEmail}{suffix}@gmail.com";
                suffix++;
            }

            result.Add((fullName, email));
        }

        return result;
    }

    /// <summary>
    /// Bỏ dấu tiếng Việt, lowercase, bỏ khoảng trắng và mọi ký tự không phải chữ/số.
    /// "Phan Quốc Lâm" -> "phanquoclam"
    /// Lưu ý: các chữ Đ/đ, Ơ/ơ, Ư/ư không phải là dấu NonSpacingMark nên phải thay thế thủ công,
    /// nếu không email sẽ chứa ký tự Unicode và không gõ được trên form đăng nhập.
    /// </summary>
    private static string Slugify(string fullName)
    {
        var normalized = fullName.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        // Thay các chữ đặc biệt của tiếng Việt trước khi lowercase.
        var ascii = sb.ToString()
            .Replace("Đ", "D").Replace("đ", "d")
            .Replace("Ơ", "O").Replace("ơ", "o")
            .Replace("Ư", "U").Replace("ư", "u")
            .ToLowerInvariant();

        var result = new StringBuilder();
        foreach (var ch in ascii)
            result.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        return string.Concat(result.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
