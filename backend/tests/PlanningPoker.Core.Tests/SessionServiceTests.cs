using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using PlanningPoker.Core.Tests.Fakes;
using Xunit;

namespace PlanningPoker.Core.Tests;

public class SessionServiceTests
{
    private readonly FakeSessionStore _store = new();
    private readonly TestClock _clock = new();
    private readonly SessionService _sut;

    public SessionServiceTests()
    {
        _sut = new SessionService(_store, new StubShortCodeGenerator("blue-fox-42", "red-owl-99"), _clock);
    }

    private CreateSessionRequest CreateReq(
        string name = "Sprint 24",
        DeckType deck = DeckType.Fibonacci,
        string? custom = null,
        string creatorId = "creator-1",
        string creatorName = "Alice",
        bool organise = true) =>
        new(name, deck, custom, creatorId, creatorName, organise);

    // --- Create ------------------------------------------------------------

    [Fact]
    public async Task Create_returns_snapshot_with_short_code_deck_and_creator()
    {
        var result = await _sut.CreateAsync(CreateReq());

        result.Status.Should().Be(CreateSessionStatus.Ok);
        result.Session!.ShortCode.Should().Be("blue-fox-42");
        result.Session.Name.Should().Be("Sprint 24");
        result.Session.Cards.Should().ContainInOrder("0", "1", "2", "3", "5").And.ContainInOrder("?", "☕");
        result.Session.Participants.Should().ContainSingle()
            .Which.DisplayName.Should().Be("Alice");
    }

    [Fact]
    public async Task Create_with_organise_marks_creator_as_organiser_and_records_organiser_id()
    {
        var result = await _sut.CreateAsync(CreateReq(creatorId: "creator-1", organise: true));

        result.Session!.OrganiserUserId.Should().Be("creator-1");
        result.Session.Participants.Single().IsOrganiser.Should().BeTrue();
    }

