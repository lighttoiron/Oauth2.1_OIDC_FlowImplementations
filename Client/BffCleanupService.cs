public class BffCleanupService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.UtcNow;
            
            foreach (var kvp in BffStore.LoginAttempts)
            {
                if (kvp.Value.ExpiresAt < now)
                {
                    BffStore.LoginAttempts.TryRemove(kvp.Key, out _);
                }
            }

            foreach (var kvp in BffStore.Sessions)
            {
                if (kvp.Value.ExpiresAt < now)
                {
                    BffStore.Sessions.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}