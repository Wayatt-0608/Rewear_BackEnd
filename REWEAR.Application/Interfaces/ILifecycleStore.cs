using REWEAR.Domain.Entities;

namespace REWEAR.Application.Interfaces;

public interface ILifecycleStore
{
    Task<T?> GetAsync<T>(string id) where T : LifecycleRecord;
    Task<List<T>> ListAsync<T>() where T : LifecycleRecord;
    Task<List<T>> FindAsync<T>(System.Linq.Expressions.Expression<Func<T, bool>> filter) where T : LifecycleRecord;
    Task InsertAsync<T>(T record) where T : LifecycleRecord;
    Task SaveAsync<T>(T record) where T : LifecycleRecord;
}
