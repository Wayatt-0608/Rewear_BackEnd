using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using REWEAR.Application.DTOs;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Tests;

/// <summary>
/// Test cho Wishlist (danh sách yêu thích) của Product.
/// Phạm vi: chỉ Wishlist của Product, KHÔNG đụng Like/Save của Community Post.
/// </summary>
public partial class CommerceTests
{
    /// <summary>
    /// Tạo 1 user mới (không cần address/cart) để test wishlist.
    /// </summary>
    private async Task<User> WishlistUser()
    {
        var user = new User
        {
            FullName = "Wishlist fixture",
            Email = Guid.NewGuid() + "@example.test",
            IsVerified = true,
            Role = UserRole.Member
        };
        await Users.CreateAsync(user);
        return user;
    }

    /// <summary>
    /// Tạo product còn mua được (Available + IsActive + còn tồn).
    /// </summary>
    private async Task<Product> WishlistProduct(string title = "Wishlist item", int stock = 1)
    {
        var product = new Product
        {
            Title = title,
            Price = 299000,
            StockQuantity = stock,
            IsUnique = true,
            Status = ProductStatus.Available,
            IsActive = true,
            ImageUrls = new List<string> { "https://res.cloudinary.com/fixture/image.jpg" },
            Size = "M",
            Color = "Xanh"
        };
        await Products.CreateAsync(product);
        return product;
    }

    private Task<long> WishlistCountAsync(string userId)
        => Context.WishlistItems.CountDocumentsAsync(i => i.UserId == userId);

    // ============================================
    // INDEX: unique compound (userId, productId) + index sort mới nhất
    // ============================================

    [Test]
    public async Task WishlistCollectionHasUniqueCompoundIndexOnUserAndProduct()
    {
        var user = await WishlistUser();
        var product = await WishlistProduct();
        await WishlistService.AddAsync(user.Id, product.Id);

        var collection = _client.GetDatabase(_database).GetCollection<WishlistItem>("WishlistItems");
        using var cursor = await collection.Indexes.ListAsync();
        var indexes = await cursor.ToListAsync();

        var unique = indexes.FirstOrDefault(i =>
            i["name"].AsString == "ux_wishlistItems_userId_productId");

        Assert.That(unique, Is.Not.Null, "Thiếu unique index (userId, productId).");
        Assert.That(unique!["unique"].AsBoolean, Is.True);

        var keys = unique["key"].AsBsonDocument;
        Assert.That(keys["userId"].AsInt32, Is.EqualTo(1));
        Assert.That(keys["productId"].AsInt32, Is.EqualTo(1));
    }

    // ============================================
    // 1. ADD THÀNH CÔNG
    // ============================================

