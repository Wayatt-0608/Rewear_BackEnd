using System.Text.Json;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using MongoDB.Bson;
using MongoDB.Driver;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

internal static class FullSeed
{
    private sealed record ProductSeedSpec(
        string Key,
        string Title,
        string SourceTitle,
        string CategorySlug,
        string SourceBrandSlug,
        string Size,
        string Color,
        ProductCondition Condition,
        decimal SellingPrice,
        decimal AcquisitionCost,
        decimal MaterialCost,
        decimal LaborCost,
        string Description,
        string Concept,
        string BeforeLocalPath,
        string[] AfterLocalPaths,
        int DaysAgoStart,
        string SaleStage // "Available" | "Delivered" | "InTransit" | "PendingShip" | "ConfirmedQueue"
    );

    public static async Task RunAsync(IMongoDatabase db)
    {
        var now = DateTime.UtcNow;
        Console.WriteLine($"=== REWEAR FULL SEED (UTC: {now:yyyy-MM-dd HH:mm:ss}) ===");

        // 1. Load Cloudinary config from REWEAR.API/appsettings.Development.json
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        if (!File.Exists(Path.Combine(repoRoot, "REWEAR.API", "appsettings.Development.json")))
            repoRoot = @"d:\EXE212\Rewear_BackEnd";

        var appSettingsPath = Path.Combine(repoRoot, "REWEAR.API", "appsettings.Development.json");
        using var cfgDoc = JsonDocument.Parse(await File.ReadAllTextAsync(appSettingsPath));
        var cloudSec = cfgDoc.RootElement.GetProperty("Cloudinary");
        var cloudinary = new Cloudinary(new Account(
            cloudSec.GetProperty("CloudName").GetString(),
            cloudSec.GetProperty("ApiKey").GetString(),
            cloudSec.GetProperty("ApiSecret").GetString()))
        {
            Api = { Secure = true }
        };

        // 2. Load existing users (NEVER wipe Users!)
        var usersCol = db.GetCollection<User>("Users");
        var allUsers = await usersCol.Find(FilterDefinition<User>.Empty).ToListAsync();

        var admin = allUsers.FirstOrDefault(u => u.Role == UserRole.Admin)
            ?? throw new InvalidOperationException("Chưa có tài khoản Admin trong Users.");
        var staff = allUsers.FirstOrDefault(u => u.Role == UserRole.Staff) ?? admin;
        var shipper = allUsers.FirstOrDefault(u => u.Role == UserRole.Shipper)
            ?? throw new InvalidOperationException("Chưa có tài khoản Shipper trong Users.");

        var members = allUsers.Where(u => u.Role == UserRole.Member).ToList();
        if (members.Count < 12)
            throw new InvalidOperationException($"Cần ít nhất 12 Member trong Users (hiện có {members.Count}).");

        User FindMember(string email, int fallbackIdx) =>
            members.FirstOrDefault(m => m.Email.Equals(email, StringComparison.OrdinalIgnoreCase))
            ?? members[fallbackIdx % members.Count];

        var userLam = FindMember("phanquoclam682004@gmail.com", 0);
        var userTuan = FindMember("doanminhtuan15022k4@gmail.com", 1);
        var userTest = FindMember("member.test@gmail.com", 2);

        Console.WriteLine($"Actors: Admin={admin.Email}, Staff={staff.Email}, Shipper={shipper.Email} ({shipper.FullName})");
        Console.WriteLine($"Total Members found: {members.Count}");

        // 3. Clean business collections (keep Users intact!)
        string[] collectionsToClean =
        {
            "Addresses", "Brands", "Categories", "Products",
            "SourcingRequests", "InventoryItems", "InventoryMovements",
            "PurchasePayouts", "UpcyclingProjects", "QualityInspections",
            "Orders", "OrderStatusHistories", "Payments", "Shippings",
            "StockReservations", "Vouchers", "UserVouchers", "Carts",
            "WishlistItems", "StylingProfiles", "ReturnRequests"
        };
        foreach (var colName in collectionsToClean)
        {
            await db.GetCollection<BsonDocument>(colName).DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
        }
        Console.WriteLine("Đã làm sạch toàn bộ collection nghiệp vụ (giữ nguyên Users).");

        // 4. Define local image roots
        const string feImg = @"d:\EXE212\EXE201\public\images";
        const string draftImg = @"d:\EXE212\EXE201\public\images\catalog-drafts";
        const string brainDir = @"C:\Users\phanq\.gemini\antigravity\brain\0e087b69-3562-4860-a47f-34a6e8bea80b";
        const string scratchImg = @"C:\Users\phanq\.gemini\antigravity\brain\0e087b69-3562-4860-a47f-34a6e8bea80b\scratch\seed-images";

        // 5. Define 12 complete upcycled products (each has 1 Before + 4 After angles)
        var specs = new List<ProductSeedSpec>
        {
            // === 6 AVAILABLE PRODUCTS (SHOWING ON STOREFRONT) ===
            new(
                Key: "p01",
                Title: "Túi tote tái chế từ quần jean",
                SourceTitle: "Quần jean Levi's ống đứng cũ rách gối",
                CategorySlug: "tui-xach-phu-kien",
                SourceBrandSlug: "levis",
                Size: "Một cỡ",
                Color: "Xanh chàm",
                Condition: ProductCondition.Excellent,
                SellingPrice: 329000m,
                AcquisitionCost: 110000m,
                MaterialCost: 35000m,
                LaborCost: 55000m,
                Description: "Thiết kế túi tote dáng chữ nhật tái chế từ quần jean cũ, giữ nguyên túi quần sau làm ngăn ngoài tiện dụng. Hai quai bản dày cùng chất liệu được gia cố đinh tán đồng và đường chỉ nâu nổi bật, phù hợp mang hằng ngày.",
                Concept: "Tuyển chọn phần vải denim 14oz còn chắc từ thân quần jean cũ, dựng phom túi hộp, giữ lại túi quần nguyên bản và gia cố quai chịu lực bằng đinh tán đồng.",
                BeforeLocalPath: Path.Combine(draftImg, "denim-before.png"),
                AfterLocalPaths: new[]
                {
                    Path.Combine(draftImg, "denim-after.png"),
                    Path.Combine(draftImg, "denim-after-back.png"),
                    Path.Combine(draftImg, "denim-after-angle.png"),
                    Path.Combine(draftImg, "denim-after-detail.png"),
                },
                DaysAgoStart: 18,
                SaleStage: "Available"
            ),
            new(
                Key: "p02",
                Title: "Áo sơ mi ghép vải xanh kem",
                SourceTitle: "Hai áo sơ mi cotton cũ xanh sọc & kem",
                CategorySlug: "ao-so-mi",
                SourceBrandSlug: "uniqlo",
                Size: "M",
                Color: "Xanh sọc phối kem",
                Condition: ProductCondition.LikeNew,
                SellingPrice: 349000m,
                AcquisitionCost: 120000m,
                MaterialCost: 30000m,
                LaborCost: 60000m,
                Description: "Sơ mi tay dài dáng suông ghép mảng bất đối xứng từ vải cotton xanh sọc và kem. Túi ngực tương phản tạo điểm nhấn tối giản, giữ nguyên cổ bẻ và hàng cúc thanh lịch, dễ phối cùng quần jean hoặc chân váy.",
                Concept: "Tháo rã hai chiếc áo sơ mi cotton cũ bị sờn cổ và tay, phối lại thân trước, thân sau và túi ngực theo bố cục khối màu bất đối xứng, thay mới toàn bộ cúc.",
                BeforeLocalPath: Path.Combine(draftImg, "shirt-before.png"),
                AfterLocalPaths: new[]
                {
                    Path.Combine(draftImg, "shirt-after.png"),
                    Path.Combine(draftImg, "shirt-after-back.png"),
                    Path.Combine(draftImg, "shirt-after-angle.png"),
                    Path.Combine(draftImg, "shirt-after-detail.png"),
                },
                DaysAgoStart: 17,
                SaleStage: "Available"
            ),
            new(
                Key: "p03",
                Title: "Áo sơ mi kem tinh chỉnh phom tối giản",
                SourceTitle: "Áo sơ mi Oxford rộng phom cũ màu kem",
                CategorySlug: "ao-so-mi",
                SourceBrandSlug: "uniqlo",
                Size: "M",
                Color: "Kem",
                Condition: ProductCondition.Excellent,
                SellingPrice: 310000m,
                AcquisitionCost: 105000m,
                MaterialCost: 28000m,
                LaborCost: 57000m,
                Description: "Áo sơ mi tay dài màu kem trung tính được tái cấu trúc lại phom vai và chiều dài vạt từ áo Oxford cổ điển. Chất cotton mềm thoáng, may giấu đường chỉ tỉ mỉ, phù hợp phong cách tối giản hằng ngày.",
                Concept: "Xử lý làm sạch sinh học, cắt chỉnh lại vai xuôi và bo gấu vạt bầu hiện đại, thay mới bộ cúc xà cừ tự nhiên và gia cố khuy cổ tay.",
                BeforeLocalPath: Path.Combine(brainDir, "upcycled_skirt_01_before_shirts_1795336553631.jpg"),
                AfterLocalPaths: new[]
                {
                    Path.Combine(draftImg, "resale-shirt.png"),
                    Path.Combine(draftImg, "resale-shirt-back.png"),
                    Path.Combine(draftImg, "resale-shirt-angle.png"),
                    Path.Combine(draftImg, "resale-shirt-detail.png"),
                },
                DaysAgoStart: 16,
                SaleStage: "Available"
            ),
            new(
                Key: "p04",
                Title: "Quần jean xanh chàm phục hồi & nâng phom",
                SourceTitle: "Quần dài bảo hộ & denim cũ cần dựng lại phom",
                CategorySlug: "quan-dai",
                SourceBrandSlug: "levis",
                Size: "M",
                Color: "Xanh chàm",
                Condition: ProductCondition.Excellent,
                SellingPrice: 460000m,
                AcquisitionCost: 155000m,
                MaterialCost: 45000m,
                LaborCost: 85000m,
                Description: "Quần jean cạp cao ống đứng cổ điển màu xanh chàm với hiệu ứng wash tự nhiên. Được phục hồi đường may chắp đôi bên sườn, thay khóa đồng chắc chắn và chỉnh tỷ lệ ống tôn dáng.",
                Concept: "Tái gia cố toàn bộ đường may sườn trong và đáy quần, xử lý wash cân bằng sắc chàm, thay bộ nút kim loại và lót túi cotton mộc bền bỉ.",
                BeforeLocalPath: Path.Combine(scratchImg, "p04_before_1.jpg"),
                AfterLocalPaths: new[]
                {
                    Path.Combine(draftImg, "resale-jeans.png"),
                    Path.Combine(draftImg, "resale-jeans-back.png"),
                    Path.Combine(draftImg, "resale-jeans-angle.png"),
                    Path.Combine(draftImg, "resale-jeans-detail.png"),
                },
                DaysAgoStart: 15,
                SaleStage: "Available"
            ),
            new(
                Key: "p05",
                Title: "Áo cardigan len xanh lá dịu dệt nối thủ công",
                SourceTitle: "Áo len cổ tim sợi dày đã qua sử dụng",
                CategorySlug: "ao-len-cardigan",
                SourceBrandSlug: "zara",
                Size: "M",
                Color: "Xanh lá dịu",
                Condition: ProductCondition.LikeNew,
                SellingPrice: 440000m,
                AcquisitionCost: 145000m,
                MaterialCost: 42000m,
                LaborCost: 83000m,
                Description: "Cardigan cổ chữ V tay dài sắc xanh lá dịu mắt, hoàn thiện từ len tái sinh qua công đoạn dệt nối nẹp cúc và bo gân cổ tay. Bề mặt sợi êm nhẹ, lý tưởng cho phối lớp thu đông.",
                Concept: "Hấp phục hồi sợi len, mở thân trước từ áo len cũ thành dáng cardigan cổ V, dệt viền nẹp gân nổi và đính hàng cúc đồng màu thủ công.",
                BeforeLocalPath: Path.Combine(scratchImg, "p05_before_1.jpg"),
                AfterLocalPaths: new[]
                {
                    Path.Combine(draftImg, "resale-cardigan.png"),
                    Path.Combine(draftImg, "resale-cardigan-back.png"),
                    Path.Combine(draftImg, "resale-cardigan-angle.png"),
                    Path.Combine(draftImg, "resale-cardigan-detail.png"),
                },
                DaysAgoStart: 14,
                SaleStage: "Available"
            ),
            new(
                Key: "p06",
                Title: "Chân váy chữ A xám than cắt cúp từ áo dạ",
                SourceTitle: "Áo măng tô dạ xám than dáng dài cũ",
                CategorySlug: "chan-vay",
                SourceBrandSlug: "zara",
                Size: "S",
                Color: "Xám than",
                Condition: ProductCondition.Excellent,
                SellingPrice: 390000m,
                AcquisitionCost: 130000m,
                MaterialCost: 38000m,
                LaborCost: 72000m,
                Description: "Chân váy dáng chữ A dài qua gối sắc xám than thanh lịch, tái chế từ thân vải dạ ép cao cấp của áo măng tô cũ. Cạp gọn giấu khóa kéo, đứng phom đẹp khi phối cùng sơ mi hoặc cardigan.",
                Concept: "Tận dụng phần thân dưới còn nguyên vẹn của áo khoác dạ xám than, dựng rập chân váy chữ A 4 mảnh, thêm lớp lót lụa thoáng và khóa kéo giọt lệ.",
                BeforeLocalPath: Path.Combine(feImg, "prod-charcoal-wool.jpg"),
                AfterLocalPaths: new[]
                {
                    Path.Combine(draftImg, "resale-skirt.png"),
                    Path.Combine(draftImg, "resale-skirt-back.png"),
                    Path.Combine(draftImg, "resale-skirt-angle.png"),
                    Path.Combine(draftImg, "resale-skirt-detail.png"),
                },
                DaysAgoStart: 13,
                SaleStage: "Available"
            ),

            // === 6 SOLD PRODUCTS (FULL ORDER + PAYMENT + SHIPPER + CASHFLOW) ===
            new(
                Key: "p07",
                Title: "Áo khoác phao chống gió tái chế xanh lục bảo",
                SourceTitle: "Áo khoác gió 2 lớp xanh rêu cũ",
                CategorySlug: "ao-khoac",
                SourceBrandSlug: "nike",
                Size: "L",
                Color: "Xanh lá dịu",
                Condition: ProductCondition.LikeNew,
                SellingPrice: 780000m,
                AcquisitionCost: 250000m,
                MaterialCost: 85000m,
                LaborCost: 135000m,
                Description: "Áo khoác phao dáng hộp sắc xanh lục bảo tái chế từ vỏ vải chống gió cũ kết hợp bông giữ nhiệt tái sinh. Mũ trùm liền khối che gió tốt, khóa kéo hai chiều chống thấm và hai túi sườn có khóa.",
                Concept: "Phục hồi bề mặt chống thấm của vải áo gió cũ, chần thêm lớp bông tái sinh nhẹ ấm, dựng mũ cổ cao bảo hộ và thay khóa kéo hai chiều chống nước.",
                BeforeLocalPath: Path.Combine(feImg, "prod-utility-jacket.jpg"),
                AfterLocalPaths: new[]
                {
                    Path.Combine(feImg, "sarna-flatlay.png"),
                    Path.Combine(feImg, "sarna-front.jpg"),
                    Path.Combine(feImg, "sarna-side.jpg"),
                    Path.Combine(feImg, "sarna-flatlay.jpg"),
                },
                DaysAgoStart: 12,
                SaleStage: "Delivered"
            ),
            new(
                Key: "p08",
                Title: "Áo khoác kỹ thuật màu cát tái chế chống nước",
                SourceTitle: "Áo khoác dài vải lanh màu cát cũ",
                CategorySlug: "ao-khoac",
                SourceBrandSlug: "carhartt",
                Size: "L",
                Color: "Kem",
                Condition: ProductCondition.LikeNew,
                SellingPrice: 640000m,
                AcquisitionCost: 210000m,
                MaterialCost: 70000m,
                LaborCost: 115000m,
                Description: "Áo khoác chống mưa nhẹ tông cát tối giản, cắt ngắn phom hiện đại từ áo khoác dáng dài cũ. Tích hợp mũ có lưỡi trai chắn nước, nẹp cổ tay bấm cúc và đường ép seam kín gió.",
                Concept: "Cắt gọn chiều dài từ áo măng tô cũ thành dáng jacket thể thao tối giản, phủ lớp chống trượt nước sinh học, bổ sung mũ trùm hai lớp và khóa kéo ẩn.",
                BeforeLocalPath: Path.Combine(feImg, "prod-linen-coat.jpg"),
                AfterLocalPaths: new[]
                {
                    Path.Combine(feImg, "suva-flatlay.jpg"),
                    Path.Combine(feImg, "suva-front.jpg"),
                    Path.Combine(feImg, "prod-side-profile.jpg"),
                    Path.Combine(feImg, "suva-hood-detail.png"),
                },
                DaysAgoStart: 11,
                SaleStage: "Delivered"
            ),
            new(
                Key: "p09",
                Title: "Quần cargo túi hộp tái chế màu xanh rêu",
                SourceTitle: "Quần kaki bảo hộ cũ sờn gấu màu be cát",
                CategorySlug: "quan-dai",
                SourceBrandSlug: "carhartt",
                Size: "M",
                Color: "Xanh lá dịu",
                Condition: ProductCondition.Excellent,
                SellingPrice: 490000m,
                AcquisitionCost: 160000m,
                MaterialCost: 52000m,
                LaborCost: 93000m,
                Description: "Quần cargo ống suông túi hộp tông xanh rêu ô liu được nhuộm thực vật và may lại từ quần kaki bảo hộ cũ. Phom ống đứng tôn chân, hai túi hộp bên đùi giữ trọn tinh thần workwear bền bỉ.",
                Concept: "Cắt bỏ phần gấu quần cũ bị tưa, nhuộm lại tông xanh ô liu tự nhiên, tái tạo cặp túi hộp 3D cân xứng hai bên hông và gia cố đường chỉ đôi.",
                BeforeLocalPath: Path.Combine(brainDir, "workwear_vest_before_cargo_trousers_1795336556490.jpg"),
                AfterLocalPaths: new[]
                {
                    Path.Combine(scratchImg, "p09_after_1.jpg"),
                    Path.Combine(scratchImg, "p09_after_2.jpg"),
                    Path.Combine(scratchImg, "p09_after_3.jpg"),
                    Path.Combine(scratchImg, "p09_after_4.jpg"),
                },
                DaysAgoStart: 10,
                SaleStage: "Delivered"
            ),
            new(
                Key: "p10",
                Title: "Ba lô cuộn miệng chống thấm tái chế từ vải dù",
                SourceTitle: "Áo khoác bomber vải dù cũ đã qua sử dụng",
                CategorySlug: "tui-xach-phu-kien",
                SourceBrandSlug: "nike",
                Size: "Một cỡ",
                Color: "Xanh chàm",
                Condition: ProductCondition.LikeNew,
                SellingPrice: 450000m,
                AcquisitionCost: 140000m,
                MaterialCost: 55000m,
                LaborCost: 85000m,
                Description: "Ba lô rolltop chống thấm phong cách đô thị màu xanh navy đậm, tái chế từ vải dù áo khoác cũ kết hợp đệm lưng lưới thoáng khí. Nắp cuộn cài móc kim loại chắc chắn, có ngăn chống sốc laptop 15 inch.",
                Concept: "Thu hồi lớp vải dù mật độ cao từ áo khoác ngoài, ép lớp chống thấm mặt trong, ráp thân ba lô dáng đứng cùng dây đai trợ lực và khóa móc chuyên dụng.",
                BeforeLocalPath: Path.Combine(feImg, "rec-real-1.jpg"),
                AfterLocalPaths: new[]
                {
                    Path.Combine(scratchImg, "p10_after_1.jpg"),
                    Path.Combine(scratchImg, "p10_after_2.jpg"),
                    Path.Combine(scratchImg, "p10_after_3.jpg"),
                    Path.Combine(scratchImg, "p10_after_4.jpg"),
                },
                DaysAgoStart: 9,
                SaleStage: "InTransit"
            ),
            new(
                Key: "p11",
                Title: "Áo gile phao tái chế dáng hộp màu đen",
                SourceTitle: "Áo khoác phao dáng dài cũ bị hư tay áo",
                CategorySlug: "ao-gile-vest",
                SourceBrandSlug: "nike",
                Size: "L",
                Color: "Đen",
                Condition: ProductCondition.Excellent,
                SellingPrice: 520000m,
                AcquisitionCost: 175000m,
                MaterialCost: 50000m,
                LaborCost: 95000m,
                Description: "Áo gile phao trần ngang tối giản màu đen tuyền, tái chế từ thân áo khoác phao dài bị rách tay. Cổ đứng giữ ấm, khóa kéo hai chiều linh hoạt, dễ mặc ngoài áo thun dài tay hoặc hoodie.",
                Concept: "Tháo bỏ phần tay và thân dưới hư hỏng của áo phao dài, bo viền nách co giãn ôm gọn, chần lại các khoang bông đều đặn và thay khóa kéo hai chiều.",
                BeforeLocalPath: Path.Combine(scratchImg, "p11_before_1.jpg"),
                AfterLocalPaths: new[]
                {
                    Path.Combine(scratchImg, "p11_after_1.jpg"),
                    Path.Combine(scratchImg, "p11_after_2.jpg"),
                    Path.Combine(scratchImg, "p11_after_3.jpg"),
                    Path.Combine(scratchImg, "p11_after_4.jpg"),
                },
                DaysAgoStart: 8,
                SaleStage: "PendingShip"
            ),
            new(
                Key: "p12",
                Title: "Áo sơ mi flannel caro tái chế phối túi ngực",
                SourceTitle: "Áo cotton dài tay sọc cũ dáng rộng",
                CategorySlug: "ao-so-mi",
                SourceBrandSlug: "uniqlo",
                Size: "L",
                Color: "Đỏ đô phối than",
                Condition: ProductCondition.Excellent,
                SellingPrice: 420000m,
                AcquisitionCost: 135000m,
                MaterialCost: 35000m,
                LaborCost: 75000m,
                Description: "Áo sơ mi flannel kẻ caro đỏ trầm phối xám than dệt dày ấm, được phục hồi và dựng lại phom cổ áo, túi ngực có khuy cài chắc chắn. Phù hợp mặc đơn hoặc khoác ngoài áo thun.",
                Concept: "Tuyển chọn phần vải dạ cotton kẻ caro bền màu, căn chỉnh lại đối xứng đường kẻ thân trước, gia cố trụ cổ, túi ngực và thay bộ cúc nhám màu khói.",
                BeforeLocalPath: Path.Combine(scratchImg, "p12_before_1.jpg"),
                AfterLocalPaths: new[]
                {
                    Path.Combine(scratchImg, "p12_after_1.jpg"),
                    Path.Combine(scratchImg, "p12_after_2.jpg"),
                    Path.Combine(scratchImg, "p12_after_3.jpg"),
                    Path.Combine(scratchImg, "p12_after_4.jpg"),
                },
                DaysAgoStart: 7,
                SaleStage: "ConfirmedQueue"
            ),
        };

        // 6. Upload ALL images to Cloudinary in parallel (max 6 concurrent)
        Console.WriteLine("Đang tải toàn bộ 62 ảnh (12 ảnh trước tái chế + 48 ảnh 4 góc sau tái chế + 2 ảnh thu mua chờ xử lý) lên Cloudinary...");
        var uploadItems = new List<(string Key, string LocalPath)>();
        foreach (var s in specs)
        {
            uploadItems.Add(($"{s.Key}-before", s.BeforeLocalPath));
            for (int a = 0; a < s.AfterLocalPaths.Length; a++)
                uploadItems.Add(($"{s.Key}-after-{a + 1}", s.AfterLocalPaths[a]));
        }
        // 2 extra active sourcing requests images
        uploadItems.Add(("s13-pending", Path.Combine(feImg, "prod-patchwork-denim.jpg")));
        uploadItems.Add(("s14-received", Path.Combine(brainDir, "upcycled_skirt_02_after_front_1795336570230.jpg")));

        foreach (var item in uploadItems)
        {
            if (!File.Exists(item.LocalPath))
                throw new FileNotFoundException($"Thiếu file ảnh nguồn: {item.LocalPath}");
        }

        var uploadedUrls = new System.Collections.Concurrent.ConcurrentDictionary<string, (string Url, string PublicId)>();
        using var sem = new SemaphoreSlim(6);
        await Task.WhenAll(uploadItems.Select(async item =>
        {
            await sem.WaitAsync();
            try
            {
                var publicId = $"rewear/seed-2026/{item.Key}";
                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(item.LocalPath),
                    PublicId = publicId,
                    Overwrite = true,
                    UniqueFilename = false,
                    UseFilename = false
                };
                var res = await cloudinary.UploadAsync(uploadParams);
                if (res.Error != null || res.SecureUrl == null)
                    throw new InvalidOperationException($"Upload Cloudinary thất bại ({item.Key}): {res.Error?.Message}");
                uploadedUrls[item.Key] = (res.SecureUrl.ToString(), res.PublicId);
            }
            finally { sem.Release(); }
        }));
        Console.WriteLine($"Đã upload thành công {uploadedUrls.Count} ảnh lên Cloudinary.");

