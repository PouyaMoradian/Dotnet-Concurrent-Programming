# `TimeProvider` (.NET 8)

A virtualisation seam for time. Inject a `TimeProvider` instead of calling `DateTimeOffset.UtcNow` / `Task.Delay` directly. In production, use `TimeProvider.System`. In tests, use `Microsoft.Extensions.Time.Testing.FakeTimeProvider`.

## The API

```csharp
abstract class TimeProvider
{
    public static TimeProvider System { get; }                    // wall clock

    public abstract DateTimeOffset GetUtcNow();
    public DateTimeOffset GetLocalNow();
    public virtual long GetTimestamp();                            // for stopwatch-style
    public virtual ITimer CreateTimer(TimerCallback, object?, TimeSpan due, TimeSpan period);
    public virtual TimeZoneInfo LocalTimeZone { get; }
}
```

Plus extension methods on `Task`:

```csharp
Task.Delay(TimeSpan, TimeProvider, CancellationToken);
Task.WaitAsync(Task, TimeSpan, TimeProvider, CancellationToken);
```

## Production usage

```csharp
public sealed class TokenService(TimeProvider time)
{
    public bool IsExpired(DateTimeOffset issuedAt, TimeSpan ttl)
        => time.GetUtcNow() > issuedAt + ttl;
}

services.AddSingleton(TimeProvider.System);          // register once
```

## Test usage

```csharp
[Fact]
public async Task TokenExpires()
{
    var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    var svc = new TokenService(time);
    var issued = time.GetUtcNow();
    Assert.False(svc.IsExpired(issued, TimeSpan.FromMinutes(5)));

    time.Advance(TimeSpan.FromMinutes(6));
    Assert.True(svc.IsExpired(issued, TimeSpan.FromMinutes(5)));
}
```

`FakeTimeProvider` lives in `Microsoft.Extensions.Time.Testing`. It also fires registered timers when you `Advance` past their due time — letting you test scheduling behaviour deterministically:

```csharp
var fake = new FakeTimeProvider();
using var timer = fake.CreateTimer(_ => fired = true, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
fake.Advance(TimeSpan.FromSeconds(15));   // fires now
Assert.True(fired);
```

## What about `Task.Delay`?

```csharp
await Task.Delay(TimeSpan.FromSeconds(5), time);    // honours the TimeProvider
```

In production: real delay. In tests with `FakeTimeProvider`: completes when you `Advance`. This makes tests of timeouts and rate limiters fast and deterministic.

## When to retrofit

- **Rate limiters / token buckets** — they need a clock; injecting `TimeProvider` makes them testable.
- **Scheduled jobs** — `Quartz`/`Hangfire` have their own; for hand-rolled, `TimeProvider` is the clean way.
- **Caches with TTL** — same.
- **Token validation / JWT expiry** — same.

## When not to bother

- **Logs / telemetry timestamps** — production uses `TimeProvider.System` anyway; tests don't care.
- **High-frequency hot paths** — `TimeProvider.GetTimestamp()` is fine but adds a virtual call. For monotonic time in hot paths, `Stopwatch.GetTimestamp()` is direct and ~5 ns.

## Migration

If your code calls `DateTimeOffset.UtcNow` from a class, the migration is:

1. Add `TimeProvider time` to the constructor.
2. Replace `DateTimeOffset.UtcNow` with `time.GetUtcNow()`.
3. Replace `Task.Delay(ts)` with `Task.Delay(ts, time)`.
4. In DI, register `TimeProvider.System` as a singleton.

Tests now have a clean injection point.
