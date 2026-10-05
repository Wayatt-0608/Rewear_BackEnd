using MongoDB.Driver;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Repositories;

/// <summary>
/// Repository cho Payment - implementation với MongoDB (Task 8).
/// </summary>
public class PaymentRepository : IPaymentRepository
{
    private readonly IMongoCollection<Payment> _payments;

    public PaymentRepository(MongoDbContext context)
    {
        _payments = context.Payments;
    }

    public async Task CreateAsync(Payment payment)
    {
        await _payments.InsertOneAsync(payment);
    }

    public async Task UpdateAsync(Payment payment)
    {
        payment.UpdatedAt = DateTime.UtcNow;
        await _payments.ReplaceOneAsync(p => p.Id == payment.Id, payment);
    }

    public async Task<Payment?> GetLatestByOrderIdAsync(string orderId)
    {
        return await _payments
            .Find(p => p.OrderId == orderId)
            .SortByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Payment>> GetByOrderIdAsync(string orderId)
    {
        return await _payments
            .Find(p => p.OrderId == orderId)
            .SortByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<Payment?> GetByPaymentLinkIdAsync(string paymentLinkId)
    {
        return await _payments
            .Find(p => p.PaymentLinkId == paymentLinkId)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Payment>> GetExpiredUnpaidAsync(int limit)
    {
        // Chỉ lấy phiên đang Pending và đã quá hạn. Phiên Paid/Failed/Expired
        // không cần xử lý lại (và cũng tránh trả kho 2 lần).
        var filter = Builders<Payment>.Filter.And(
            Builders<Payment>.Filter.Eq(p => p.Status, PaymentStatus.Pending),
            Builders<Payment>.Filter.Lt(p => p.ExpiresAt, DateTime.UtcNow)
        );

        return await _payments
            .Find(filter)
            .SortBy(p => p.ExpiresAt)
            .Limit(limit)
            .ToListAsync();
    }

    public async Task<long> CountByStatusAsync(PaymentStatus status)
    {
        return await _payments.CountDocumentsAsync(p => p.Status == status);
    }
}