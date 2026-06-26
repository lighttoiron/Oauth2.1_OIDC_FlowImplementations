using System.Collections.Concurrent;

static class BffStore
{
    public static readonly ConcurrentDictionary<string, LoginAttempt> LoginAttempts = new();
    public static readonly ConcurrentDictionary<string, BffSession> Sessions = new();
}