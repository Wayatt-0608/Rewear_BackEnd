using System.Net;
using System.Net.Http.Json;
using MongoDB.Bson;
using NUnit.Framework;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Application.Services;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Repositories;

namespace REWEAR.Tests;

/// <summary>
/// Test hồi quy cho các lỗi đã phát hiện trong review Styling Profile.
/// </summary>
/// <remarks>
/// <para>
/// Các test ở đây được viết SAU khi xác nhận lỗi tồn tại (chạy fail trên code cũ),
/// nên chúng là bằng chứng hồi quy chứ không phải test mới tính năng.
/// </para>
/// </remarks>
public partial class CommerceTests
{
    // ============================================
    // REGRESSION 1: style phải là TÊN hợp lệ, không phải số
    // ============================================

    [Test]
    public async Task StylingProfileRejectsNumericAndUndefinedStyleValues()
    {
        var user = await StylingUser();

        // Enum.TryParse chấp nhận chuỗi số ("7" -> giá trị ngoài danh sách).
        foreach (var raw in new[] { "7", "-1", "0", "999" })
        {
            var result = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest
            {
                Styles = new() { raw }
            });

            Assert.That(result.Success, Is.False, $"Style '{raw}' phải bị từ chối.");
            Assert.That(result.ErrorCode, Is.EqualTo(ApiErrorCode.Validation), $"Style '{raw}'.");
        }