        // 7. Insert Brands
        var brandList = new List<Brand>
        {
            new() { Name = "REWEAR Studio", Slug = "rewear-studio", Description = "Thương hiệu thiết kế & tái chế độc bản của REWEAR", IsActive = true, CreatedAt = now.AddDays(-25), UpdatedAt = now.AddDays(-25) },
            new() { Name = "Levi's", Slug = "levis", Description = "Thời trang denim cổ điển bền bỉ", IsActive = true, CreatedAt = now.AddDays(-25), UpdatedAt = now.AddDays(-25) },
            new() { Name = "Uniqlo", Slug = "uniqlo", Description = "Thời trang tối giản ứng dụng hằng ngày", IsActive = true, CreatedAt = now.AddDays(-25), UpdatedAt = now.AddDays(-25) },
            new() { Name = "Zara", Slug = "zara", Description = "Thời trang đương đại phong cách châu Âu", IsActive = true, CreatedAt = now.AddDays(-25), UpdatedAt = now.AddDays(-25) },
            new() { Name = "Carhartt", Slug = "carhartt", Description = "Workwear chất liệu kaki và canvas siêu bền", IsActive = true, CreatedAt = now.AddDays(-25), UpdatedAt = now.AddDays(-25) },
            new() { Name = "Nike", Slug = "nike", Description = "Thời trang thể thao và streetwear kỹ thuật", IsActive = true, CreatedAt = now.AddDays(-25), UpdatedAt = now.AddDays(-25) },
        };
        await db.GetCollection<Brand>("Brands").InsertManyAsync(brandList);
        var brandBySlug = brandList.ToDictionary(b => b.Slug);
        var rewearBrand = brandBySlug["rewear-studio"];