    [Fact]
    public async Task Create_without_organise_has_no_organiser()
    {
        var result = await _sut.CreateAsync(CreateReq(organise: false));

        result.Session!.OrganiserUserId.Should().BeNull();
        result.Session.Participants.Single().IsOrganiser.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_with_blank_session_name_is_rejected(string name)
    {
        var result = await _sut.CreateAsync(CreateReq(name: name));

        result.Status.Should().Be(CreateSessionStatus.InvalidName);
        result.Session.Should().BeNull();
    }

    [Fact]
    public async Task Create_with_blank_creator_name_is_rejected()
    {
        var result = await _sut.CreateAsync(CreateReq(creatorName: "  "));

        result.Status.Should().Be(CreateSessionStatus.InvalidName);
    }

    [Fact]
    public async Task Create_with_empty_custom_deck_is_rejected()
    {
        var result = await _sut.CreateAsync(CreateReq(deck: DeckType.Custom, custom: " , "));

        result.Status.Should().Be(CreateSessionStatus.InvalidDeck);
    }

    [Fact]
    public async Task Create_skips_short_codes_already_in_use()
    {
        // First session takes "blue-fox-42"; second must skip it and take "red-owl-99".
        await _sut.CreateAsync(CreateReq(creatorId: "c1", creatorName: "Alice"));
        var second = await _sut.CreateAsync(CreateReq(creatorId: "c2", creatorName: "Bob"));

        second.Session!.ShortCode.Should().Be("red-owl-99");
    }

    // --- Join --------------------------------------------------------------

    private async Task<string> SeedSessionAsync()
    {
        var created = await _sut.CreateAsync(CreateReq(creatorId: "creator-1", creatorName: "Alice"));
        return created.Session!.ShortCode;
    }

    [Fact]
    public async Task Join_adds_a_new_participant_and_returns_full_snapshot()
    {
        var code = await SeedSessionAsync();

        var result = await _sut.JoinAsync(new JoinSessionRequest(code, "user-2", "Bob", ParticipantRole.Voter));

        result.Status.Should().Be(JoinStatus.Ok);
        result.Participant!.DisplayName.Should().Be("Bob");
        result.Session!.Participants.Select(p => p.DisplayName).Should().Equal("Alice", "Bob");
    }

    [Fact]
    public async Task Join_unknown_short_code_returns_not_found()
    {
        var result = await _sut.JoinAsync(new JoinSessionRequest("nope-nope-1", "user-2", "Bob", ParticipantRole.Voter));

        result.Status.Should().Be(JoinStatus.SessionNotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Join_with_blank_name_is_rejected(string name)
    {
        var code = await SeedSessionAsync();

        var result = await _sut.JoinAsync(new JoinSessionRequest(code, "user-2", name, ParticipantRole.Voter));

        result.Status.Should().Be(JoinStatus.InvalidName);
    }

    [Theory]
    [InlineData("Alice")]   // exact
    [InlineData("alice")]   // case
    [InlineData("  Alice ")] // whitespace
    public async Task Join_with_name_taken_by_another_user_is_rejected(string name)
    {
        var code = await SeedSessionAsync(); // creator is "Alice"

        var result = await _sut.JoinAsync(new JoinSessionRequest(code, "user-2", name, ParticipantRole.Voter));

        result.Status.Should().Be(JoinStatus.NameTaken);
    }

    [Fact]
    public async Task Rejoining_with_same_userId_keeps_own_name_and_does_not_duplicate()
    {
        var code = await SeedSessionAsync(); // creator "Alice" = creator-1

        // The creator reconnects, sending their own name again — must NOT be rejected as taken.
        var result = await _sut.JoinAsync(new JoinSessionRequest(code, "creator-1", "Alice", ParticipantRole.Voter));

        result.Status.Should().Be(JoinStatus.Ok);
        result.Session!.Participants.Should().ContainSingle();
    }

    [Fact]
    public async Task Rejoining_can_change_own_display_name()
    {
        var code = await SeedSessionAsync();
        await _sut.JoinAsync(new JoinSessionRequest(code, "user-2", "Bob", ParticipantRole.Voter));

        var result = await _sut.JoinAsync(new JoinSessionRequest(code, "user-2", "Bobby", ParticipantRole.Voter));

        result.Status.Should().Be(JoinStatus.Ok);
        result.Session!.Participants.Select(p => p.DisplayName).Should().Equal("Alice", "Bobby");
    }

    [Fact]
    public async Task Join_translates_concurrent_duplicate_into_name_taken()
    {
        var code = await SeedSessionAsync();
        _store.ThrowDuplicateOnNextUpdate = true;

        var result = await _sut.JoinAsync(new JoinSessionRequest(code, "user-2", "Charlie", ParticipantRole.Voter));

        result.Status.Should().Be(JoinStatus.NameTaken);
    }

    [Fact]
    public async Task Join_updates_last_activity()
    {
        var code = await SeedSessionAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));

        await _sut.JoinAsync(new JoinSessionRequest(code, "user-2", "Bob", ParticipantRole.Voter));

        var session = await _store.FindByShortCodeAsync(code);
        session!.LastActivityAt.Should().Be(_clock.UtcNow);
    }

    // --- Leave -------------------------------------------------------------

    [Fact]
    public async Task Leave_removes_the_participant()
    {
        var code = await SeedSessionAsync();
        await _sut.JoinAsync(new JoinSessionRequest(code, "user-2", "Bob", ParticipantRole.Voter));

        var result = await _sut.LeaveAsync(code, "user-2");

        result.Status.Should().Be(LeaveStatus.Ok);
        result.Session!.Participants.Select(p => p.DisplayName).Should().Equal("Alice");
    }

    [Fact]
    public async Task Leave_unknown_session_returns_not_found()
    {
        var result = await _sut.LeaveAsync("nope-nope-1", "user-2");

        result.Status.Should().Be(LeaveStatus.SessionNotFound);
    }

    [Fact]
    public async Task Leave_when_not_a_member_returns_not_in_session()
    {
        var code = await SeedSessionAsync();

        var result = await _sut.LeaveAsync(code, "ghost");

        result.Status.Should().Be(LeaveStatus.NotInSession);
    }

    // --- Get ---------------------------------------------------------------

    [Fact]
    public async Task GetByShortCode_returns_snapshot_when_present_else_null()
    {
        var code = await SeedSessionAsync();

        (await _sut.GetByShortCodeAsync(code)).Should().NotBeNull();
        (await _sut.GetByShortCodeAsync("missing-code-1")).Should().BeNull();
    }
}
