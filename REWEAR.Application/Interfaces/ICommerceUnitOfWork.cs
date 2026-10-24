namespace REWEAR.Application.Interfaces;

/// <summary>All database writes in a business operation commit or roll back together.</summary>
public interface ICommerceUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation);
}
