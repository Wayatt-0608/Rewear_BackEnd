using MongoDB.Driver;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Infrastructure.Persistence;

namespace REWEAR.Infrastructure.Repositories;

public sealed class LifecycleStore(MongoDbContext context) : ILifecycleStore
{
    private TransactionalCollection<T> Collection<T>() where T : LifecycleRecord => context.Collection<T>(typeof(T).Name + "s");
    public async Task<T?> GetAsync<T>(string id) where T : LifecycleRecord => await Collection<T>().Find(r => r.Id == id).FirstOrDefaultAsync();
    public Task<List<T>> ListAsync<T>() where T : LifecycleRecord => Collection<T>().Find(_ => true).SortByDescending(r => r.CreatedAt).ToListAsync();
    public Task<List<T>> FindAsync<T>(System.Linq.Expressions.Expression<Func<T, bool>> filter) where T : LifecycleRecord => Collection<T>().Find(filter).ToListAsync();
    public Task InsertAsync<T>(T record) where T : LifecycleRecord => Collection<T>().InsertOneAsync(record);
    public async Task SaveAsync<T>(T record) where T : LifecycleRecord
    {
        var version = record.Version; record.Version++; record.UpdatedAt = DateTime.UtcNow;
        if ((await Collection<T>().ReplaceOneAsync(r => r.Id == record.Id && r.Version == version, record)).MatchedCount == 0)
            throw new InvalidOperationException("Dữ liệu đã thay đổi, vui lòng tải lại.");
    }
}
