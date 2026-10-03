namespace DeBliss.UnitTests.TestDoubles;

/// <summary>
/// Clock the tests can move forward, so audit timestamps can be asserted without
/// sleeping or accepting flaky "did it change?" checks.
/// </summary>
internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