        // 8. Insert Categories
        var categoryList = new List<Category>
        {
            new() { Name = "Túi xách & Phụ kiện", Slug = "tui-xach-phu-kien", Description = "Túi tote, ba lô và phụ kiện tái chế độc bản", ImageUrl = uploadedUrls["p01-after-1"].Url, IsActive = true, CreatedAt = now.AddDays(-25), UpdatedAt = now.AddDays(-25) },
            new() { Name = "Áo sơ mi", Slug = "ao-so-mi", Description = "Áo sơ mi ghép vải, flannel và tinh chỉnh phom", ImageUrl = uploadedUrls["p02-after-1"].Url, IsActive = true, CreatedAt = now.AddDays(-25), UpdatedAt = now.AddDays(-25) },
            new() { Name = "Áo khoác", Slug = "ao-khoac", Description = "Áo khoác phao, áo gió và áo khoác kỹ thuật tái chế", ImageUrl = uploadedUrls["p07-after-1"].Url, IsActive = true, CreatedAt = now.AddDays(-25), UpdatedAt = now.AddDays(-25) },
            new() { Name = "Quần dài", Slug = "quan-dai", Description = "Quần jean phục hồi và quần cargo tái chế", ImageUrl = uploadedUrls["p04-after-1"].Url, IsActive = true, CreatedAt = now.AddDays(-25), UpdatedAt = now.AddDays(-25) },
            new() { Name = "Chân váy", Slug = "chan-vay", Description = "Chân váy chữ A và midi tái cấu trúc từ vải cũ", ImageUrl = uploadedUrls["p06-after-1"].Url, IsActive = true, CreatedAt = now.AddDays(-25), UpdatedAt = now.AddDays(-25) },
            new() { Name = "Áo len & Cardigan", Slug = "ao-len-cardigan", Description = "Cardigan và đồ dệt kim phục hồi thủ công", ImageUrl = uploadedUrls["p05-after-1"].Url, IsActive = true, CreatedAt = now.AddDays(-25), UpdatedAt = now.AddDays(-25) },
            new() { Name = "Áo gile & Vest", Slug = "ao-gile-vest", Description = "Áo gile dáng hộp và vest tái chế", ImageUrl = uploadedUrls["p11-after-1"].Url, IsActive = true, CreatedAt = now.AddDays(-25), UpdatedAt = now.AddDays(-25) },
        };
        await db.GetCollection<Category>("Categories").InsertManyAsync(categoryList);
        var catBySlug = categoryList.ToDictionary(c => c.Slug);

