using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using REWEAR.Application.Interfaces;

namespace REWEAR.Infrastructure.Services;

/// <summary>
/// Background service disable voucher đã hết hạn (Task 10).
/// </summary>
/// <remarks>
/// Mỗi giờ quét 1 lần, tìm các voucher:
/// - Đang IsActive = true
/// - Đã quá ExpiryDate
/// → Set IsActive = false để user không dùng được nữa.
/// (FE vẫn thấy voucher trong kho nhưng với status = "expired".)
/// </remarks>
public class VoucherExpirationService : BackgroundService
{
    private static readonly TimeSpan SCAN_INTERVAL = TimeSpan.FromHours(1);
    private const int BATCH_SIZE = 200;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VoucherExpirationService> _logger;

    public VoucherExpirationService(
        IServiceProvider serviceProvider,
        ILogger<VoucherExpirationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "VoucherExpirationService bat dau chay, quet moi {Hours} gio.",
            SCAN_INTERVAL.TotalHours);

        // Đợi 1 phút trước lần quét đầu tiên (cho app khởi động xong).
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        using var timer = new PeriodicTimer(SCAN_INTERVAL);

        try
        {
            do
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var voucherService = scope.ServiceProvider
                        .GetRequiredService<IVoucherService>();

                    int count = await voucherService.DisableExpiredVouchersAsync();

                    if (count > 0)
                    {
                        _logger.LogInformation(
                            "VoucherExpirationService: da tat {Count} voucher het han.", count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "VoucherExpirationService: loi khi quet.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // App đang tắt → thoát bình thường.
        }

        _logger.LogInformation("VoucherExpirationService da dung.");
    }
}
