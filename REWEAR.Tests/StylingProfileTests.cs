using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Application.Services;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
using REWEAR.Infrastructure.Repositories;

namespace REWEAR.Tests;

/// <summary>
/// Test cho Styling Profile (hồ sơ sở thích thời trang).
/// Phạm vi: hồ sơ sở thích thời trang của user, KHÔNG đụng User Profile / Address /
/// Auth (thuộc phạm vi Backend 2).
/// </summary>
public partial class CommerceTests
{
    private async Task<User> StylingUser()
    {
        var user = new User
        {
            FullName = "Styling fixture",
            Email = Guid.NewGuid() + "@example.test",
            IsVerified = true,
            Role = UserRole.Member
        };
        await Users.CreateAsync(user);
        return user;
    }

    private async Task<Category> StylingCategory(string name = "Tops")
    {
        var category = new Category { Name = name, Slug = name.ToLowerInvariant(), IsActive = true };
        await Context.Categories.InsertOneAsync(category);
        return category;
    }

    private Task<long> StylingProfileCountAsync(string userId)
        => Context.StylingProfiles.CountDocumentsAsync(p => p.UserId == userId);

    // ============================================
    // INDEX: unique trên userId
    // ============================================

    [Test]
    public async Task StylingProfileCollectionHasUniqueIndexOnUserId()
    {
        var user = await StylingUser();

        var result = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest { Styles = new() { "Casual" } });
        Assert.That(result.Success, Is.True, result.Message);

        var collection = _client.GetDatabase(_database).GetCollection<StylingProfile>("StylingProfiles");
        using var cursor = await collection.Indexes.ListAsync();
        var indexes = await cursor.ToListAsync();

        var unique = indexes.FirstOrDefault(i => i["name"].AsString == "ux_stylingProfiles_userId");
        Assert.That(unique, Is.Not.Null, "Thiếu unique index trên userId.");
        Assert.That(unique!["unique"].AsBoolean, Is.True);