        // 9. Insert Addresses for key buyers
        var buyers = new[]
        {
            (User: userLam, Phone: "0365373441", Province: "TP. Hồ Chí Minh", District: "TP. Thủ Đức", Ward: "Phường Tăng Nhơn Phú A", Street: "124 Đường 160"),
            (User: userTuan, Phone: "0912485620", Province: "TP. Hồ Chí Minh", District: "Quận Bình Thạnh", Ward: "Phường 25", Street: "68 Đường D2 (Nguyễn Gia Trí)"),
            (User: FindMember("nguyenducha@gmail.com", 3), Phone: "0903112233", Province: "TP. Hồ Chí Minh", District: "Quận 3", Ward: "Phường Võ Thị Sáu", Street: "215 Nam Kỳ Khởi Nghĩa"),
            (User: FindMember("tranhaitrang@gmail.com", 4), Phone: "0908445566", Province: "TP. Hồ Chí Minh", District: "Quận 1", Ward: "Phường Bến Nghé", Street: "45 Lê Lợi"),
            (User: FindMember("ledinhphong@gmail.com", 5), Phone: "0977889900", Province: "TP. Hồ Chí Minh", District: "Quận 7", Ward: "Phường Tân Phong", Street: "18 Nguyễn Hữu Thọ"),
            (User: FindMember("phamvietloan@gmail.com", 6), Phone: "0934567891", Province: "Hà Nội", District: "Quận Cầu Giấy", Ward: "Phường Dịch Vọng", Street: "92 Trần Thái Tông"),
            (User: userTest, Phone: "0988123456", Province: "TP. Hồ Chí Minh", District: "Quận Phú Nhuận", Ward: "Phường 9", Street: "55 Phan Xích Long"),
        };

        var addresses = buyers.Select(b => new Address
        {
            UserId = b.User.Id,
            RecipientName = b.User.FullName,
            PhoneNumber = b.Phone,
            Province = b.Province,
            District = b.District,
            Ward = b.Ward,
            StreetAddress = b.Street,
            Note = "Gọi điện trước khi giao 15 phút",
            IsDefault = true,
            CreatedAt = now.AddDays(-20),
            UpdatedAt = now.AddDays(-20)
        }).ToList();
        await db.GetCollection<Address>("Addresses").InsertManyAsync(addresses);
        var addrByUserId = addresses.ToDictionary(a => a.UserId);

        // 10. Insert Vouchers
        var voucher50k = new Voucher
        {
            Code = "REWEAR50K",
            Name = "Ưu đãi Tuần hoàn — Giảm 50.000₫ đơn từ 400.000₫",
            Description = "Áp dụng cho toàn bộ sản phẩm tái chế độc bản REWEAR.",
            Type = VoucherType.FixedAmount,
            Value = 50000m,
            MinOrderAmount = 400000m,
            MinDiscount = 0m,
            MaxDiscount = 0m,
            StartDate = now.AddDays(-20),
            ExpiryDate = now.AddDays(60),
            UsageLimit = 100,
            UsedCount = 1,
            ReservedCount = 0,
            UsageLimitPerUser = 1,
            IsActive = true,
            CreatedBy = admin.Id,
            CreatedAt = now.AddDays(-20),
            UpdatedAt = now.AddDays(-5)
        };
        var voucher10Pct = new Voucher
        {
            Code = "REWEAR10",
            Name = "Thành viên xanh — Giảm 10% tối đa 100.000₫",
            Description = "Giảm 10% cho đơn từ 300.000₫, tối đa 100.000₫.",
            Type = VoucherType.Percentage,
            Value = 10m,
            MinOrderAmount = 300000m,
            MinDiscount = 20000m,
            MaxDiscount = 100000m,
            StartDate = now.AddDays(-15),
            ExpiryDate = now.AddDays(45),
            UsageLimit = 200,
            UsedCount = 0,
            ReservedCount = 0,
            UsageLimitPerUser = 1,
            IsActive = true,
            CreatedBy = admin.Id,
            CreatedAt = now.AddDays(-15),
            UpdatedAt = now.AddDays(-15)
        };
        var voucher30k = new Voucher
        {
            Code = "GREEN30K",
            Name = "Tri ân tái chế — Giảm 30.000₫ đơn từ 250.000₫",
            Description = "Dành tặng khách hàng ủng hộ thời trang tuần hoàn.",
            Type = VoucherType.FixedAmount,
            Value = 30000m,
            MinOrderAmount = 250000m,
            MinDiscount = 0m,
            MaxDiscount = 0m,
            StartDate = now.AddDays(-10),
            ExpiryDate = now.AddDays(30),
            UsageLimit = 150,
            UsedCount = 0,
            ReservedCount = 0,
            UsageLimitPerUser = 1,
            IsActive = true,
            CreatedBy = admin.Id,
            CreatedAt = now.AddDays(-10),
            UpdatedAt = now.AddDays(-10)
        };
        await db.GetCollection<Voucher>("Vouchers").InsertManyAsync(new[] { voucher50k, voucher10Pct, voucher30k });

        // 11. Build all 12 full circular fashion flows
        var sourcingDocs = new List<SourcingRequest>();
        var payoutDocs = new List<PurchasePayout>();
        var inventoryDocs = new List<InventoryItem>();
        var movementDocs = new List<InventoryMovement>();
        var projectDocs = new List<UpcyclingProject>();
        var qcDocs = new List<QualityInspection>();
        var productDocs = new List<Product>();
        var orderDocs = new List<Order>();
        var historyDocs = new List<OrderStatusHistory>();
        var paymentDocs = new List<Payment>();
        var shippingDocs = new List<Shipping>();
        var reservationDocs = new List<StockReservation>();
        var userVoucherDocs = new List<UserVoucher>();

