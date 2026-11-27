using System.Collections.Concurrent;

namespace CarTracker.Api.Services;

public class LoginGuardOptions
{
    /// <summary>Failed logins / invite codes from one IP within <see cref="WindowMinutes"/> before it gets blocked.</summary>
    public int MaxFailures { get; set; } = 20;
    public int WindowMinutes { get; set; } = 60;
    public int BanHours { get; set; } = 24;
}

/// <summary>
/// fail2ban-style protection on top of the per-account lockout and the per-minute rate limit: an IP that keeps
/// failing (wrong passwords, unknown emails, invalid invite codes) is blocked from login and registration for a day.
/// In memory: a restart forgets bans, which is fine for a handful of users behind Cloudflare.
/// </summary>
public class LoginGuard(LoginGuardOptions options, TimeProvider time, ILogger<LoginGuard> log)
{
    private sealed class Entry
    {
        public readonly Queue<DateTimeOffset> Failures = new();
        public DateTimeOffset? BannedUntil;
    }

    private readonly ConcurrentDictionary<string, Entry> ips = new();
    private const int MaxTracked = 10_000;

    public bool IsBlocked(string ip)
    {
        if (!ips.TryGetValue(ip, out var e)) return false;
        lock (e) return e.BannedUntil > time.GetUtcNow();
    }

    /// <param name="what">For the log, e.g. "login for x@y.de" or "invite code".</param>
    public void Failure(string ip, string what)
    {
        var now = time.GetUtcNow();
        if (ips.Count >= MaxTracked) Prune(now);
        var e = ips.GetOrAdd(ip, _ => new Entry());
        lock (e)
        {
            var windowStart = now.AddMinutes(-options.WindowMinutes);
            while (e.Failures.TryPeek(out var t) && t < windowStart) e.Failures.Dequeue();
            e.Failures.Enqueue(now);
            log.LogWarning("Failed {What} from {Ip} ({Count} in the last {Window} min)", what, ip, e.Failures.Count, options.WindowMinutes);
            if (e.Failures.Count >= options.MaxFailures && !(e.BannedUntil > now))
            {
                e.BannedUntil = now.AddHours(options.BanHours);
                e.Failures.Clear();
                log.LogWarning("Blocked {Ip} from login and registration until {Until:u} after {Max} failed attempts", ip, e.BannedUntil, options.MaxFailures);
            }
        }
    }

    /// <summary>A successful login clears the IP's failures (people sharing a home connection mistype too).</summary>
    public void Success(string ip)
    {
        if (!ips.TryGetValue(ip, out var e)) return;
        lock (e)
        {
            if (!(e.BannedUntil > time.GetUtcNow())) ips.TryRemove(ip, out _);
        }
    }

    private void Prune(DateTimeOffset now)
    {
        var windowStart = now.AddMinutes(-options.WindowMinutes);
        foreach (var (ip, e) in ips)
        {
            lock (e)
            {
                var stale = !(e.BannedUntil > now) && (!e.Failures.TryPeek(out _) || e.Failures.Last() < windowStart);
                if (stale) ips.TryRemove(ip, out _);
            }
        }
    }
}
