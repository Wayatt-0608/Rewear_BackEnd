using REWEAR.Application.DTOs;
namespace REWEAR.Application.Interfaces;
public interface IOperationsQueries
{
    Task<OperationsOrderPage> OrdersAsync(OperationsOrderQuery query);
    Task<OperationsOrderDetail?> OrderAsync(string id);
    Task<OperationsMetrics> MetricsAsync(DateTime from, DateTime to);
}
