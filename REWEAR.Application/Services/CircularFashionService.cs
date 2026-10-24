using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Services;

public sealed class CircularFashionService(ILifecycleStore store, ISourcingRepository sourcing, IProductRepository products,
    IBrandRepository brands, ICategoryRepository categories, IUserRepository users, ICommerceUnitOfWork transactions)
{
    public Task<List<InventoryItem>> InventoryAsync() => store.ListAsync<InventoryItem>();
    public Task<List<PurchasePayout>> PayoutsAsync() => store.ListAsync<PurchasePayout>();
    public Task<List<UpcyclingProject>> ProjectsAsync() => store.ListAsync<UpcyclingProject>();
    public Task<UpcyclingProject?> ProjectAsync(string id) => store.GetAsync<UpcyclingProject>(id);
    public async Task<List<InventoryMovement>> MovementsAsync(string id) => (await store.ListAsync<InventoryMovement>()).Where(m => m.InventoryId == id).OrderBy(m => m.CreatedAt).ToList();
    public async Task<List<QualityInspection>> InspectionsAsync(string id) => (await store.ListAsync<QualityInspection>()).Where(q => q.ProjectId == id || q.ProjectId == "inventory:" + id).ToList();

    public Task<InventoryItem> ReceiveAsync(string id, string actor) => transactions.ExecuteAsync(async () =>
    {
        await Operator(actor);
        var source = await Source(id);
        if (source.Status is not (SourcingStatus.Accepted or SourcingStatus.Received)) throw new InvalidOperationException("Chỉ nhận món đồ đã được khách đồng ý giá.");
        var existing = await store.GetAsync<InventoryItem>(id);
        if (existing != null) return existing;
        source.Status = SourcingStatus.Received; await sourcing.UpdateAsync(source);
        var item = new InventoryItem { Id = source.Id, SourcingId = source.Id, Title = source.Title, Custody = "Warehouse", Images = source.ImageUrls.ToList() };
        await store.InsertAsync(item); await Movement(item, null, "AwaitingInspection", actor, "Nhận món đồ từ người gửi.", id);
        return item;
    });

    public Task<SourcingRequest> InspectSourceAsync(string id, InspectSourcingRequest request, string actor) => transactions.ExecuteAsync(async () =>
    {
        await Operator(actor); var source = await Source(id);
        if (source.Status != SourcingStatus.Received) throw new InvalidOperationException("Món đồ chưa ở bước kiểm định sau nhận hàng.");
        if (!Enum.IsDefined(request.InspectedCondition)) throw new ArgumentException("Tình trạng kiểm định không hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.AdminNote)) throw new ArgumentException("Cần ghi kết quả kiểm định.");
        ValidateImages(request.Images, false);
        var item = await store.GetAsync<InventoryItem>(id);
        if (item == null)
        {
            item = new InventoryItem { Id = id, SourcingId = id, Title = source.Title, Custody = "Warehouse", Images = source.ImageUrls.ToList() };
            await store.InsertAsync(item); await Movement(item, null, item.State, actor, "Tiếp nhận bản ghi nguồn hàng cũ tại bước kiểm định.", id);
        }
        source.InspectedBy = actor; source.InspectedAt = DateTime.UtcNow; source.InspectionImages = request.Images.ToList();
        source.InspectedCondition = request.InspectedCondition; source.AdminNote = request.AdminNote.Trim();
        if (!request.Passed)
        {
            source.Status = SourcingStatus.Rejected; source.RejectReason = request.AdminNote.Trim();
            await Move(item, "ReturnToSenderPending", actor, request.AdminNote, id);
        }
        else if (request.InspectedCondition != source.DeclaredCondition || (request.RevisedPrice.HasValue && request.RevisedPrice != source.AcceptedOfferPrice))
        {
            var price = request.RevisedPrice ?? source.OfferedPrice ?? 0; Money(price);
            source.OfferedPrice = price; source.RequiresRevisedAcceptance = true; source.OfferVersion++;
            source.OfferHistory.Add(new SourcingOffer { Version = source.OfferVersion, Amount = price, ActorId = actor });
            await Move(item, "AwaitingCustomerAcceptance", actor, "Chờ người gửi đồng ý kết quả kiểm định và giá hiện tại.", id);
        }
        else
        {
            source.Status = SourcingStatus.Approved; source.RequiresRevisedAcceptance = false;
            await Move(item, "AwaitingPayout", actor, request.AdminNote, id);
        }
        await sourcing.UpdateAsync(source); return source;
    });

    public Task<PurchasePayout> ConfirmPayoutAsync(string id, ConfirmPayoutRequest request, string actor) => transactions.ExecuteAsync(async () =>
    {
        request.Reference = request.Reference?.Trim() ?? "";
        await Operator(actor); Money(request.Amount);
        if (string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Length > 120 || request.Method is not ("ManualTransfer" or "Cash")) throw new ArgumentException("Cần phương thức và chứng từ chi trả hợp lệ.");
        var existing = await store.GetAsync<PurchasePayout>(id);
        if (existing != null)
        {
            if (existing.Reference != request.Reference || existing.Amount != request.Amount || existing.Method != request.Method) throw new InvalidOperationException("Yêu cầu đã có chứng từ chi trả khác, không được ghi đè.");
            return existing;
        }
        var source = await Source(id); var finalPrice = source.AcceptedOfferPrice ?? source.OfferedPrice;
        if (source.Status != SourcingStatus.Approved || source.RequiresRevisedAcceptance || finalPrice != request.Amount) throw new InvalidOperationException("Chưa được kiểm định/đồng ý giá hoặc số tiền không khớp.");
        var item = await store.GetAsync<InventoryItem>(id) ?? throw new InvalidOperationException("Cần tiếp nhận món đồ vào kho trước khi chi trả.");
        if (item.State != "AwaitingPayout") throw new InvalidOperationException("Món đồ không ở bước chờ chi trả.");
        var payout = new PurchasePayout { Id = id, SourcingId = id, CustomerId = source.UserId, Amount = request.Amount,
            Reference = request.Reference.Trim(), Method = request.Method, ConfirmedBy = actor, Note = request.Note };
        await store.InsertAsync(payout); item.AcquisitionCost = request.Amount;
        await Move(item, "AvailableRaw", actor, "Đã xác nhận chứng từ chi trả; nguyên liệu sẵn sàng.", id);
        return payout;
    });

    public Task<InventoryItem> ReturnSourceAsync(string id, string reference, string actor) => transactions.ExecuteAsync(async () =>
    {
        await Operator(actor); if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("Cần chứng từ trả đồ.");
        var item = await store.GetAsync<InventoryItem>(id) ?? throw new InvalidOperationException("Không tìm thấy món đồ.");
        if (item.State == "ReturnedToSender") return item;
        if (item.State != "ReturnToSenderPending") throw new InvalidOperationException("Món đồ không nằm trong danh sách chờ trả người gửi.");
        await Move(item, "ReturnedToSender", actor, reference, id); return item;
    });

    public Task<UpcyclingProject> CreateProjectAsync(CreateUpcyclingRequest request, string actor) => transactions.ExecuteAsync(async () =>
    {
        await Operator(actor); await Operator(request.ResponsibleStaffId);
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Concept)) throw new ArgumentException("Cần tên và ý tưởng dự án.");
        if (request.InventoryIds.Count is < 1 or > 20 || request.InventoryIds.Distinct().Count() != request.InventoryIds.Count) throw new ArgumentException("Chọn từ 1–20 nguyên liệu khác nhau.");
        var project = new UpcyclingProject { Title = request.Title.Trim(), Concept = request.Concept.Trim(), CreatedBy = actor,
            ResponsibleStaffId = request.ResponsibleStaffId, PlannedCompletion = request.PlannedCompletion, InventoryIds = request.InventoryIds.ToList() };
        foreach (var id in request.InventoryIds)
        {
            var item = await store.GetAsync<InventoryItem>(id) ?? throw new InvalidOperationException("Không tìm thấy nguyên liệu.");
            if (item.State != "AvailableRaw" || await store.GetAsync<PurchasePayout>(item.SourcingId) == null) throw new InvalidOperationException("Nguyên liệu chưa chi trả hoặc đã được cấp cho dự án khác.");
            item.ProjectId = project.Id; project.AcquisitionCost += item.AcquisitionCost; project.BeforeImages.AddRange(item.Images);
            await Move(item, "ReservedForUpcycling", actor, "Cấp nguyên liệu cho dự án.", project.Id);
        }
        project.Timeline.Add(new() { To = "Pending", ActorId = actor, Note = "Tạo dự án và giữ nguyên liệu." });
        await store.InsertAsync(project); return project;
    });

    public Task<UpcyclingProject> UpdateProjectAsync(string id, UpdateProjectRequest request, string actor) => transactions.ExecuteAsync(async () =>
    {
        await Operator(actor); var project = await RequiredProject(id);
        bool allowed = (project.State, request.State) is ("Pending", "InProgress") or ("Rework", "InProgress") or ("InProgress", "QualityCheck");
        if (!allowed) throw new InvalidOperationException("Không thể chuyển dự án theo bước này.");
        if (request.AfterImages != null) { ValidateImages(request.AfterImages, true); project.AfterImages = request.AfterImages.ToList(); }
        if (request.State == "QualityCheck" && project.AfterImages.Count == 0) throw new InvalidOperationException("Cần ảnh thành phẩm trước khi gửi QC.");
        var from = project.State; project.State = request.State;
        project.Timeline.Add(new() { From = from, To = project.State, ActorId = actor, Note = request.Note });
        foreach (var itemId in project.InventoryIds)
        {
            var item = (await store.GetAsync<InventoryItem>(itemId))!;
            await Move(item, request.State == "QualityCheck" ? "FinishedAwaitingQC" : "UpcyclingInProgress", actor, request.Note, project.Id);
        }
        await store.SaveAsync(project); return project;
    });

    public Task<UpcyclingProject> AddCostAsync(string id, RecordCostRequest request, string actor) => transactions.ExecuteAsync(async () =>
    {
        await Operator(actor); Money(request.Amount);
        if (request.Kind is not ("Material" or "Labor") || string.IsNullOrWhiteSpace(request.Note) || string.IsNullOrWhiteSpace(request.IdempotencyKey)) throw new ArgumentException("Cần loại chi phí, ghi chú và mã yêu cầu.");
        var project = await RequiredProject(id);
        var existingCost = project.Costs.FirstOrDefault(c => c.IdempotencyKey == request.IdempotencyKey);
        if (existingCost != null)
        {
            if (existingCost.Kind != request.Kind || existingCost.Amount != request.Amount || existingCost.Note != request.Note)
                throw new InvalidOperationException("Mã ghi nhận chi phí đã được dùng cho nội dung khác.");
            return project;
        }
        if (project.State is not ("Pending" or "InProgress" or "Rework")) throw new InvalidOperationException("Chi phí đã chốt khi gửi kiểm định.");
        project.Costs.Add(new() { IdempotencyKey = request.IdempotencyKey, Kind = request.Kind, Amount = request.Amount, Note = request.Note, ActorId = actor });
        await store.SaveAsync(project); return project;
    });

    public Task<QualityInspection> InspectProjectAsync(string id, QualityDecisionRequest request, string actor) => transactions.ExecuteAsync(async () =>
    {
        await Operator(actor); var project = await RequiredProject(id);
        if (project.State != "QualityCheck") throw new InvalidOperationException("Dự án chưa ở bước QC.");
        if (string.IsNullOrWhiteSpace(request.Note)) throw new ArgumentException("Cần ghi nhận kết quả QC.");
        ValidateImages(request.Images, false);
        var inspection = new QualityInspection { ProjectId = id, InspectorId = actor, Passed = request.Passed, Note = request.Note, Images = request.Images.ToList() };
        await store.InsertAsync(inspection); project.State = request.Passed ? "QCApproved" : "Rework";
        project.Timeline.Add(new() { From = "QualityCheck", To = project.State, ActorId = actor, Note = request.Note });
        await store.SaveAsync(project); return inspection;
    });

    public Task<Product> PublishProjectAsync(string id, PublishProjectRequest request, string actor) => transactions.ExecuteAsync(async () =>
    {
        await Operator(actor); var project = await RequiredProject(id);
        if (project.ProductId != null) return (await products.GetByIdAsync(project.ProductId))!;
        if (project.State != "QCApproved") throw new InvalidOperationException("Chỉ đăng bán thành phẩm đã được QC chấp nhận.");
        await Catalog(request.BrandId, request.CategoryId); Money(request.SellingPrice); ValidateImages(project.AfterImages, true);
        if (string.IsNullOrWhiteSpace(request.Title)) throw new ArgumentException("Cần tên sản phẩm.");
        var product = new Product { Title = request.Title.Trim(), Description = request.Description, BrandId = request.BrandId, CategoryId = request.CategoryId,
            Price = request.SellingPrice, ImageUrls = project.AfterImages.ToList(), IsUnique = true, StockQuantity = 1, ProductType = "Upcycled",
            UpcyclingProjectId = id, AcquisitionCost = project.AcquisitionCost, ProductionCost = project.Costs.Sum(c => c.Amount), HasCostBasis = true,
            SourceInventoryIds = project.InventoryIds.ToList(), Size = request.Size, Color = request.Color };
        product.Slug = "rewear-" + product.Id; await products.CreateAsync(product);
        foreach (var itemId in project.InventoryIds)
        {
            var item = (await store.GetAsync<InventoryItem>(itemId))!; item.ProductId = product.Id;
            await Move(item, "Consumed", actor, "Nguyên liệu đã tạo thành sản phẩm được QC duyệt.", id);
            var source = await sourcing.GetByIdAsync(item.SourcingId);
            if (source != null) { source.Status = SourcingStatus.Completed; source.ProductId = product.Id; source.CompletedAt = DateTime.UtcNow; await sourcing.UpdateAsync(source); }
        }
        project.State = "Completed"; project.ProductId = product.Id;
        var finished = new InventoryItem { Id = product.Id, SourcingId = "project:" + project.Id, Title = product.Title,
            State = "ReadyRetail", Custody = "Warehouse", ProjectId = project.Id, ProductId = product.Id, AcquisitionCost = product.AcquisitionCost,
            Images = product.ImageUrls.ToList() };
        await store.InsertAsync(finished);
        await Movement(finished, null, "ReadyRetail", actor, "Thành phẩm QC đạt được nhập kho bán lẻ.", project.Id);
        project.Timeline.Add(new() { From = "QCApproved", To = "Completed", ActorId = actor, Note = "Đã tạo sản phẩm bán lẻ." });
        await store.SaveAsync(project); return product;
    });

    public Task<QualityInspection> InspectInventoryAsync(string id, QualityDecisionRequest request, string actor) => transactions.ExecuteAsync(async () =>
    {
        await Operator(actor);
        var item = await store.GetAsync<InventoryItem>(id) ?? throw new InvalidOperationException("Không tìm thấy món đồ trong kho.");
        if (item.State is not ("AvailableRaw" or "QCApproved" or "Rework"))
            throw new InvalidOperationException("Món đồ không ở bước chờ nghiệm thu QC.");
        if (string.IsNullOrWhiteSpace(request.Note)) throw new ArgumentException("Cần ghi nhận kết quả QC.");
        ValidateImages(request.Images, false);
        var inspection = new QualityInspection
        {
            ProjectId = "inventory:" + id,
            InspectorId = actor,
            Passed = request.Passed,
            Note = request.Note,
            Images = request.Images.ToList()
        };
        await store.InsertAsync(inspection);
        if (request.Images.Count > 0) item.Images = request.Images.ToList();
        var nextState = request.Passed ? "QCApproved" : "Rework";
        await Move(item, nextState, actor, request.Note, id);
        return inspection;
    });

    public Task<Product> PublishResaleAsync(string id, PublishResaleRequest request, string actor) => transactions.ExecuteAsync(async () =>
    {
        await Operator(actor); Money(request.SellingPrice); var source = await Source(id);
        if (source.ProductId != null) return (await products.GetByIdAsync(source.ProductId))!;
        if (source.Status != SourcingStatus.Approved || source.RequiresRevisedAcceptance) throw new InvalidOperationException("Chưa hoàn tất kiểm định và đồng ý giá.");
        var payout = await store.GetAsync<PurchasePayout>(id) ?? throw new InvalidOperationException("Cần xác nhận chi trả trước khi đăng bán.");
        var item = await store.GetAsync<InventoryItem>(id) ?? throw new InvalidOperationException("Chưa nhập kho.");
        if (item.State is not ("AvailableRaw" or "QCApproved")) throw new InvalidOperationException("Món đồ đã được cấp cho quy trình khác hoặc chưa sẵn sàng.");
        
        var brandId = !string.IsNullOrWhiteSpace(request.BrandId) ? request.BrandId : source.BrandId;
        var categoryId = !string.IsNullOrWhiteSpace(request.CategoryId) ? request.CategoryId : source.CategoryId;
        await Catalog(brandId, categoryId);

        var title = !string.IsNullOrWhiteSpace(request.Title) ? request.Title.Trim() : source.Title;
        var description = request.Description ?? source.Description ?? "";
        var condition = request.Condition ?? source.InspectedCondition ?? source.DeclaredCondition;
        var color = !string.IsNullOrWhiteSpace(request.Color) ? request.Color : source.Color;
        var size = !string.IsNullOrWhiteSpace(request.Size) ? request.Size : source.Size;
        var images = (request.Images != null && request.Images.Count > 0) ? request.Images : (item.Images.Count > 0 ? item.Images : source.ImageUrls.ToList());

        var product = new Product
        {
            Title = title,
            Description = description,
            BrandId = brandId,
            CategoryId = categoryId,
            Price = request.SellingPrice,
            Condition = condition,
            Color = color,
            Size = size,
            ImageUrls = images,
            IsUnique = true,
            StockQuantity = 1,
            ProductType = "SecondHand",
            SourceSourcingId = id,
            SourceInventoryIds = new() { item.Id },
            AcquisitionCost = payout.Amount,
            HasCostBasis = true
        };
        product.Slug = "rewear-" + product.Id;
        await products.CreateAsync(product);
        item.ProductId = product.Id;
        await Move(item, "ReadyRetail", actor, "Đăng bán món đồ đã qua kiểm định QC và làm sạch hoàn thiện.", product.Id);
        source.ProductId = product.Id;
        source.Status = SourcingStatus.Completed;
        source.CompletedAt = DateTime.UtcNow;
        await sourcing.UpdateAsync(source);
        return product;
    });

    public Task<Product> PublishResaleAsync(string id, decimal price, string actor) =>
        PublishResaleAsync(id, new PublishResaleRequest { SellingPrice = price }, actor);

    private async Task<SourcingRequest> Source(string id) => await sourcing.GetByIdAsync(id) ?? throw new InvalidOperationException("Không tìm thấy yêu cầu thu mua.");
    private async Task<UpcyclingProject> RequiredProject(string id) => await store.GetAsync<UpcyclingProject>(id) ?? throw new InvalidOperationException("Không tìm thấy dự án.");
    private async Task Operator(string id)
    { var user = string.IsNullOrWhiteSpace(id) ? null : await users.GetByIdAsync(id); if (user?.Role is not (UserRole.Admin or UserRole.Staff)) throw new UnauthorizedAccessException("Cần quyền nhân viên vận hành."); }
    private async Task Catalog(string brandId, string categoryId)
    { if ((await brands.GetByIdAsync(brandId))?.IsActive != true || (await categories.GetByIdAsync(categoryId))?.IsActive != true) throw new ArgumentException("Thương hiệu hoặc danh mục không hoạt động."); }
    private static void Money(decimal value) { if (value <= 0 || decimal.Truncate(value) != value) throw new ArgumentException("Số tiền phải là số nguyên VND lớn hơn 0."); }
    private static void ValidateImages(List<string> images, bool required)
    { if (images.Count > 5 || (required && images.Count == 0) || images.Any(i => !Uri.TryCreate(i, UriKind.Absolute, out var url) || url.Scheme != "https")) throw new ArgumentException("Ảnh cần từ 1–5 URL HTTPS hợp lệ."); }
    private async Task Move(InventoryItem item, string state, string actor, string reason, string related)
    {
        var from = item.State; item.State = state;
        if (state == "Consumed") { item.Custody = "Transformed"; item.Location = "Đã chuyển thành thành phẩm"; }
        if (state == "ReturnedToSender") { item.Custody = "Sender"; item.Location = "Đã trả người gửi"; }
        await store.SaveAsync(item); await Movement(item, from, state, actor, reason, related);
    }
    private Task Movement(InventoryItem item, string? from, string state, string actor, string reason, string related) => store.InsertAsync(new InventoryMovement
        { InventoryId = item.Id, FromState = from, ToState = state, ActorId = actor, Reason = reason, RelatedId = related });
}
