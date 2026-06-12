using PlanningPoker.Core;

namespace PlanningPoker.Core.Tests.Fakes;

/// <summary>Deterministic clock for tests. Advance with <see cref="Advance"/>.</summary>
public sealed class TestClock : IClock
{
    public TestClock(DateTimeOffset? start = null) =>
        UtcNow = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow { get; private set; }

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>Short-code generator returning a fixed queue of codes for predictable tests.</summary>
public sealed class StubShortCodeGenerator : IShortCodeGenerator
{
    private readonly Queue<string> _codes;

    public StubShortCodeGenerator(params string[] codes) => _codes = new Queue<string>(codes);

    public string Generate() => _codes.Count > 0 ? _codes.Dequeue() : $"code-{Guid.NewGuid():N}"[..12];
}
