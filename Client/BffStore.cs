using System.Collections.Concurrent;

// A set of concurrent dicionaries storing active login attempts and user sessions
static class BffStore
{
    public static readonly ConcurrentDictionary<string, LoginAttempt> LoginAttempts = new();
    public static readonly ConcurrentDictionary<string, BffSession> Sessions = new();
}