        int soldIndex = 0;

        for (int i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            int num = i + 1;
            var seller = members[(i + 10) % members.Count]; // distinct member sellers
            var cat = catBySlug[spec.CategorySlug];
            var srcBrand = brandBySlug[spec.SourceBrandSlug];

            var (beforeUrl, beforePublicId) = uploadedUrls[$"{spec.Key}-before"];
            var afterUrls = Enumerable.Range(1, 4).Select(a => uploadedUrls[$"{spec.Key}-after-{a}"].Url).ToList();

            // Strictly chronological timestamps within last 18 days
            var tSource = now.AddDays(-spec.DaysAgoStart).AddHours(-8);
            var tOffer = tSource.AddHours(3);
            var tAccept = tOffer.AddHours(2);
            var tReceive = tAccept.AddHours(6);
            var tInspect = tReceive.AddHours(4);
            var tPayout = tInspect.AddHours(3);
            var tProjCreate = tPayout.AddHours(4);
            var tProjStart = tProjCreate.AddHours(3);
            var tCostMat = tProjStart.AddHours(5);
            var tCostLab = tCostMat.AddHours(8);
            var tProjQCReq = tCostLab.AddHours(6);
            var tQC = tProjQCReq.AddHours(4);
            var tPublish = tQC.AddHours(3);

            string sourcingId = ObjectId.GenerateNewId().ToString();
            string projectId = ObjectId.GenerateNewId().ToString();
            string productId = ObjectId.GenerateNewId().ToString();

            bool isSold = spec.SaleStage != "Available";

            // A) SourcingRequest
            sourcingDocs.Add(new SourcingRequest
            {
                Id = sourcingId,
                UserId = seller.Id,
                Title = spec.SourceTitle,
                Description = $"Đồ cá nhân đã qua sử dụng, chất liệu còn chắc chắn, gửi thu mua cho xưởng tái chế REWEAR.",
                BrandId = srcBrand.Id,
                CategoryId = cat.Id,
                Size = spec.Size,
                Color = spec.Color,
                DeclaredCondition = ProductCondition.Good,
                InspectedCondition = ProductCondition.Good,
                CustomerExpectedPrice = spec.AcquisitionCost + 15000m,
                OfferedPrice = spec.AcquisitionCost,
                AcceptedOfferPrice = spec.AcquisitionCost,
                OfferVersion = 1,
                RequiresRevisedAcceptance = false,
                OfferHistory = new List<SourcingOffer>
                {
                    new()
                    {
                        Version = 1,
                        Amount = spec.AcquisitionCost,
                        ActorId = staff.Id,
                        At = tOffer,
                        AcceptedAt = tAccept
                    }
                },
                InspectedBy = staff.Id,
                InspectedAt = tInspect,
                ImageUrls = new List<string> { beforeUrl },
                ImagePublicIds = new List<string> { beforePublicId },
                InspectionImages = new List<string> { beforeUrl },
                Status = SourcingStatus.Completed,
                AdminNote = "Đã kiểm định chất liệu đạt chuẩn tái chế REWEAR, không mục vải, đủ điều kiện thu mua.",
                ProductId = productId,
                CreatedAt = tSource,
                UpdatedAt = tPublish,
                CompletedAt = tPublish
            });

            // B) PurchasePayout (Confirmed!)
            payoutDocs.Add(new PurchasePayout
            {
                Id = sourcingId,
                SourcingId = sourcingId,
                CustomerId = seller.Id,
                Amount = spec.AcquisitionCost,
                Method = num % 3 == 0 ? "Cash" : "ManualTransfer",
                State = "Confirmed",
                Reference = $"PAYOUT-RW26-{num:D3}",
                ConfirmedBy = admin.Id,
                PaidAt = tPayout,
                Note = $"Chi trả thu mua nguồn hàng #{num:D2} cho {seller.FullName}",
                CreatedAt = tPayout,
                UpdatedAt = tPayout
            });

            // C) Raw InventoryItem (Consumed into upcycled product)
            inventoryDocs.Add(new InventoryItem
            {
                Id = sourcingId,
                SourcingId = sourcingId,
                Title = spec.SourceTitle,
                Custody = "Transformed",
                State = "Consumed",
                AcquisitionCost = spec.AcquisitionCost,
                Location = "Đã chuyển thành thành phẩm",
                Images = new List<string> { beforeUrl },
                ProjectId = projectId,
                ProductId = productId,
                Version = 7,
                CreatedAt = tReceive,
                UpdatedAt = tPublish
            });

            // D) Raw InventoryMovements (7 chronological steps)
            void AddMove(string invId, string? fromSt, string toSt, string actorId, string reason, string relId, DateTime at, string? fromC = null, string? toC = null)
            {
                movementDocs.Add(new InventoryMovement
                {
                    InventoryId = invId,
                    FromState = fromSt,
                    ToState = toSt,
                    FromCustody = fromC,
                    ToCustody = toC,
                    ActorId = actorId,
                    Reason = reason,
                    RelatedId = relId,
                    CreatedAt = at,
                    UpdatedAt = at
                });
            }

            AddMove(sourcingId, null, "AwaitingInspection", staff.Id, "Nhận món đồ từ người gửi tại kho REWEAR.", sourcingId, tReceive, null, "Warehouse");
            AddMove(sourcingId, "AwaitingInspection", "AwaitingPayout", staff.Id, "Kiểm định nguồn hàng đạt chuẩn tái chế.", sourcingId, tInspect);
            AddMove(sourcingId, "AwaitingPayout", "AvailableRaw", admin.Id, "Đã xác nhận chứng từ chi trả; nguyên liệu sẵn sàng.", sourcingId, tPayout);
            AddMove(sourcingId, "AvailableRaw", "ReservedForUpcycling", admin.Id, "Cấp nguyên liệu cho dự án tái chế.", projectId, tProjCreate);
            AddMove(sourcingId, "ReservedForUpcycling", "UpcyclingInProgress", staff.Id, "Xưởng bắt đầu tháo rã và tái thiết kế.", projectId, tProjStart);
            AddMove(sourcingId, "UpcyclingInProgress", "FinishedAwaitingQC", staff.Id, "Hoàn thiện thành phẩm 4 góc chụp, gửi kiểm định QC.", projectId, tProjQCReq);
            AddMove(sourcingId, "FinishedAwaitingQC", "Consumed", admin.Id, "Nguyên liệu đã chuyển thành sản phẩm độc bản đạt QC.", projectId, tPublish, "Warehouse", "Transformed");

            // E) UpcyclingProject (with BeforeImages, 4 AfterImages, Material & Labor Costs, Timeline)
            projectDocs.Add(new UpcyclingProject
            {
                Id = projectId,
                Title = $"Tái chế: {spec.Title}",
                Concept = spec.Concept,
                ResponsibleStaffId = staff.Id,
                CreatedBy = admin.Id,
                PlannedCompletion = tPublish.AddDays(1),
                InventoryIds = new List<string> { sourcingId },
                BeforeImages = new List<string> { beforeUrl },
                AfterImages = afterUrls,
                State = "Completed",
                AcquisitionCost = spec.AcquisitionCost,
                Costs = new List<ProductionCost>
                {
                    new()
                    {
                        IdempotencyKey = $"cost-{spec.Key}-mat",
                        Kind = "Material",
                        Amount = spec.MaterialCost,
                        Note = "Phụ liệu may mặc (chỉ gia cố, cúc/khóa, vải lót, xử lý giặt hấp)",
                        ActorId = staff.Id,
                        RecordedAt = tCostMat
                    },
                    new()
                    {
                        IdempotencyKey = $"cost-{spec.Key}-lab",
                        Kind = "Labor",
                        Amount = spec.LaborCost,
                        Note = "Công thợ rập, cắt may tái cấu trúc và hoàn thiện thủ công",
                        ActorId = staff.Id,
                        RecordedAt = tCostLab
                    }
                },
                Timeline = new List<LifecycleEvent>
                {
                    new() { From = null, To = "Pending", ActorId = admin.Id, Note = "Tạo dự án và giữ nguyên liệu.", At = tProjCreate },
                    new() { From = "Pending", To = "InProgress", ActorId = staff.Id, Note = "Bắt đầu thiết kế, cắt rập và may tái chế.", At = tProjStart },
                    new() { From = "InProgress", To = "QualityCheck", ActorId = staff.Id, Note = "Hoàn thành thành phẩm, chụp đủ 4 góc và chuyển QC.", At = tProjQCReq },
                    new() { From = "QualityCheck", To = "QCApproved", ActorId = admin.Id, Note = "Kiểm định QC đạt chuẩn đường may, phom dáng và vệ sinh.", At = tQC },
                    new() { From = "QCApproved", To = "Completed", ActorId = admin.Id, Note = "Đã đăng bán thành phẩm độc bản trên REWEAR.", At = tPublish }
                },
                ProductId = productId,
                Version = 4,
                CreatedAt = tProjCreate,
                UpdatedAt = tPublish
            });

            // F) QualityInspection (Passed = true)
            qcDocs.Add(new QualityInspection
            {
                ProjectId = projectId,
                InspectorId = admin.Id,
                Passed = true,
                Note = "Đạt chuẩn QC REWEAR: đường may gia cố chắc chắn, phom dáng cân đối, đã hấp tiệt trùng và duyệt đủ 4 góc chụp thành phẩm.",
                Images = new List<string> { afterUrls[0], afterUrls[3] },
                CreatedAt = tQC,
                UpdatedAt = tQC
            });

            // G) Product (Upcycled, 4 angle ImageUrls, full cost basis!)
            decimal prodCost = spec.MaterialCost + spec.LaborCost;
            var product = new Product
            {
                Id = productId,
                Title = spec.Title,
                Slug = "rewear-" + productId,
                Description = spec.Description,
                BrandId = rewearBrand.Id,
                CategoryId = cat.Id,
                Price = spec.SellingPrice,
                StockQuantity = isSold ? 0 : 1,
                Condition = spec.Condition,
                Size = spec.Size,
                Color = spec.Color,
                ImageUrls = afterUrls,
                Status = isSold ? ProductStatus.Sold : ProductStatus.Available,
                IsActive = true,
                IsUnique = true,
                Version = isSold ? 2 : 1,
                ProductType = "Upcycled",
                SourceSourcingId = sourcingId,
                UpcyclingProjectId = projectId,
                SourceInventoryIds = new List<string> { sourcingId },
                AcquisitionCost = spec.AcquisitionCost,
                ProductionCost = prodCost,
                HasCostBasis = true,
                CreatedAt = tPublish,
                UpdatedAt = tPublish
            };
            productDocs.Add(product);

            // H) Finished retail InventoryItem
            string retailCustody = spec.SaleStage switch
            {
                "Delivered" => "Buyer",
                "InTransit" => "Shipper",
                _ => "Warehouse"
            };
            string retailLocation = spec.SaleStage switch
            {
                "Delivered" => "Đã giao khách hàng",
                "InTransit" => "Đang giao bởi Shipper",
                _ => "Kho REWEAR"
            };

            var retailInv = new InventoryItem
            {
                Id = productId,
                SourcingId = "project:" + projectId,
                Title = product.Title,
                State = "ReadyRetail",
                Custody = retailCustody,
                Location = retailLocation,
                ProjectId = projectId,
                ProductId = productId,
                AcquisitionCost = spec.AcquisitionCost,
                Images = afterUrls,
                CreatedAt = tPublish,
                UpdatedAt = tPublish
            };
            inventoryDocs.Add(retailInv);
            AddMove(productId, null, "ReadyRetail", admin.Id, "Thành phẩm QC đạt được nhập kho bán lẻ.", projectId, tPublish, null, "Warehouse");

            // I) If Sold -> Create complete Order + Payment + StockReservation + OrderStatusHistory + Shipping!
            if (isSold)
            {
                var buyerInfo = buyers[soldIndex];
                var buyerUser = buyerInfo.User;
                var buyerAddr = addrByUserId[buyerUser.Id];
                soldIndex++;

                // Spread the 6 paid orders cleanly across the last 6 days (so both 7-day and 30-day dashboards shine!)
                var tOrder = now.AddDays(-(6 - soldIndex)).AddHours(-12);
                if (tOrder <= tPublish) tOrder = tPublish.AddHours(2);
                var tPaid = tOrder.AddMinutes(4);

                decimal discount = 0m;
                string? usedVoucherId = null;
                string? usedVoucherCode = null;
                if (soldIndex == 1) // Apply REWEAR50K on Order #1 (P07)
                {
                    discount = 50000m;
                    usedVoucherId = voucher50k.Id;
                    usedVoucherCode = voucher50k.Code;
                }

                decimal totalAmount = spec.SellingPrice - discount;
                string orderId = ObjectId.GenerateNewId().ToString();
                string orderCode = $"RW-{tOrder:yyyyMMdd}-{spec.Key.ToUpperInvariant()}0{soldIndex}";

                OrderStatus finalOrderStatus = spec.SaleStage switch
                {
                    "Delivered" => OrderStatus.Delivered,
                    "InTransit" => OrderStatus.Shipping,
                    _ => OrderStatus.Confirmed
                };

                string? trackingNum = spec.SaleStage is "Delivered" or "InTransit"
                    ? $"GHN-RW2611{soldIndex:D2}"
                    : null;

                var tAssign = tPaid.AddHours(2);
                var tPickup = tAssign.AddHours(3);
                var tTransit = tPickup.AddHours(1);
                var tDelivered = tTransit.AddHours(16);

                var orderUpdated = spec.SaleStage switch
                {
                    "Delivered" => tDelivered,
                    "InTransit" => tTransit,
                    "PendingShip" => tAssign,
                    _ => tPaid
                };

                product.UpdatedAt = tPaid;
                retailInv.UpdatedAt = orderUpdated;

                var order = new Order
                {
                    Id = orderId,
                    OrderCode = orderCode,
                    UserId = buyerUser.Id,
                    CheckoutKey = $"chk-{spec.Key}-{soldIndex:D2}",
                    PaidAt = tPaid,
                    ReservationExpiresAt = tOrder.AddMinutes(15),
                    ShippingMethod = soldIndex % 2 == 0 ? ShippingMethod.Express : ShippingMethod.Standard,
                    Items = new List<OrderItem>
                    {
                        new()
                        {
                            Id = ObjectId.GenerateNewId().ToString(),
                            ProductId = productId,
                            Quantity = 1,
                            UnitPrice = spec.SellingPrice,
                            ProductTitle = spec.Title,
                            ProductSlug = product.Slug,
                            ProductImageUrl = afterUrls[0],
                            Size = spec.Size,
                            Color = spec.Color,
                            Condition = spec.Condition.ToString(),
                            AcquisitionCost = spec.AcquisitionCost,
                            ProductionCost = prodCost,
                            HasCostBasis = true,
                            UpcyclingProjectId = projectId
                        }
                    },
                    ShippingRecipientName = buyerAddr.RecipientName,
                    ShippingPhoneNumber = buyerAddr.PhoneNumber,
                    ShippingProvince = buyerAddr.Province,
                    ShippingDistrict = buyerAddr.District,
                    ShippingWard = buyerAddr.Ward,
                    ShippingStreetAddress = buyerAddr.StreetAddress,
                    ShippingNote = buyerAddr.Note,
                    AddressId = buyerAddr.Id,
                    PaymentMethod = PaymentMethod.PayOs,
                    SubTotal = spec.SellingPrice,
                    DiscountAmount = discount,
                    VoucherId = usedVoucherId,
                    VoucherCode = usedVoucherCode,
                    ShippingFee = 0m,
                    TotalAmount = totalAmount,
                    Status = finalOrderStatus,
                    TrackingNumber = trackingNum,
                    EstimatedDeliveryDate = tPaid.AddDays(3),
                    CreatedAt = tOrder,
                    UpdatedAt = orderUpdated
                };
                orderDocs.Add(order);

                if (usedVoucherId != null)
                {
                    userVoucherDocs.Add(new UserVoucher
                    {
                        UserId = buyerUser.Id,
                        VoucherId = usedVoucherId,
                        Status = UserVoucherStatus.Used,
                        UsageCount = 1,
                        OrderId = orderId,
                        ReservedAt = tOrder,
                        UsedAt = tPaid,
                        GrantedAt = now.AddDays(-18),
                        GrantedBy = admin.Id
                    });
                }

                // StockReservation (Paid)
                reservationDocs.Add(new StockReservation
                {
                    Id = $"{orderId}:{productId}",
                    OrderId = orderId,
                    ProductId = productId,
                    Quantity = 1,
                    State = "Paid",
                    ExpiresAt = tOrder.AddMinutes(15),
                    UpdatedAt = tPaid
                });

                // Payment (Paid via PayOS with Receipts & CashEvents!)
                string txId = $"PAYOS-TX-RW26-{soldIndex:D2}";
                string linkId = $"payos-link-rw26-{soldIndex:D2}";
                paymentDocs.Add(new Payment
                {
                    OrderId = orderId,
                    OrderCode = orderCode,
                    UserId = buyerUser.Id,
                    Method = PaymentMethod.PayOs,
                    Amount = totalAmount,
                    ReceivedAmount = totalAmount,
                    Status = PaymentStatus.Paid,
                    GatewayOrderCode = 2026112200L + soldIndex,
                    SessionCreating = false,
                    RequiresReconciliation = false,
                    PaymentLinkId = linkId,
                    PayOsTransactionId = txId,
                    CheckoutUrl = $"https://pay.payos.vn/web/{linkId}",
                    ExpiresAt = tOrder.AddMinutes(15),
                    PaidAt = tPaid,
                    LastGatewayCheckedAt = tPaid,
                    Receipts = new List<GatewayReceipt>
                    {
                        new() { Reference = txId, Amount = totalAmount, RecordedAt = tPaid }
                    },
                    CashEvents = new List<PaymentCashEvent>
                    {
                        new() { Amount = totalAmount, RecordedAt = tPaid, LegacyTimestamp = false }
                    },
                    Note = "Thanh toán PayOS thành công, đã đối soát tự động.",
                    CreatedAt = tOrder,
                    UpdatedAt = tPaid
                });

                // OrderStatusHistory: OrderPlaced + Confirmed
                historyDocs.Add(new OrderStatusHistory
                {
                    OrderId = orderId,
                    FromStatus = null,
                    ToStatus = OrderStatus.AwaitingPayment,
                    EventType = "OrderPlaced",
                    ChangedBy = OrderStatusChangedBy.Customer,
                    ChangedByUserId = buyerUser.Id,
                    ChangedByName = buyerUser.FullName,
                    Note = "Khách hàng tạo đơn hàng mới.",
                    CreatedAt = tOrder
                });
                historyDocs.Add(new OrderStatusHistory
                {
                    OrderId = orderId,
                    FromStatus = OrderStatus.AwaitingPayment,
                    ToStatus = OrderStatus.Confirmed,
                    EventType = "Confirmed",
                    ChangedBy = OrderStatusChangedBy.System,
                    ChangedByName = "PayOS",
                    Note = $"Đã nhận thanh toán {totalAmount:N0}₫ qua PayOS ({txId}).",
                    CreatedAt = tPaid
                });

                // Shipping (for all sold orders EXCEPT ConfirmedQueue!)
                if (spec.SaleStage != "ConfirmedQueue")
                {
                    string shipId = ObjectId.GenerateNewId().ToString();
                    ShippingStatus shipStatus = spec.SaleStage switch
                    {
                        "Delivered" => ShippingStatus.Delivered,
                        "InTransit" => ShippingStatus.InTransit,
                        _ => ShippingStatus.Pending
                    };

                    var shipping = new Shipping
                    {
                        Id = shipId,
                        OrderId = orderId,
                        OrderCode = orderCode,
                        Method = order.ShippingMethod,
                        Fee = 0m,
                        Status = shipStatus,
                        Carrier = soldIndex % 3 == 0 ? "GHTK" : "GHN",
                        TrackingNumber = trackingNum,
                        EstimatedDeliveryDate = tPaid.AddDays(3),
                        ShipperId = shipper.Id,
                        ShipperName = shipper.FullName,
                        AssignedAt = tAssign,
                        RequiresPickupConfirmation = true,
                        PickedUpAt = spec.SaleStage is "InTransit" or "Delivered" ? tPickup : null,
                        ShippedAt = spec.SaleStage is "InTransit" or "Delivered" ? tTransit : null,
                        DeliveredAt = spec.SaleStage == "Delivered" ? tDelivered : null,
                        Note = spec.SaleStage switch
                        {
                            "Delivered" => "Đã giao tận tay người nhận, khách kiểm tra hài lòng.",
                            "InTransit" => "Shipper đang trên tuyến giao hàng nội thành.",
                            _ => "Đã đóng gói tại kho REWEAR, chờ shipper qua nhận kiện."
                        },
                        AttemptCount = 0,
                        CreatedAt = tAssign,
                        UpdatedAt = orderUpdated
                    };
                    shippingDocs.Add(shipping);

                    // Assigned history
                    historyDocs.Add(new OrderStatusHistory
                    {
                        OrderId = orderId,
                        FromStatus = OrderStatus.Confirmed,
                        ToStatus = OrderStatus.Confirmed,
                        EventType = "Assigned",
                        ShipmentId = shipId,
                        AssignedShipperId = shipper.Id,
                        ToShippingStatus = ShippingStatus.Pending.ToString(),
                        ChangedBy = OrderStatusChangedBy.Staff,
                        ChangedByUserId = staff.Id,
                        ChangedByName = staff.FullName,
                        Note = $"Phân công giao hàng cho {shipper.FullName}.",
                        CreatedAt = tAssign
                    });

                    if (spec.SaleStage is "InTransit" or "Delivered")
                    {
                        historyDocs.Add(new OrderStatusHistory
                        {
                            OrderId = orderId,
                            FromStatus = OrderStatus.Confirmed,
                            ToStatus = OrderStatus.Confirmed,
                            EventType = "PickedUp",
                            ShipmentId = shipId,
                            AssignedShipperId = shipper.Id,
                            FromShippingStatus = ShippingStatus.Pending.ToString(),
                            ToShippingStatus = ShippingStatus.PickedUp.ToString(),
                            ChangedBy = OrderStatusChangedBy.Shipper,
                            ChangedByUserId = shipper.Id,
                            ChangedByName = shipper.FullName,
                            Note = "Shipper đã lấy kiện hàng từ kho REWEAR.",
                            CreatedAt = tPickup
                        });
                        historyDocs.Add(new OrderStatusHistory
                        {
                            OrderId = orderId,
                            FromStatus = OrderStatus.Confirmed,
                            ToStatus = OrderStatus.Shipping,
                            EventType = "InTransit",
                            ShipmentId = shipId,
                            AssignedShipperId = shipper.Id,
                            FromShippingStatus = ShippingStatus.PickedUp.ToString(),
                            ToShippingStatus = ShippingStatus.InTransit.ToString(),
                            ChangedBy = OrderStatusChangedBy.Shipper,
                            ChangedByUserId = shipper.Id,
                            ChangedByName = shipper.FullName,
                            Note = $"Đang vận chuyển với mã vận đơn {trackingNum}.",
                            CreatedAt = tTransit
                        });
                        AddMove(productId, "ReadyRetail", "ReadyRetail", shipper.Id, "Bàn giao kiện hàng cho Shipper vận chuyển.", orderId, tPickup, "Warehouse", "Shipper");
                    }

                    if (spec.SaleStage == "Delivered")
                    {
                        historyDocs.Add(new OrderStatusHistory
                        {
                            OrderId = orderId,
                            FromStatus = OrderStatus.Shipping,
                            ToStatus = OrderStatus.Delivered,
                            EventType = "Delivered",
                            ShipmentId = shipId,
                            AssignedShipperId = shipper.Id,
                            FromShippingStatus = ShippingStatus.InTransit.ToString(),
                            ToShippingStatus = ShippingStatus.Delivered.ToString(),
                            ChangedBy = OrderStatusChangedBy.Shipper,
                            ChangedByUserId = shipper.Id,
                            ChangedByName = shipper.FullName,
                            Note = "Đã giao kiện hàng thành công cho người nhận.",
                            CreatedAt = tDelivered
                        });
                        AddMove(productId, "ReadyRetail", "ReadyRetail", shipper.Id, "Shipper giao hàng thành công đến khách mua.", orderId, tDelivered, "Shipper", "Buyer");
                    }
                }
            }
        }

