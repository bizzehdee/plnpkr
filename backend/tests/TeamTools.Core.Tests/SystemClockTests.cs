using FluentAssertions;
using TeamTools.Core;
using Xunit;

namespace TeamTools.Core.Tests;

// M1 smoke test: proves the test harness, FluentAssertions, and Core reference all work.
// Real behaviour suites land alongside the logic in M2+ (see #16).
public class SystemClockTests
{
    [Fact]
    public void UtcNow_returns_a_current_utc_timestamp()
    {
        var before = DateTimeOffset.UtcNow;

        var now = new SystemClock().UtcNow;

        now.Should().BeOnOrAfter(before);
        now.Offset.Should().Be(TimeSpan.Zero);
    }
}
