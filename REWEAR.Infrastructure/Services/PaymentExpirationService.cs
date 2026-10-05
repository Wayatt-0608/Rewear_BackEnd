using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using REWEAR.Application.Interfaces;

namespace REWEAR.Infrastructure.Services;

/// <summary>
/// Background service đóng các phiên thanh toán quá hạn (Task 8).
/// </summary>
/// <remarks>
/// REWEAR giữ chỗ sản phẩm 15 phút cho khách thanh toán. Nếu không có service này,
/// sản phẩm sẽ bị kẹt ở trạng thái Reserved vô thời gian và không ai mua được.
/// Chạy mỗi 60 giây — độ trễ tối đa 1 phút so với mốc hết hạn, chấp nhận được.
/// </remarks>
public class PaymentExpirationService : BackgroundService
{
    private static readonly TimeSpan SCAN_INTERVAL = TimeSpan.FromSeconds(60);
    private const int BATCH_SIZE = 100;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentExpirationService> _logger;

    public PaymentExpirationService(
        IServiceProvider serviceProvider,
        ILogger<PaymentExpirationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Chờ 1 phút trước lần quét đầu tiên: vừa để app khởi động xong,
        // vừa tránh quét khi database chưa sẵn sàng.
        _logger.LogInformation("PaymentExpirationService bat dau chay, quet moi {Minutes} phut.",
            SCAN_INTERVAL.TotalMinutes);

        using var timer = new PeriodicTimer(SCAN_INTERVAL);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    // Tạo scope mới mỗi vòng lặp: các service đăng ký theo
                    // Scoped nên không dùng lại được sau khi scope đã dispose.
                    using var scope = _serviceProvider.CreateScope();
                    var paymentService = scope.ServiceProvider
                        .GetRequiredService<IPaymentService>();

                    await paymentService.ExpireStalePaymentsAsync(BATCH_SIZE);
                }
                catch (Exception ex)
                {
                    // Không để lỗi một vòng quét làm sập cả service.
                    _logger.LogError(ex, "Loi khi quet va dong phien thanh toan het han.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ứng dụng đang tắt -> thoát bình thường.
        }

        _logger.LogInformation("PaymentExpirationService da dung.");
    }
}