        // 12. Add 2 extra active SourcingRequests so Admin/Staff Sourcing & Inspection queues are also populated!
        var (s13Url, s13Pub) = uploadedUrls["s13-pending"];
        sourcingDocs.Add(new SourcingRequest
        {
            UserId = userLam.Id,
            Title = "Áo khoác denim chắp vá cũ gửi thu mua",
            Description = "Áo khoác denim cũ mặc khoảng 2 năm, phom rộng, vải còn dày chắc, muốn bán lại cho xưởng tái chế REWEAR.",
            BrandId = brandBySlug["levis"].Id,
            CategoryId = catBySlug["ao-khoac"].Id,
            Size = "L",
            Color = "Xanh chàm",
            DeclaredCondition = ProductCondition.Good,
            CustomerExpectedPrice = 220000m,
            ImageUrls = new List<string> { s13Url },
            ImagePublicIds = new List<string> { s13Pub },
            Status = SourcingStatus.Pending,
            CreatedAt = now.AddHours(-14),
            UpdatedAt = now.AddHours(-14)
        });

        string s14Id = ObjectId.GenerateNewId().ToString();
        var (s14Url, s14Pub) = uploadedUrls["s14-received"];
        var tS14Create = now.AddDays(-2);
        var tS14Offer = tS14Create.AddHours(4);
        var tS14Accept = tS14Offer.AddHours(3);
        var tS14Recv = now.AddHours(-6);
        sourcingDocs.Add(new SourcingRequest
        {
            Id = s14Id,
            UserId = userTuan.Id,
            Title = "Chân váy sơ mi kẻ xanh trắng cần kiểm định kho",
            Description = "Váy dáng midi chất cotton kẻ sọc xanh phối trắng, đã gửi đến kho REWEAR để nhân viên kiểm định.",
            BrandId = brandBySlug["zara"].Id,
            CategoryId = catBySlug["chan-vay"].Id,
            Size = "M",
            Color = "Xanh sọc phối kem",
            DeclaredCondition = ProductCondition.Excellent,
            CustomerExpectedPrice = 160000m,
            OfferedPrice = 145000m,
            AcceptedOfferPrice = 145000m,
            OfferVersion = 1,
            OfferHistory = new List<SourcingOffer>
            {
                new() { Version = 1, Amount = 145000m, ActorId = staff.Id, At = tS14Offer, AcceptedAt = tS14Accept }
            },
            ImageUrls = new List<string> { s14Url },
            ImagePublicIds = new List<string> { s14Pub },
            Status = SourcingStatus.Received,
            CreatedAt = tS14Create,
            UpdatedAt = tS14Recv
        });
        inventoryDocs.Add(new InventoryItem
        {
            Id = s14Id,
            SourcingId = s14Id,
            Title = "Chân váy sơ mi kẻ xanh trắng cần kiểm định kho",
            Custody = "Warehouse",
            State = "AwaitingInspection",
            AcquisitionCost = 0m,
            Location = "Kho REWEAR",
            Images = new List<string> { s14Url },
            Version = 1,
            CreatedAt = tS14Recv,
            UpdatedAt = tS14Recv
        });
        movementDocs.Add(new InventoryMovement
        {
            InventoryId = s14Id,
            FromState = null,
            ToState = "AwaitingInspection",
            ToCustody = "Warehouse",
            ActorId = staff.Id,
            Reason = "Đã tiếp nhận món đồ từ người gửi, chờ kiểm định chất lượng.",
            RelatedId = s14Id,
            CreatedAt = tS14Recv,
            UpdatedAt = tS14Recv
        });