        // Tên hợp lệ vẫn được chấp nhận (không phân biệt hoa thường).
        var valid = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest
        {
            Styles = new() { "minimalist", "Casual" }
        });
        Assert.That(valid.Success, Is.True, valid.Message);
        Assert.That(((StylingProfileResponse)valid.Data!).Styles, Is.EqualTo(new[] { "Minimalist", "Casual" }));

        Assert.That(await StylingProfileCountAsync(user.Id), Is.EqualTo(1));
    }

    // ============================================
    // REGRESSION 2: update phải báo hồ sơ biến mất, không im lặng bỏ qua
    // ============================================

    [Test]
    public async Task StylingProfileRepositoryUpdateReportsWhenTheDocumentIsGone()
    {
        var user = await StylingUser();
        var profile = new StylingProfile { UserId = user.Id, Styles = new() { "Casual" } };
        await StylingProfiles.CreateAsync(profile);

        // Hồ sơ bị xóa (mô phỏng DELETE chạy song song với PUT).
        await Context.StylingProfiles.DeleteOneAsync(p => p.Id == profile.Id);

        // Update phải báo "không còn bản ghi để cập nhật" thay vì im lặng thành công.
        var updated = await StylingProfiles.UpdateAsync(profile);
        Assert.That(updated, Is.False, "Update phải trả false khi bản ghi không còn tồn tại.");
    }

    [Test]
    public async Task StylingProfileUpsertReportsConflictWhenTheProfileDisappearsBeforeUpdate()
    {
        var user = await StylingUser();

        // Repository giả: đọc được hồ sơ nhưng bản ghi bị xóa trước lúc ghi.
        var service = new StylingProfileService(new VanishingStylingProfileRepository(user.Id), new CategoryRepository(Context));

        var result = await service.UpsertAsync(user.Id, new UpsertStylingProfileRequest { Styles = new() { "Casual" } });

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(ApiErrorCode.Conflict));
    }

    // ============================================
    // REGRESSION 3: HTTP map Conflict -> 409 (đồng bộ các controller khác)
    // ============================================

    [Test]
    public async Task StylingProfileHttpMapsConflictToConflictStatus()
    {
        var user = await StylingUser();

        await using var host = await StartHttpAsync(stylingProfiles: new VanishingStylingProfileRepository(user.Id));
        using var client = host.Client(user);

        var response = await client.PutAsJsonAsync("/api/styling-profile", new UpsertStylingProfileRequest
        {
            Styles = new() { "Casual" }
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), await response.Content.ReadAsStringAsync());
    }

    // ============================================
    // REGRESSION 4: thông báo lỗi giới hạn phải nói đúng nguyên nhân
    // ============================================

    [Test]
    public async Task StylingProfileLimitMessagesDistinguishCountFromLength()
    {
        var user = await StylingUser();

        // Vượt số lượng -> phải nói rõ vượt số lượng.
        var tooMany = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest
        {
            Styles = Enumerable.Range(0, 11).Select(_ => "Casual").ToList()
        });
        Assert.That(tooMany.Success, Is.False);
        Assert.That(tooMany.Message, Does.Contain("10"), tooMany.Message);
        Assert.That(tooMany.Message, Does.Not.Contain("ký tự"), tooMany.Message);

        // Vượt số lượng kích cỡ -> phải nói về số lượng, KHÔNG nói về độ dài.
        // (Thông báo sai nguyên nhân làm frontend hiển thị thông báo lỗi gây hiểu nhầm.)
        var tooManySizes = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest
        {
            Sizes = Enumerable.Range(0, 21).Select(i => "S" + i).ToList()
        });
        Assert.That(tooManySizes.Success, Is.False);
        Assert.That(tooManySizes.Message, Does.Contain("20"), tooManySizes.Message);
        Assert.That(tooManySizes.Message, Does.Not.Contain("ký tự"), tooManySizes.Message);
        // Vượt độ dài -> phải nói rõ vượt độ dài.
        var tooLong = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest
        {
            Sizes = new() { new string('a', 51) }
        });
        Assert.That(tooLong.Success, Is.False);
        Assert.That(tooLong.Message, Does.Contain("ký tự"), tooLong.Message);
    }

    // ============================================
    // REGRESSION 5: danh mục đã ẩn (soft delete) không được là yêu thích
    // ============================================

    [Test]
    public async Task StylingProfileRejectsInactiveCategory()
    {
        var user = await StylingUser();
        var category = await StylingCategory("Hidden");
        category.IsActive = false;
        await Context.Categories.ReplaceOneAsync(c => c.Id == category.Id, category);

        var result = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest
        {
            FavoriteCategoryIds = new() { category.Id }
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(ApiErrorCode.Validation));
        Assert.That(await StylingProfileCountAsync(user.Id), Is.Zero);
    }

    // ============================================
    // REGRESSION 6: nhiều danh mục -> thứ tự giữ nguyên, trả về đủ
    // ============================================

    [Test]
    public async Task StylingProfileKeepsCategoryOrderAndReturnsAllOfThem()
    {
        var user = await StylingUser();
        var first = await StylingCategory("First");
        var second = await StylingCategory("Second");
        var third = await StylingCategory("Third");

        var result = await StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest
        {
            FavoriteCategoryIds = new() { third.Id, first.Id, second.Id }
        });

        Assert.That(result.Success, Is.True, result.Message);
        var response = (StylingProfileResponse)result.Data!;

        Assert.That(response.FavoriteCategoryIds, Is.EqualTo(new[] { third.Id, first.Id, second.Id }));
        Assert.That(response.FavoriteCategories.Select(c => c.Name),
            Is.EqualTo(new[] { "Third", "First", "Second" }));
    }

    // ============================================
    // REGRESSION 7: PUT song song -> vẫn đúng 1 hồ sơ
    // ============================================

    [Test]
    public async Task ConcurrentStylingProfileUpsertsKeepExactlyOneProfile()
    {
        var user = await StylingUser();

        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(i => StylingProfileService.UpsertAsync(user.Id, new UpsertStylingProfileRequest
            {
                Styles = new() { "Casual" },
                Notes = "round " + i
            })));

        Assert.That(results.All(r => r.Success), Is.True,
            string.Join(" | ", results.Select(r => r.Message)));
        Assert.That(await StylingProfileCountAsync(user.Id), Is.EqualTo(1));
    }

    // ============================================
    // REGRESSION 10: JSON bất thường phải trả 400 chứ không phải 500
    // ============================================

    [Test]
    public async Task StylingProfileHttpRejectsMalformedJsonWithBadRequest()
    {
        var user = await StylingUser();

        await using var host = await StartHttpAsync();
        using var client = host.Client(user);

        // "styles" là object thay vì mảng.
        var wrongShape = await client.PutAsJsonAsync("/api/styling-profile",
            new { styles = new { unexpected = "object" } });
        Assert.That(wrongShape.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            await wrongShape.Content.ReadAsStringAsync());

        // "styles" là chuỗi thay vì mảng.
        var wrongType = await client.PutAsJsonAsync("/api/styling-profile",
            new { styles = "Casual" });
        Assert.That(wrongType.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            await wrongType.Content.ReadAsStringAsync());

        // "minBudget" là chuỗi không parse được thành số.
        var badNumber = await client.PutAsJsonAsync("/api/styling-profile",
            new { minBudget = "khong-phai-so" });
        Assert.That(badNumber.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            await badNumber.Content.ReadAsStringAsync());

        // "minBudget" vượt giới hạn decimal -> overflow khi parse.
        var overflow = await client.PutAsJsonAsync("/api/styling-profile",
            new { minBudget = "1e40" });
        Assert.That(overflow.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            await overflow.Content.ReadAsStringAsync());

        // Body rỗng.
        var emptyBody = await client.PutAsJsonAsync("/api/styling-profile", (object?)null);
        Assert.That(emptyBody.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            await emptyBody.Content.ReadAsStringAsync());

        // Không request lỗi nào được tạo ra hồ sơ.
        Assert.That(await StylingProfileCountAsync(user.Id), Is.Zero);
    }

    // ============================================
    // REGRESSION 9: document có field null không được làm hỏng API đọc
    // ============================================

    [Test]
    public async Task StylingProfileWithNullListFieldsIsReadSafely()
    {
        var user = await StylingUser();

        // Document ghi tay có field list = null (có thể do import dữ liệu cũ).
        var id = ObjectId.GenerateNewId().ToString();
        var raw = _client.GetDatabase(_database).GetCollection<BsonDocument>("StylingProfiles");
        await raw.InsertOneAsync(new BsonDocument
        {
            { "_id", ObjectId.Parse(id) },
            { "userId", user.Id },
            { "styles", BsonNull.Value },
            { "sizes", BsonNull.Value },
            { "colors", BsonNull.Value },
            { "favoriteCategoryIds", BsonNull.Value },
            { "createdAt", DateTime.UtcNow },
            { "updatedAt", DateTime.UtcNow }
        });

        var response = await StylingProfileService.GetAsync(user.Id);

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Styles, Is.Empty);
        Assert.That(response.Sizes, Is.Empty);
        Assert.That(response.Colors, Is.Empty);
        Assert.That(response.FavoriteCategoryIds, Is.Empty);
        Assert.That(response.FavoriteCategories, Is.Empty);
    }

    // ============================================
    // REGRESSION 8: không N+1 query khi đọc nhiều danh mục
    // ============================================

    [Test]
    public async Task StylingProfileReadsCategoriesInOneBatchQuery()
    {
        var user = await StylingUser();
        var ids = new List<string>();
        for (int i = 0; i < 5; i++) ids.Add((await StylingCategory("Cat" + i)).Id);

        var counting = new CountingCategoryRepository(new CategoryRepository(Context));
        var service = new StylingProfileService(StylingProfiles, counting);

        await service.UpsertAsync(user.Id, new UpsertStylingProfileRequest
        {
            FavoriteCategoryIds = ids
        });

        // Chỉ đo phần đọc lại hồ sơ.
        counting.Reset();

        var response = await service.GetAsync(user.Id);

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.FavoriteCategories, Has.Count.EqualTo(5));

        // Đọc 5 danh mục phải tốn 1 lệnh batch, không phải 5 lệnh lẻ.
        Assert.That(counting.BatchCalls, Is.EqualTo(1), $"Batch calls: {counting.BatchCalls}.");
        Assert.That(counting.SingleCalls, Is.Zero, $"Single calls: {counting.SingleCalls}.");
    }
}

