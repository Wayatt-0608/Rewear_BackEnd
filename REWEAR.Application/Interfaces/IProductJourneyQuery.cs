using REWEAR.Application.DTOs;
namespace REWEAR.Application.Interfaces;
public interface IProductJourneyQuery
{
    Task<ProductJourneyResponse?> GetAsync(string productId);
}