        // 13. Grant Available UserVouchers to key members
        foreach (var member in new[] { userLam, userTuan, userTest })
        {
            userVoucherDocs.Add(new UserVoucher
            {
                UserId = member.Id,
                VoucherId = voucher10Pct.Id,
                Status = UserVoucherStatus.Available,
                GrantedAt = now.AddDays(-5),
                GrantedBy = admin.Id
            });
            userVoucherDocs.Add(new UserVoucher
            {
                UserId = member.Id,
                VoucherId = voucher30k.Id,
                Status = UserVoucherStatus.Available,
                GrantedAt = now.AddDays(-4),
                GrantedBy = admin.Id
            });
            if (member.Id != userLam.Id) // userLam already used voucher50k on Order #1
            {
                userVoucherDocs.Add(new UserVoucher
                {
                    UserId = member.Id,
                    VoucherId = voucher50k.Id,
                    Status = UserVoucherStatus.Available,
                    GrantedAt = now.AddDays(-6),
                    GrantedBy = admin.Id
                });
            }
        }

        // 14. Seed Wishlist + StylingProfile for userLam
        var wishlistDocs = new List<WishlistItem>
        {
            new() { UserId = userLam.Id, ProductId = productDocs[0].Id, CreatedAt = now.AddDays(-2) },
            new() { UserId = userLam.Id, ProductId = productDocs[1].Id, CreatedAt = now.AddDays(-1) },
        };
        var stylingProfile = new StylingProfile
        {
            UserId = userLam.Id,
            Styles = new List<string> { FashionStyle.Minimalist.ToString(), FashionStyle.Streetwear.ToString(), FashionStyle.Vintage.ToString() },
            Sizes = new List<string> { "M", "L" },
            Colors = new List<string> { "Xanh chàm", "Kem", "Xám than" },
            FavoriteCategoryIds = new List<string> { catBySlug["ao-khoac"].Id, catBySlug["ao-so-mi"].Id, catBySlug["tui-xach-phu-kien"].Id },
            MinBudget = 250000m,
            MaxBudget = 800000m,
            CreatedAt = now.AddDays(-10),
            UpdatedAt = now.AddDays(-2)
        };

