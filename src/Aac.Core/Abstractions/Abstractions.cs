namespace Aac.Core.Abstractions;

/// <summary>Time source. Everything time-dependent (cooldowns, debounce) reads this so tests are deterministic.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}

/// <summary>Manually advanced clock for tests and simulations.</summary>
public sealed class ManualClock : IClock
{
    public ManualClock(DateTimeOffset? start = null) => Now = start ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    public DateTimeOffset Now { get; private set; }
    public void Advance(TimeSpan by) => Now += by;
    public void AdvanceMs(double ms) => Now += TimeSpan.FromMilliseconds(ms);
}

/// <summary>
/// Encrypts data at rest. The Windows app uses DPAPI (current user scope); tests use a pass-through.
/// </summary>
public interface IDataProtector
{
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] cipher);
}

/// <summary>No encryption. Only for tests and non-Windows tooling.</summary>
public sealed class PassThroughProtector : IDataProtector
{
    public byte[] Protect(byte[] plain) => plain;
    public byte[] Unprotect(byte[] cipher) => cipher;
}
