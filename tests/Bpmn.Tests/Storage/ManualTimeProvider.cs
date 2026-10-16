namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Storage;

/// <summary>A clock that only moves when a test moves it.</summary>
/// <param name="now">The starting time.</param>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    /// <param name="by">How far.</param>
    public void Advance(TimeSpan by) => _now += by;
}