        // 15. Persist everything to MongoDB!
        await db.GetCollection<SourcingRequest>("SourcingRequests").InsertManyAsync(sourcingDocs);
        await db.GetCollection<PurchasePayout>("PurchasePayouts").InsertManyAsync(payoutDocs);
        await db.GetCollection<InventoryItem>("InventoryItems").InsertManyAsync(inventoryDocs);
        await db.GetCollection<InventoryMovement>("InventoryMovements").InsertManyAsync(movementDocs);
        await db.GetCollection<UpcyclingProject>("UpcyclingProjects").InsertManyAsync(projectDocs);
        await db.GetCollection<QualityInspection>("QualityInspections").InsertManyAsync(qcDocs);
        await db.GetCollection<Product>("Products").InsertManyAsync(productDocs);
        await db.GetCollection<Order>("Orders").InsertManyAsync(orderDocs);
        await db.GetCollection<OrderStatusHistory>("OrderStatusHistories").InsertManyAsync(historyDocs);
        await db.GetCollection<Payment>("Payments").InsertManyAsync(paymentDocs);
        await db.GetCollection<Shipping>("Shippings").InsertManyAsync(shippingDocs);
        await db.GetCollection<StockReservation>("StockReservations").InsertManyAsync(reservationDocs);
        await db.GetCollection<UserVoucher>("UserVouchers").InsertManyAsync(userVoucherDocs);
        await db.GetCollection<WishlistItem>("WishlistItems").InsertManyAsync(wishlistDocs);
        await db.GetCollection<StylingProfile>("StylingProfiles").InsertOneAsync(stylingProfile);

        // 16. Print summary & cash flow reconciliation
        decimal totalPayouts = payoutDocs.Sum(p => p.Amount);
        decimal totalMat = projectDocs.SelectMany(p => p.Costs).Where(c => c.Kind == "Material").Sum(c => c.Amount);
        decimal totalLab = projectDocs.SelectMany(p => p.Costs).Where(c => c.Kind == "Labor").Sum(c => c.Amount);
        decimal totalProdSpend = totalMat + totalLab;
        decimal receivedRev = paymentDocs.Sum(p => p.ReceivedAmount ?? 0m);
        decimal recognizedRev = orderDocs.Sum(o => o.TotalAmount);
        decimal soldAcq = orderDocs.SelectMany(o => o.Items).Sum(i => i.AcquisitionCost * i.Quantity);
        decimal soldProd = orderDocs.SelectMany(o => o.Items).Sum(i => i.ProductionCost * i.Quantity);
        decimal grossProfit = recognizedRev - soldAcq - soldProd;

        Console.WriteLine();
        Console.WriteLine("================ SEED SUMMARY ================");
        Console.WriteLine($"Brands              : {brandList.Count}");
        Console.WriteLine($"Categories          : {categoryList.Count}");
        Console.WriteLine($"Products (Upcycled) : {productDocs.Count} (Available={productDocs.Count(p => p.Status == ProductStatus.Available)}, Sold={productDocs.Count(p => p.Status == ProductStatus.Sold)})");
        Console.WriteLine($"SourcingRequests    : {sourcingDocs.Count} (Completed=12, Pending=1, Received=1)");
        Console.WriteLine($"PurchasePayouts     : {payoutDocs.Count}");
        Console.WriteLine($"InventoryItems      : {inventoryDocs.Count}");
        Console.WriteLine($"InventoryMovements  : {movementDocs.Count}");
        Console.WriteLine($"UpcyclingProjects   : {projectDocs.Count}");
        Console.WriteLine($"QualityInspections  : {qcDocs.Count}");
        Console.WriteLine($"Orders              : {orderDocs.Count} (Delivered=3, Shipping=1, Confirmed=2)");
        Console.WriteLine($"Payments (Paid)     : {paymentDocs.Count}");
        Console.WriteLine($"Shippings           : {shippingDocs.Count} (Delivered=3, InTransit=1, Pending=1; +1 in Admin Queue)");
        Console.WriteLine($"OrderStatusHistory  : {historyDocs.Count}");
        Console.WriteLine($"Vouchers / Grants   : 3 / {userVoucherDocs.Count}");
        Console.WriteLine("---------------- CASH FLOW -------------------");
        Console.WriteLine($"Tiền đã nhận (ReceivedRevenue) : {receivedRev:N0} đ");
        Console.WriteLine($"Doanh thu ghi nhận             : {recognizedRev:N0} đ");
        Console.WriteLine($"Chi trả thu mua (12 món)       : {totalPayouts:N0} đ");
        Console.WriteLine($"Chi phí tái chế (12 dự án)     : {totalProdSpend:N0} đ (Vật tư: {totalMat:N0} đ | Nhân công: {totalLab:N0} đ)");
        Console.WriteLine($"Tổng chi phát sinh trong kỳ    : {totalPayouts + totalProdSpend:N0} đ");
        Console.WriteLine($"Giá vốn hàng đã bán (6 đơn)    : {soldAcq + soldProd:N0} đ (Thu mua: {soldAcq:N0} đ | Tái chế: {soldProd:N0} đ)");
        Console.WriteLine($"Lãi gộp tham khảo              : {grossProfit:N0} đ");
        Console.WriteLine("==============================================");
    }
}