/// <summary>
/// Đếm lời gọi để phát hiện truy vấn N+1 (mỗi danh mục một lệnh).
/// </summary>
internal sealed class CountingCategoryRepository : ICategoryRepository
{
    private readonly ICategoryRepository _inner;

    public int BatchCalls { get; private set; }
    public int SingleCalls { get; private set; }

    public void Reset()
    {
        BatchCalls = 0;
        SingleCalls = 0;
    }

    public CountingCategoryRepository(ICategoryRepository inner)
    {
        _inner = inner;
    }

    public Task<List<Category>> GetAllAsync() => _inner.GetAllAsync();

    public Task<List<Category>> GetAllAsync(bool? isActive) => _inner.GetAllAsync(isActive);

    public Task<Category?> GetByIdAsync(string id)
    {
        SingleCalls++;
        return _inner.GetByIdAsync(id);
    }

    public Task<List<Category>> GetByIdsAsync(IEnumerable<string> ids)
    {
        BatchCalls++;
        return _inner.GetByIdsAsync(ids);
    }

    public Task<Category?> GetByNameAsync(string name) => _inner.GetByNameAsync(name);

    public Task CreateAsync(Category category) => _inner.CreateAsync(category);

    public Task UpdateAsync(Category category) => _inner.UpdateAsync(category);
}

/// <summary>
/// Repository giả mô phỏng race: đọc được hồ sơ, nhưng bản ghi bị xóa trước lúc ghi.
/// </summary>
internal sealed class VanishingStylingProfileRepository : IStylingProfileRepository
{
    private readonly string _userId;

    public VanishingStylingProfileRepository(string userId)
    {
        _userId = userId;
    }

    public Task<StylingProfile?> GetByUserIdAsync(string userId)
        => Task.FromResult<StylingProfile?>(new StylingProfile { UserId = _userId, CreatedAt = DateTime.UtcNow });

    public Task CreateAsync(StylingProfile profile) => Task.CompletedTask;

    public Task<bool> UpdateAsync(StylingProfile profile) => Task.FromResult(false);

    public Task<bool> DeleteByUserIdAsync(string userId) => Task.FromResult(true);
}
