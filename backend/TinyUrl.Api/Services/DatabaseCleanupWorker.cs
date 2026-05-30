using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TinyUrl.Api.Data;

namespace TinyUrl.Api.Services
{
    public class DatabaseCleanupWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DatabaseCleanupWorker> _logger;
        // Periodic cleanup interval: 1 hour (3600 seconds)
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1);

        public DatabaseCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<DatabaseCleanupWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Database Cleanup Background Hosted Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Delay for 1 hour before performing periodic cleanup
                    await Task.Delay(_cleanupInterval, stoppingToken);

                    _logger.LogInformation("Background worker executing database cleanup trigger at: {Time}", DateTime.UtcNow);

                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        
                        // Count active entries before deletion
                        int countBefore = await context.ShortUrls.CountAsync(stoppingToken);
                        _logger.LogInformation("Background worker: Active short URL database records found: {Count}", countBefore);

                        if (countBefore > 0)
                        {
                            await context.ShortUrls.ExecuteDeleteAsync(stoppingToken);
                            _logger.LogInformation("Database cleared successfully by background service. Deleted {Count} short URL mappings.", countBefore);
                        }
                        else
                        {
                            _logger.LogInformation("Database is already empty. No cleanup required.");
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    _logger.LogInformation("Database Cleanup Background Hosted Service has been cancelled/stopped.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred in the Database Cleanup Background Hosted Service.");
                }
            }
        }
    }
}
