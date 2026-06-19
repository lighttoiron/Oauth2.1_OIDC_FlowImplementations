using System;

public class PendingRequestCleanupService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    // This function is called automatically by the app since it was added to hosted services in Program.cs
    // This stoppingToken is passed in automatically by the app when it runs and gets signalled when the app shuts down, cleanly exiting the loop.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // PeriodicTimer is the modern, recommended way to run "do this every N seconds" tasks.
        // It handles cancellation cleanly via WaitForNextTickAsync returning false on shutdown
        using var timer = new PeriodicTimer(SweepInterval); // the using keyword will call .Dispose() on this timer when it goes out of scope, shutting it down immediately

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.UtcNow;

            foreach (var kvp in AuthorizeEndpoint.PendingRequests)
            {
                if (kvp.Value.ExpiresAt < now)
                {
                    Console.WriteLine($"~~~~~~~~Removing {kvp.Value}");
                    AuthorizeEndpoint.PendingRequests.TryRemove(kvp.Key, out _); // use _ because we do not actually need the param later
                }
            }

            foreach (var kvp in AuthorizeEndpoint.AuthCodes)
            {
                if (kvp.Value.ExpiresAt < now)
                {
                    Console.WriteLine($"~~~~~~~~~~~~~~Removing {kvp.Value}");
                    AuthorizeEndpoint.AuthCodes.TryRemove(kvp.Key, out _);                
                }
            }
        }
    }
}