        var keys = unique["key"].AsBsonDocument;
        Assert.That(keys["userId"].AsInt32, Is.EqualTo(1));
    }

    // ============================================
    // 1. TẠO HỒ SƠ
    // ============================================

    [Test]
    public async Task StylingProfileUpsertCreatesTheProfile()
    {
        var user = await StylingUser();

        var result = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest
        {
            Styles = new() { "Minimalist", "Vintage" },
            Sizes = new() { "M", "L" },
            Colors = new() { "Đen", "Be" },
            MinBudget = 100000,
            MaxBudget = 500000,
            Notes = "Thích đồ vintage"
        });

        Assert.That(result.Success, Is.True, result.Message);
        var response = (StylingProfileResponse)result.Data!;
        Assert.That(response.Styles, Is.EqualTo(new[] { "Minimalist", "Vintage" }));
        Assert.That(response.Sizes, Is.EqualTo(new[] { "M", "L" }));
        Assert.That(response.Colors, Is.EqualTo(new[] { "Đen", "Be" }));
        Assert.That(response.MinBudget, Is.EqualTo(100000));
        Assert.That(response.MaxBudget, Is.EqualTo(500000));
        Assert.That(response.Notes, Is.EqualTo("Thích đồ vintage"));

        var stored = await Context.StylingProfiles.Find(p => p.UserId == user.Id).FirstOrDefaultAsync();
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.UserId, Is.EqualTo(user.Id));
    }

    // ============================================
    // 2. CẬP NHẬT HỒ SƠ ĐÃ TỒN TẠI
    // ============================================

    [Test]
    public async Task StylingProfileUpsertUpdatesTheExistingProfile()
    {
        var user = await StylingUser();

        await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest { Styles = new() { "Casual" } });
        var result = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest { Styles = new() { "Streetwear" } });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(await StylingProfileCountAsync(user.Id), Is.EqualTo(1));

        var response = (StylingProfileResponse)result.Data!;
        Assert.That(response.Styles, Is.EqualTo(new[] { "Streetwear" }));
    }

    // ============================================
    // 3. PUT NHIỀU LẦN KHÔNG TẠO NHIỀU HỒ SƠ
    // ============================================

    [Test]
    public async Task RepeatedStylingProfileUpsertsKeepASingleProfile()
    {
        var user = await StylingUser();

        for (int i = 0; i < 5; i++)
        {
            var result = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest
            {
                Styles = new() { "Casual" },
                Notes = "round " + i
            });
            Assert.That(result.Success, Is.True, result.Message);
        }

        Assert.That(await StylingProfileCountAsync(user.Id), Is.EqualTo(1));
    }

    // ============================================
    // 4. GHI ĐÈ TOÀN BỘ (KHÔNG GIỮ NHÓM CŨ)
    // ============================================

    [Test]
    public async Task StylingProfileUpsertReplacesPreviousGroups()
    {
        var user = await StylingUser();

        await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest
        {
            Styles = new() { "Casual" },
            Colors = new() { "Đen" }
        });

        var result = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest
        {
            Sizes = new() { "M" }
        });

        var response = (StylingProfileResponse)result.Data!;
        Assert.That(response.Styles, Is.Empty);
        Assert.That(response.Colors, Is.Empty);
        Assert.That(response.Sizes, Is.EqualTo(new[] { "M" }));
    }

    // ============================================
    // 5. USER CHỈ ĐỌC/SỬA HỒ SƠ CỦA CHÍNH MÌNH
    // ============================================

    [Test]
    public async Task StylingProfileIsScopedToTheOwningUser()
    {
        var owner = await StylingUser();
        var intruder = await StylingUser();

        await StylingProfileService.UpsertAsync(owner.Id, new UpsertStylingProfileRequest { Styles = new() { "Vintage" } });

        // User B không thấy hồ sơ của user A.
        Assert.That(await StylingProfileService.GetAsync(intruder.Id), Is.Null);

        // User B PUT không ghi đè lên hồ sơ của user A.
        await StylingProfileService.UpsertAsync(intruder.Id, new UpsertStylingProfileRequest { Styles = new() { "Classic" } });
        var ownerProfile = await StylingProfileService.GetAsync(owner.Id);
        Assert.That(ownerProfile!.Styles, Is.EqualTo(new[] { "Vintage" }));

        // User B DELETE không xóa hồ sơ của user A.
        await StylingProfileService.DeleteAsync(intruder.Id);
        Assert.That(await StylingProfileCountAsync(owner.Id), Is.EqualTo(1));
        Assert.That(await StylingProfileCountAsync(intruder.Id), Is.Zero);
    }

    // ============================================
    // 6. VALIDATION: DỮ LIỆU KHÔNG HỢP LỆ BỊ TỪ CHỐI
    // ============================================

    [Test]
    public async Task StylingProfileUpsertRejectsInvalidData()
    {
        var user = await StylingUser();

        // Phong cách không tồn tại.
        var badStyle = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest { Styles = new() { "NeonCyber" } });
        Assert.That(badStyle.Success, Is.False);
        Assert.That(badStyle.ErrorCode, Is.EqualTo(ApiErrorCode.Validation));

        // Ngân sách âm.
        var negativeBudget = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest { MinBudget = -1 });
        Assert.That(negativeBudget.Success, Is.False);

        // Min > Max.
        var invertedBudget = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest { MinBudget = 500000, MaxBudget = 100000 });
        Assert.That(invertedBudget.Success, Is.False);

        // Id danh mục sai định dạng.
        var badCategory = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest { FavoriteCategoryIds = new() { "not-an-object-id" } });
        Assert.That(badCategory.Success, Is.False);

        // Id danh mục hợp lệ nhưng không tồn tại -> NOT_FOUND.
        var missingCategory = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest { FavoriteCategoryIds = new() { ObjectId.GenerateNewId().ToString() } });
        Assert.That(missingCategory.Success, Is.False);
        Assert.That(missingCategory.ErrorCode, Is.EqualTo(ApiErrorCode.NotFound));

        // Quá nhiều phong cách.
        var tooManyStyles = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest
        {
            Styles = Enumerable.Range(0, 11).Select(_ => "Casual").ToList()
        });
        Assert.That(tooManyStyles.Success, Is.False);

        // Ghi chú quá dài.
        var longNotes = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest { Notes = new string('a', 501) });
        Assert.That(longNotes.Success, Is.False);

        // Không có hồ sơ nào được tạo ra từ các request lỗi.
        Assert.That(await StylingProfileCountAsync(user.Id), Is.Zero);
    }

    // ============================================
    // 7. DANH MỤC YÊU THÍCH: LƯU ID, ĐỌC TÊN LIVE
    // ============================================

    [Test]
    public async Task StylingProfileStoresCategoryIdsAndReturnsLiveNames()
    {
        var user = await StylingUser();
        var category = await StylingCategory("Outerwear");

        var result = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest
        {
            FavoriteCategoryIds = new() { category.Id }
        });

        Assert.That(result.Success, Is.True, result.Message);
        var response = (StylingProfileResponse)result.Data!;
        Assert.That(response.FavoriteCategoryIds, Is.EqualTo(new[] { category.Id }));
        Assert.That(response.FavoriteCategories.Single().Name, Is.EqualTo("Outerwear"));
        Assert.That(response.FavoriteCategories.Single().Slug, Is.EqualTo("outerwear"));

        // Danh mục bị xóa khỏi catalog -> response không lỗi, chỉ bỏ qua danh mục đó.
        await Context.Categories.DeleteOneAsync(c => c.Id == category.Id);
        var afterDelete = await StylingProfileService.GetAsync(user.Id);
        Assert.That(afterDelete, Is.Not.Null);
        Assert.That(afterDelete!.FavoriteCategories, Is.Empty);
        Assert.That(afterDelete.FavoriteCategoryIds, Is.EqualTo(new[] { category.Id }));
    }

    // ============================================
    // 8. CHƯA CÓ HỒ SƠ
    // ============================================

    [Test]
    public async Task StylingProfileGetReturnsNullWhenNoProfileExists()
    {
        var user = await StylingUser();

        Assert.That(await StylingProfileService.GetAsync(user.Id), Is.Null);
    }

    // ============================================
    // 9. XÓA HỒ SƠ (IDEMPOTENT)
    // ============================================

    [Test]
    public async Task StylingProfileDeleteRemovesTheProfileAndIsIdempotent()
    {
        var user = await StylingUser();
        await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest { Styles = new() { "Sporty" } });

        var deleted = await StylingProfileService.DeleteAsync(user.Id);
        Assert.That(deleted.Success, Is.True, deleted.Message);
        Assert.That(await StylingProfileCountAsync(user.Id), Is.Zero);

        // Xóa lần 2 vẫn trả thành công.
        var deletedAgain = await StylingProfileService.DeleteAsync(user.Id);
        Assert.That(deletedAgain.Success, Is.True, deletedAgain.Message);
    }

    // ============================================
    // 10. KHÔNG ẢNH HƯỞNG MODULE CỦA BACKEND 2
    // ============================================

    [Test]
    public async Task StylingProfileDoesNotTouchUserProfileOrAddress()
    {
        var user = await StylingUser();
        var address = new Address { UserId = user.Id, RecipientName = "Styling fixture", StreetAddress = "Test-only address" };
        await Addresses.CreateAsync(address);

        await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest { Styles = new() { "Elegant" } });
        await StylingProfileService.GetAsync(user.Id);

        var storedUser = (await Users.GetByIdAsync(user.Id))!;
        Assert.That(storedUser.FullName, Is.EqualTo("Styling fixture"));
        Assert.That(storedUser.AvatarUrl, Is.Null);
        Assert.That(storedUser.Role, Is.EqualTo(UserRole.Member));

        var storedAddress = (await Addresses.GetByIdAsync(address.Id))!;
        Assert.That(storedAddress.RecipientName, Is.EqualTo("Styling fixture"));
    }

    // ============================================
    // 11. KHÔNG EXPOSE DỮ LIỆU NỘI BỘ
    // ============================================

    [Test]
    public async Task StylingProfileResponseDoesNotExposeUserAccountData()
    {
        var user = await StylingUser();
        user.PasswordHash = "hash-should-never-leak";
        await Users.UpdateAsync(user);

        var result = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest { Styles = new() { "Casual" } });
        var json = JsonSerializer.Serialize(result.Data, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.That(json, Does.Not.Contain("hash-should-never-leak"));
        Assert.That(json, Does.Not.Contain("PasswordHash"));
        Assert.That(json, Does.Not.Contain(user.Email));
    }

    // ============================================
    // HTTP: 401 + status code + ownership
    // ============================================

    [Test]
    public async Task StylingProfileHttpRequiresAuthorizationAndMapsStatusCodes()
    {
        var user = await StylingUser();
        var other = await StylingUser();
        var category = await StylingCategory("Bottoms");

        await using var host = await StartHttpAsync();
        using var anonymous = host.Client();
        using var client = host.Client(user);
        using var otherClient = host.Client(other);

        // Chưa đăng nhập -> 401.
        Assert.That((await anonymous.GetAsync("/api/styling-profile")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await anonymous.PutAsJsonAsync("/api/styling-profile", new UpsertStylingProfileRequest())).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await anonymous.DeleteAsync("/api/styling-profile")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        // Chưa có hồ sơ -> 404.
        var missing = await client.GetAsync("/api/styling-profile");
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        // PUT tạo hồ sơ -> 200.
        var created = await client.PutAsJsonAsync("/api/styling-profile", new UpsertStylingProfileRequest
        {
            Styles = new() { "Minimalist" },
            Sizes = new() { "M" },
            FavoriteCategoryIds = new() { category.Id }
        });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());

        // PUT lần 2 vẫn 200 và không tạo hồ sơ thứ hai.
        await client.PutAsJsonAsync("/api/styling-profile", new UpsertStylingProfileRequest { Styles = new() { "Vintage" } });
        var profile = await Phase5HttpHost.Data<StylingProfileResponse>(await client.GetAsync("/api/styling-profile"));
        Assert.That(profile.Styles, Is.EqualTo(new[] { "Vintage" }));
        Assert.That(await StylingProfileCountAsync(user.Id), Is.EqualTo(1));

        // User khác không thấy hồ sơ của user này.
        var otherGet = await otherClient.GetAsync("/api/styling-profile");
        Assert.That(otherGet.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        // Dữ liệu không hợp lệ -> 400.
        var invalid = await client.PutAsJsonAsync("/api/styling-profile", new UpsertStylingProfileRequest { Styles = new() { "NotAStyle" } });
        Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        // Danh mục không tồn tại -> 404.
        var missingCategory = await client.PutAsJsonAsync("/api/styling-profile", new UpsertStylingProfileRequest
        {
            FavoriteCategoryIds = new() { ObjectId.GenerateNewId().ToString() }
        });
        Assert.That(missingCategory.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        // DELETE -> 200 (idempotent), sau đó GET -> 404.
        Assert.That((await client.DeleteAsync("/api/styling-profile")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.DeleteAsync("/api/styling-profile")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.GetAsync("/api/styling-profile")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
