using FluentAssertions;
using TeamTools.Api.Hubs;
using TeamTools.Core;
using Xunit;

namespace TeamTools.Api.Tests;

/// <summary>Per-connection create/join throttle (#3-abuse).</summary>
public class HubThrottleTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public void Create_is_allowed_up_to_the_limit_then_blocked()
    {
        var clock = new FakeClock();
        var throttle = new HubThrottle(clock, new HubThrottle.Options { CreatePerMinute = 3 });

        throttle.TryCreate("conn").Should().BeTrue();
        throttle.TryCreate("conn").Should().BeTrue();
        throttle.TryCreate("conn").Should().BeTrue();
        throttle.TryCreate("conn").Should().BeFalse("the 4th create within the window is over the limit");
    }

    [Fact]
    public void The_window_slides_so_attempts_recover_after_a_minute()
    {
        var clock = new FakeClock();
        var throttle = new HubThrottle(clock, new HubThrottle.Options { CreatePerMinute = 1 });

        throttle.TryCreate("conn").Should().BeTrue();
        throttle.TryCreate("conn").Should().BeFalse();

        clock.UtcNow = clock.UtcNow.AddSeconds(61); // window elapsed
        throttle.TryCreate("conn").Should().BeTrue();
    }

    [Fact]
    public void Limits_are_per_connection_and_per_action()
    {
        var throttle = new HubThrottle(new FakeClock(), new HubThrottle.Options { CreatePerMinute = 1, JoinPerMinute = 1 });

        throttle.TryCreate("a").Should().BeTrue();
        throttle.TryCreate("b").Should().BeTrue("a different connection has its own window");
        // Joining is a separate bucket from creating for the same connection.
        throttle.TryJoin("a").Should().BeTrue();
    }

    [Fact]
    public void Forget_resets_a_connections_windows()
    {
        var throttle = new HubThrottle(new FakeClock(), new HubThrottle.Options { CreatePerMinute = 1 });

        throttle.TryCreate("conn").Should().BeTrue();
        throttle.TryCreate("conn").Should().BeFalse();

        throttle.Forget("conn");
        throttle.TryCreate("conn").Should().BeTrue("the window was cleared on disconnect");
    }
}
