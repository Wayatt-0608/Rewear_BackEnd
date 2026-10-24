using REWEAR.Application.Interfaces;

namespace REWEAR.Infrastructure.Persistence;

public sealed class MongoCommerceUnitOfWork(MongoDbContext context) : ICommerceUnitOfWork
{
    public Task<T> ExecuteAsync<T>(Func<Task<T>> operation) => context.ExecuteTransactionAsync(operation);
}
