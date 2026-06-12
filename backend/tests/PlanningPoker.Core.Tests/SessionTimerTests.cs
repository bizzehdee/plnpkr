using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using PlanningPoker.Core.Tests.Fakes;
using Xunit;

namespace PlanningPoker.Core.Tests;

/// <summary>Behaviour of the organiser-controlled round timer (#14).</summary>
public class SessionTimerTests
{
    private const string Code = "blue-fox-42";
    private readonly FakeSessionStore _store = new();
    private readonly TestClock _clock = new();
    private readonly SessionService _sut;

    public SessionTimerTests()
    {
        _sut = new SessionService(_store, new StubShortCodeGenerator(Code), _clock);
    }

    private Task<CreateSessionResult> CreateAsync(int? timerSeconds) =>
        _sut.CreateAsync(new CreateSessionRequest(
            "Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, null, true, timerSeconds));

    [Fact]
    public async Task The_configured_duration_is_set_at_creation_and_clamped()
    {
        var result = await CreateAsync(99999); // above the max → clamped

        result.Session!.TimerDurationSeconds.Should().Be(SessionService.MaxTimerSeconds);
        result.Session.TimerDeadline.Should().BeNull(); // configured, not started
    }

    [Fact]
    public async Task Creation_without_a_timer_leaves_it_unconfigured()
    {
        var result = await CreateAsync(null);

        result.Session!.TimerDurationSeconds.Should().BeNull();
    }

    [Fact]
    public async Task Starting_the_timer_sets_a_deadline_from_the_clock()
    {
        await CreateAsync(60);

        var result = await _sut.StartTimerAsync(Code, "alice", 90);

        result.Status.Should().Be(SessionActionStatus.Ok);
        result.Session!.TimerDurationSeconds.Should().Be(90); // start also updates the configured duration
        result.Session.TimerDeadline.Should().Be(_clock.UtcNow.AddSeconds(90));
        result.Session.TimerPausedRemainingSeconds.Should().BeNull();
    }

    [Fact]
    public async Task Starting_without_a_duration_reuses_the_configured_one()
    {
        await CreateAsync(45);

        var result = await _sut.StartTimerAsync(Code, "alice", null);

        result.Session!.TimerDeadline.Should().Be(_clock.UtcNow.AddSeconds(45));
    }

    [Fact]
    public async Task Pausing_freezes_the_remaining_time_and_resuming_restores_a_deadline()
    {
        await CreateAsync(60);
        await _sut.StartTimerAsync(Code, "alice", 60);

        _clock.Advance(TimeSpan.FromSeconds(20));
        var paused = await _sut.PauseTimerAsync(Code, "alice");

        paused.Session!.TimerDeadline.Should().BeNull();
        paused.Session.TimerPausedRemainingSeconds.Should().Be(40);

        var resumed = await _sut.ResumeTimerAsync(Code, "alice");
        resumed.Session!.TimerPausedRemainingSeconds.Should().BeNull();
        resumed.Session.TimerDeadline.Should().Be(_clock.UtcNow.AddSeconds(40));
    }

    [Fact]
    public async Task Stopping_clears_the_timer_but_keeps_the_configured_duration()
    {
        await CreateAsync(60);
        await _sut.StartTimerAsync(Code, "alice", 60);

        var stopped = await _sut.StopTimerAsync(Code, "alice");

        stopped.Session!.TimerDeadline.Should().BeNull();
        stopped.Session.TimerPausedRemainingSeconds.Should().BeNull();
        stopped.Session.TimerDurationSeconds.Should().Be(60);
    }

    [Fact]
    public async Task The_duration_can_be_changed_mid_session_without_starting()
    {
        await CreateAsync(60);

        var result = await _sut.SetTimerDurationAsync(Code, "alice", 120);

        result.Session!.TimerDurationSeconds.Should().Be(120);
        result.Session.TimerDeadline.Should().BeNull();
    }

    [Fact]
    public async Task A_non_organiser_cannot_control_the_timer()
    {
        await CreateAsync(60);
        await _sut.JoinAsync(new JoinSessionRequest(Code, "bob", "Bob", ParticipantRole.Voter, null));

        (await _sut.StartTimerAsync(Code, "bob", 60)).Status.Should().Be(SessionActionStatus.NotOrganiser);
        (await _sut.PauseTimerAsync(Code, "bob")).Status.Should().Be(SessionActionStatus.NotOrganiser);
        (await _sut.SetTimerDurationAsync(Code, "bob", 30)).Status.Should().Be(SessionActionStatus.NotOrganiser);
    }

    [Fact]
    public async Task Revealing_stops_a_running_timer()
    {
        await CreateAsync(60);
        await _sut.StartTimerAsync(Code, "alice", 60);

        var revealed = await _sut.RevealAsync(Code, "alice");

        revealed.Session!.State.Should().Be(SessionState.Revealed);
        revealed.Session.TimerDeadline.Should().BeNull();
    }

    [Fact]
    public async Task Resetting_the_round_clears_the_timer()
    {
        await CreateAsync(60);
        await _sut.StartTimerAsync(Code, "alice", 60);

        var reset = await _sut.ResetRoundAsync(Code, "alice");

        reset.Session!.TimerDeadline.Should().BeNull();
        reset.Session.TimerPausedRemainingSeconds.Should().BeNull();
    }

    // --- Server-side expiry (via the maintenance sweep) ---

    [Fact]
    public async Task A_due_timer_force_reveals_the_session()
    {
        var maintenance = new SessionMaintenanceService(_store, _clock);
        await CreateAsync(30);
        await _sut.StartTimerAsync(Code, "alice", 30);

        _clock.Advance(TimeSpan.FromSeconds(31)); // past the deadline
        var revealed = await maintenance.ExpireDueRoundTimersAsync();

        revealed.Should().ContainSingle();
        revealed[0].ShortCode.Should().Be(Code);
        revealed[0].State.Should().Be(SessionState.Revealed);
        revealed[0].TimerDeadline.Should().BeNull();
    }

    [Fact]
    public async Task A_timer_that_has_not_yet_expired_is_left_running()
    {
        var maintenance = new SessionMaintenanceService(_store, _clock);
        await CreateAsync(60);
        await _sut.StartTimerAsync(Code, "alice", 60);

        _clock.Advance(TimeSpan.FromSeconds(30)); // still time left
        var revealed = await maintenance.ExpireDueRoundTimersAsync();

        revealed.Should().BeEmpty();
        (await _sut.GetByShortCodeAsync(Code))!.State.Should().Be(SessionState.Voting);
    }

    [Fact]
    public async Task A_paused_timer_does_not_expire()
    {
        var maintenance = new SessionMaintenanceService(_store, _clock);
        await CreateAsync(30);
        await _sut.StartTimerAsync(Code, "alice", 30);
        await _sut.PauseTimerAsync(Code, "alice");

        _clock.Advance(TimeSpan.FromMinutes(5)); // long past the original deadline, but paused
        var revealed = await maintenance.ExpireDueRoundTimersAsync();

        revealed.Should().BeEmpty();
        (await _sut.GetByShortCodeAsync(Code))!.State.Should().Be(SessionState.Voting);
    }
}
