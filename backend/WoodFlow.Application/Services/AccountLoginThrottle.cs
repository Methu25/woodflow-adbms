using System.Security.Cryptography;
using System.Text;

namespace WoodFlow.Application.Services;

// Single-instance deployment: bounded memory, no raw usernames/passwords, no database account lockouts.
// A token bucket permits a burst of 10 attempts per normalized username, then one every 6 seconds.
public sealed class AccountLoginThrottle(TimeProvider clock)
{
    private const int Capacity = 10, MaximumAccounts = 5000;
    private const double RefillSeconds = 6;
    private readonly Dictionary<string, Bucket> _buckets = [];
    private readonly object _gate = new();
    private sealed class Bucket(DateTimeOffset now)
    {
        public double Tokens = Capacity;
        public DateTimeOffset Updated = now;
    }

    // Zero means admitted. Rejected attempts cannot extend the refill delay.
    public int RetryAfterSeconds(string username)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(username.Trim().ToUpperInvariant())));
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (!_buckets.TryGetValue(key, out var bucket))
            {
                if (_buckets.Count >= MaximumAccounts)
                    foreach (var expired in _buckets.Where(p => now-p.Value.Updated >= TimeSpan.FromMinutes(10)).Select(p => p.Key).ToArray())
                        _buckets.Remove(expired);
                // Do not evict active buckets; attackers cannot rotate usernames to reset a victim's limit.
                if (_buckets.Count >= MaximumAccounts) return 60;
                _buckets[key] = bucket = new(now);
            }
            bucket.Tokens = Math.Min(Capacity, bucket.Tokens + Math.Max(0, (now-bucket.Updated).TotalSeconds)/RefillSeconds);
            bucket.Updated = now;
            if (bucket.Tokens < 1) return Math.Max(1, (int)Math.Ceiling((1-bucket.Tokens)*RefillSeconds));
            bucket.Tokens--;
            return 0;
        }
    }
}
