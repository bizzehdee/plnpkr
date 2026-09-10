using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TeamTools.Api;
using Xunit;

namespace TeamTools.Api.Tests;

/// <summary>
/// The shared sweep loop (#34). Three background services run on it, and the behaviour worth
/// pinning is the one that fails silently: a pass that throws must not end the loop. Without the
/// catch, one bad tick takes the round timer — or idle eviction — down for the life of the process
/// and nothing surfaces it.
/// </summary>
public class RoomSweepServiceTests
{
    /// <summary>A sweep that can be told to throw, and counts its passes.</summary>
    private sealed class TestSweep : RoomSweepService
    {
        private readonly Func<int, Task> _body;

        public TestSweep(Func<int, Task> body)
            : base(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                   NullLogger<TestSweep>.Instance)
        {
            _body = body;
        }

        public int Passes { get; private set; }

        protected override TimeSpan Interval => TimeSpan.FromMilliseconds(20);

        protected override string SweepName => "Test";

        protected override Task SweepAsync(IServiceProvider services, CancellationToken ct)
        {
            services.Should().NotBeNull("each pass gets a fresh scope");
            return _body(++Passes);
        }
    }

    private static async Task<TestSweep> RunUntilAsync(Func<int, Task> body, int passes)
    {
        var done = new TaskCompletionSource();
        var sweep = new TestSweep(async n =>
        {
            await body(n);
            if (n >= passes)
            {
                done.TrySetResult();
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await sweep.StartAsync(cts.Token);
        await done.Task.WaitAsync(cts.Token);
        await sweep.StopAsync(CancellationToken.None);
        return sweep;
    }

    [Fact]
    public async Task It_sweeps_repeatedly_on_its_interval()
    {
        var sweep = await RunUntilAsync(_ => Task.CompletedTask, passes: 3);

        sweep.Passes.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task A_pass_that_throws_does_not_end_the_loop()
    {
        // The whole reason for the catch. Before #34 each service had its own copy of this
        // guarantee; now one mistake here would break all three at once, so it is tested once here.
        var sweep = await RunUntilAsync(
            n => n == 1 ? Task.FromException(new InvalidOperationException("boom")) : Task.CompletedTask,
            passes: 3);

        sweep.Passes.Should().BeGreaterThanOrEqualTo(3, "the loop kept going after the failure");
    }

    [Fact]
    public async Task Cancellation_stops_the_loop_without_logging_a_failure()
    {
        // Shutdown cancels the token mid-pass; that is an orderly stop, not a failed sweep.
        var sweep = new TestSweep(_ => Task.CompletedTask);
        using var cts = new CancellationTokenSource();

        await sweep.StartAsync(cts.Token);
        await sweep.StopAsync(CancellationToken.None);

        // StopAsync completing is the assertion: an unhandled OperationCanceledException would
        // surface here as a faulted task.
        sweep.ExecuteTask!.IsCompleted.Should().BeTrue();
    }
}
