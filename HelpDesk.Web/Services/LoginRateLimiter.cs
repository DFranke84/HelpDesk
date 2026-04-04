using System.Collections.Concurrent;

namespace HelpDesk.Web.Services;

/// <summary>
/// In-memory rate limiter that blocks login attempts after repeated failures.
/// Registered as singleton; state does not survive app restarts (acceptable trade-off
/// for simplicity — persistent lockout would require a database column).
/// </summary>
public sealed class LoginRateLimiter
{
    private readonly int _maxAttempts;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, LoginAttemptRecord> _records = new(StringComparer.OrdinalIgnoreCase);

    public LoginRateLimiter(IConfiguration configuration)
    {
        _maxAttempts = configuration.GetValue("App:LoginRateLimit:MaxAttempts", 5);
        _window = TimeSpan.FromMinutes(configuration.GetValue("App:LoginRateLimit:WindowMinutes", 15));
    }

    public bool IsBlocked(string loginId, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!_records.TryGetValue(loginId, out var record))
            return false;

        lock (record)
        {
            record.Prune(_window);
            if (record.Attempts.Count < _maxAttempts)
                return false;

            var oldest = record.Attempts[0];
            var unblockAt = oldest + _window;
            var remaining = unblockAt - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                record.Attempts.Clear();
                return false;
            }

            retryAfter = remaining;
            return true;
        }
    }

    public void RecordFailure(string loginId)
    {
        var record = _records.GetOrAdd(loginId, _ => new LoginAttemptRecord());
        lock (record)
        {
            record.Prune(_window);
            record.Attempts.Add(DateTime.UtcNow);
        }
    }

    public void RecordSuccess(string loginId)
    {
        _records.TryRemove(loginId, out _);
    }

    private sealed class LoginAttemptRecord
    {
        public List<DateTime> Attempts { get; } = new();

        public void Prune(TimeSpan window)
        {
            var cutoff = DateTime.UtcNow - window;
            Attempts.RemoveAll(t => t < cutoff);
        }
    }
}