    [Test]
    public async Task WishlistAddStoresOnlyTheProductLink()
    {
        var user = await WishlistUser();
        var product = await WishlistProduct();

        var result = await WishlistService.AddAsync(user.Id, product.Id);

        Assert.That(result.Success, Is.True, result.Message);
        var stored = await Wishlists.GetByUserAndProductAsync(user.Id, product.Id);
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.UserId, Is.EqualTo(user.Id));
        Assert.That(stored.ProductId, Is.EqualTo(product.Id));
    }

    // ============================================
    // 2. ADD TRÙNG (idempotent)
    // ============================================

    [Test]
    public async Task WishlistAddIsIdempotent()
    {
        var user = await WishlistUser();
        var product = await WishlistProduct();

        var first = await WishlistService.AddAsync(user.Id, product.Id);
        var second = await WishlistService.AddAsync(user.Id, product.Id);

        Assert.That(first.Success, Is.True, first.Message);
        Assert.That(second.Success, Is.True, second.Message);
        Assert.That(await WishlistCountAsync(user.Id), Is.EqualTo(1));
    }

    // ============================================
    // 3. CONCURRENT ADD KHÔNG TẠO DUPLICATE
    // ============================================

    [Test]
    public async Task ConcurrentWishlistAddsCreateOnlyOneRecord()
    {
        var user = await WishlistUser();
        var product = await WishlistProduct();

        // Unique compound index (userId, productId) là hàng rào chống duplicate.
        var results = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => WishlistService.AddAsync(user.Id, product.Id)));

        Assert.That(results.All(r => r.Success), Is.True);
        Assert.That(await WishlistCountAsync(user.Id), Is.EqualTo(1));
    }

    // ============================================
    // 4. PRODUCT KHÔNG TỒN TẠI -> 404
    // ============================================

    [Test]
    public async Task WishlistAddRejectsUnknownProduct()
    {
        var user = await WishlistUser();

        var result = await WishlistService.AddAsync(user.Id, MongoDB.Bson.ObjectId.GenerateNewId().ToString());

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(ApiErrorCode.NotFound));
        Assert.That(await WishlistCountAsync(user.Id), Is.Zero);
    }

    // ============================================
    // 5. PRODUCT KHÔNG ĐỦ ĐIỀU KIỆN HIỂN THỊ
    // ============================================

    [Test]
    public async Task WishlistAddRejectsProductsThatAreNotPurchaseEligible()
    {
        var user = await WishlistUser();

        var sold = await WishlistProduct("Sold item");
        sold.Status = ProductStatus.Sold; await Products.UpdateAsync(sold);

        var reserved = await WishlistProduct("Reserved item");
        reserved.Status = ProductStatus.Reserved; await Products.UpdateAsync(reserved);

        var hidden = await WishlistProduct("Hidden item");
        hidden.IsActive = false; await Products.UpdateAsync(hidden);

        var outOfStock = await WishlistProduct("Out of stock", stock: 0);
        outOfStock.StockQuantity = 0; await Products.UpdateAsync(outOfStock);

        foreach (var product in new[] { sold, reserved, hidden, outOfStock })
        {
            var result = await WishlistService.AddAsync(user.Id, product.Id);
            Assert.That(result.Success, Is.False, product.Title);
            Assert.That(result.ErrorCode, Is.EqualTo(ApiErrorCode.Conflict), product.Title);
        }

        Assert.That(await WishlistCountAsync(user.Id), Is.Zero);
    }

    // ============================================
    // 6. GET WISHLIST CỦA USER
    // ============================================

    [Test]
    public async Task WishlistListReturnsOnlyTheOwnersItems()
    {
        var user = await WishlistUser();
        var other = await WishlistUser();
        var product = await WishlistProduct();
        var otherProduct = await WishlistProduct("Other item");

        await WishlistService.AddAsync(user.Id, product.Id);
        await WishlistService.AddAsync(other.Id, otherProduct.Id);

        var page = await WishlistService.GetMyWishlistAsync(user.Id, new WishlistQueryParameters());

        Assert.That(page.TotalCount, Is.EqualTo(1));
        Assert.That(page.Items.Single().ProductId, Is.EqualTo(product.Id));
        Assert.That(page.Items.Single().Title, Is.EqualTo(product.Title));
        Assert.That(page.Items.Single().Price, Is.EqualTo(product.Price));
        Assert.That(page.Items.Single().ImageUrl, Is.EqualTo(product.ImageUrls.First()));
        Assert.That(page.Items.Single().Size, Is.EqualTo("M"));
        Assert.That(page.Items.Single().Color, Is.EqualTo("Xanh"));
        Assert.That(page.Items.Single().IsAvailableForPurchase, Is.True);
    }

    // ============================================
    // 7. USER A KHÔNG XEM / KHÔNG XÓA WISHLIST CỦA USER B
    // ============================================

    [Test]
    public async Task WishlistIsScopedToTheOwningUser()
    {
        var owner = await WishlistUser();
        var intruder = await WishlistUser();
        var product = await WishlistProduct();

        await WishlistService.AddAsync(owner.Id, product.Id);

        // User B không thấy item của user A.
        var intruderList = await WishlistService.GetMyWishlistAsync(intruder.Id, new WishlistQueryParameters());
        Assert.That(intruderList.Items, Is.Empty);
        Assert.That(intruderList.TotalCount, Is.Zero);

        // User B xóa product của user A -> không ảnh hưởng, vẫn còn item của user A.
        await WishlistService.RemoveAsync(intruder.Id, product.Id);
        Assert.That(await WishlistCountAsync(owner.Id), Is.EqualTo(1));
        Assert.That(await WishlistCountAsync(intruder.Id), Is.Zero);

        // Check cũng phải scope theo user.
        var intruderCheck = await WishlistService.CheckAsync(intruder.Id, product.Id);
        Assert.That(intruderCheck.IsWishlisted, Is.False);
        var ownerCheck = await WishlistService.CheckAsync(owner.Id, product.Id);
        Assert.That(ownerCheck.IsWishlisted, Is.True);
    }

    // ============================================
    // 8 & 9. REMOVE (thành công + idempotent)
    // ============================================

    [Test]
    public async Task WishlistRemoveDeletesTheItemAndIsIdempotent()
    {
        var user = await WishlistUser();
        var product = await WishlistProduct();
        await WishlistService.AddAsync(user.Id, product.Id);

        var removed = await WishlistService.RemoveAsync(user.Id, product.Id);
        Assert.That(removed.Success, Is.True, removed.Message);
        Assert.That(await WishlistCountAsync(user.Id), Is.Zero);

        // Xóa lần 2 (bản ghi không tồn tại) vẫn trả thành công.
        var removedAgain = await WishlistService.RemoveAsync(user.Id, product.Id);
        Assert.That(removedAgain.Success, Is.True, removedAgain.Message);
    }

    // ============================================
    // 10. CHECK TRUE / FALSE
    // ============================================

    [Test]
    public async Task WishlistCheckReportsMembership()
    {
        var user = await WishlistUser();
        var product = await WishlistProduct();

        var before = await WishlistService.CheckAsync(user.Id, product.Id);
        Assert.That(before.IsWishlisted, Is.False);
        Assert.That(before.ProductId, Is.EqualTo(product.Id));

        await WishlistService.AddAsync(user.Id, product.Id);

        var after = await WishlistService.CheckAsync(user.Id, product.Id);
        Assert.That(after.IsWishlisted, Is.True);
    }

    // ============================================
    // 11. PAGINATION
    // ============================================

    [Test]
    public async Task WishlistListPaginates()
    {
        var user = await WishlistUser();
        for (int i = 0; i < 5; i++)
        {
            var product = await WishlistProduct($"Item {i}");
            await WishlistService.AddAsync(user.Id, product.Id);
        }

        var page1 = await WishlistService.GetMyWishlistAsync(user.Id, new WishlistQueryParameters { Page = 1, PageSize = 2 });
        var page2 = await WishlistService.GetMyWishlistAsync(user.Id, new WishlistQueryParameters { Page = 2, PageSize = 2 });
        var page3 = await WishlistService.GetMyWishlistAsync(user.Id, new WishlistQueryParameters { Page = 3, PageSize = 2 });

        Assert.That(page1.TotalCount, Is.EqualTo(5));
        Assert.That(page1.Items, Has.Count.EqualTo(2));
        Assert.That(page2.Items, Has.Count.EqualTo(2));
        Assert.That(page3.Items, Has.Count.EqualTo(1));
        Assert.That(page1.TotalPages, Is.EqualTo(3));
        Assert.That(page1.HasNext, Is.True);
        Assert.That(page1.HasPrevious, Is.False);
        Assert.That(page3.HasNext, Is.False);
        Assert.That(page3.HasPrevious, Is.True);

        // Các trang không trùng nhau.
        var ids = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(i => i.ProductId).ToList();
        Assert.That(ids.Distinct().Count(), Is.EqualTo(5));
    }

    // ============================================
    // 12. SORT MỚI NHẤT TRƯỚC
    // ============================================

    [Test]
    public async Task WishlistListSortsNewestFirst()
    {
        var user = await WishlistUser();
        var first = await WishlistProduct("First");
        var second = await WishlistProduct("Second");
        var third = await WishlistProduct("Third");

        await WishlistService.AddAsync(user.Id, first.Id);
        await Task.Delay(15);
        await WishlistService.AddAsync(user.Id, second.Id);
        await Task.Delay(15);
        await WishlistService.AddAsync(user.Id, third.Id);

        var page = await WishlistService.GetMyWishlistAsync(user.Id, new WishlistQueryParameters());

        Assert.That(page.Items.Select(i => i.ProductId),
            Is.EqualTo(new[] { third.Id, second.Id, first.Id }));
    }

    // ============================================
    // 13 & 14. PRODUCT SOLD / INACTIVE SAU KHI ĐÃ THÊM
    // ============================================

    [Test]
    public async Task WishlistKeepsTheLinkWhenProductBecomesSoldOrHidden()
    {
        var user = await WishlistUser();
        var sold = await WishlistProduct("Sold after add");
        var hidden = await WishlistProduct("Hidden after add");

        await WishlistService.AddAsync(user.Id, sold.Id);
        await WishlistService.AddAsync(user.Id, hidden.Id);

        sold.Status = ProductStatus.Sold; await Products.UpdateAsync(sold);
        hidden.IsActive = false; await Products.UpdateAsync(hidden);

        var page = await WishlistService.GetMyWishlistAsync(user.Id, new WishlistQueryParameters());

        Assert.That(page.TotalCount, Is.EqualTo(2));
        Assert.That(page.Items.All(i => !i.IsAvailableForPurchase), Is.True);

        // Vẫn xóa được khỏi wishlist.
        var removed = await WishlistService.RemoveAsync(user.Id, sold.Id);
        Assert.That(removed.Success, Is.True, removed.Message);
    }

    // ============================================
    // 15. KHÔNG THAY ĐỔI TỒN KHO
    // ============================================

    [Test]
    public async Task WishlistNeverChangesProductStockOrStatus()
    {
        var user = await WishlistUser();
        var product = await WishlistProduct(stock: 3);

        await WishlistService.AddAsync(user.Id, product.Id);
        await WishlistService.GetMyWishlistAsync(user.Id, new WishlistQueryParameters());
        await WishlistService.RemoveAsync(user.Id, product.Id);

        var stored = (await Products.GetByIdAsync(product.Id))!;
        Assert.That(stored.StockQuantity, Is.EqualTo(3));
        Assert.That(stored.Status, Is.EqualTo(ProductStatus.Available));
        Assert.That(stored.IsActive, Is.True);
    }

    // ============================================
    // 16. KHÔNG TÁC ĐỘNG CART / CHECKOUT
    // ============================================

    [Test]
    public async Task WishlistDoesNotTouchCartOrCheckout()
    {
        var product = await WishlistProduct();
        var buyer = await Buyer(product);

        var before = await Carts.GetByUserIdAsync(buyer.User.Id);
        Assert.That(before!.Items, Has.Count.EqualTo(1));

        await WishlistService.AddAsync(buyer.User.Id, product.Id);
        await WishlistService.GetMyWishlistAsync(buyer.User.Id, new WishlistQueryParameters());

        var after = await Carts.GetByUserIdAsync(buyer.User.Id);
        Assert.That(after!.Items, Has.Count.EqualTo(1));
        Assert.That(after.Items.Single().ProductId, Is.EqualTo(before.Items.Single().ProductId));

        // Tồn kho vẫn nguyên -> checkout vẫn chạy được bình thường.
        var order = await Checkout(product, buyer.User, buyer.Address);
        Assert.That(order.Id, Is.Not.Empty);
        Assert.That((await Products.GetByIdAsync(product.Id))!.StockQuantity, Is.Zero);
    }

    // ============================================
    // 18. KHÔNG EXPOSE DỮ LIỆU NỘI BỘ
    // ============================================

    [Test]
    public async Task WishlistResponseDoesNotExposeInternalProductData()
    {
        var user = await WishlistUser();
        var product = await WishlistProduct();
        product.AcquisitionCost = 111111m;
        product.ProductionCost = 222222m;
        product.SourceSourcingId = "internal-sourcing-id";
        product.HasCostBasis = true;
        await Products.UpdateAsync(product);

        await WishlistService.AddAsync(user.Id, product.Id);
        var page = await WishlistService.GetMyWishlistAsync(user.Id, new WishlistQueryParameters());

        var json = JsonSerializer.Serialize(page, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.That(json, Does.Not.Contain("AcquisitionCost"));
        Assert.That(json, Does.Not.Contain("ProductionCost"));
        Assert.That(json, Does.Not.Contain("SourceSourcingId"));
        Assert.That(json, Does.Not.Contain("111111"));
        Assert.That(json, Does.Not.Contain("internal-sourcing-id"));
    }

    // ============================================
    // HTTP: 401 khi chưa đăng nhập + status code đúng
    // ============================================

    [Test]
    public async Task WishlistHttpRequiresAuthorizationAndMapsStatusCodes()
    {
        var user = await WishlistUser();
        var product = await WishlistProduct();

        await using var host = await StartHttpAsync();
        using var anonymous = host.Client();
        using var client = host.Client(user);

        // 17. Không token -> 401.
        Assert.That((await anonymous.GetAsync("/api/wishlist")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await anonymous.PostAsync($"/api/wishlist/{product.Id}", null)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await anonymous.DeleteAsync($"/api/wishlist/{product.Id}")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await anonymous.GetAsync($"/api/wishlist/check/{product.Id}")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        // Thêm thành công -> 200.
        var added = await client.PostAsync($"/api/wishlist/{product.Id}", null);
        Assert.That(added.StatusCode, Is.EqualTo(HttpStatusCode.OK), await added.Content.ReadAsStringAsync());

        // Thêm trùng -> vẫn 200 (idempotent).
        var addedAgain = await client.PostAsync($"/api/wishlist/{product.Id}", null);
        Assert.That(addedAgain.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Product không tồn tại -> 404.
        var missing = await client.PostAsync($"/api/wishlist/{MongoDB.Bson.ObjectId.GenerateNewId().ToString()}", null);
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        // ProductId sai định dạng -> 400.
        var invalid = await client.PostAsync("/api/wishlist/not-an-object-id", null);
        Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        // GET list -> 200 và có item.
        var list = await Phase5HttpHost.Data<PagedWishlistResponse>(await client.GetAsync("/api/wishlist?page=1&pageSize=20"));
        Assert.That(list.Items.Single().ProductId, Is.EqualTo(product.Id));
        Assert.That(list.Items.Single().IsAvailableForPurchase, Is.True);

        // Check -> 200.
        var check = await Phase5HttpHost.Data<WishlistCheckResponse>(await client.GetAsync($"/api/wishlist/check/{product.Id}"));
        Assert.That(check.IsWishlisted, Is.True);

        // DELETE -> 200 (idempotent), sau đó check = false.
        Assert.That((await client.DeleteAsync($"/api/wishlist/{product.Id}")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.DeleteAsync($"/api/wishlist/{product.Id}")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var afterDelete = await Phase5HttpHost.Data<WishlistCheckResponse>(await client.GetAsync($"/api/wishlist/check/{product.Id}"));
        Assert.That(afterDelete.IsWishlisted, Is.False);
    }

    // ============================================
    // HTTP: product không đủ điều kiện -> 400
    // ============================================

    [Test]
    public async Task WishlistHttpRejectsUnavailableProductWithBadRequest()
    {
        var user = await WishlistUser();
        var product = await WishlistProduct();
        product.Status = ProductStatus.Sold;
        await Products.UpdateAsync(product);

        await using var host = await StartHttpAsync();
        using var client = host.Client(user);

        var response = await client.PostAsync($"/api/wishlist/{product.Id}", null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await response.Content.ReadAsStringAsync());
    }
}
