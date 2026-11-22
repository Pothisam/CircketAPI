using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Cricket_Services.NotificationServices;

/// <summary>Runs notification work after the HTTP response so scoring a ball never waits on FCM.</summary>
public interface INotificationDispatcher
{
    void Enqueue(Func<IServiceProvider, Task> work);
}

public class NotificationDispatcher : BackgroundService, INotificationDispatcher
{
    private readonly Channel<Func<IServiceProvider, Task>> _queue =
        Channel.CreateBounded<Func<IServiceProvider, Task>>(new BoundedChannelOptions(5000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(IServiceScopeFactory scopeFactory, ILogger<NotificationDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Enqueue(Func<IServiceProvider, Task> work) => _queue.Writer.TryWrite(work);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await work(scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification work item failed");
            }
        }
    }
}
