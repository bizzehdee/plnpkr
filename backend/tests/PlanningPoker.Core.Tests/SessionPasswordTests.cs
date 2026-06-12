using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Models;
using PlanningPoker.Core.Security;
using PlanningPoker.Core.Tests.Fakes;
using Xunit;

namespace PlanningPoker.Core.Tests;

/// <summary>Behaviour of the optional session password (#2).</summary>
public class SessionPasswordTests
{
    private const string Code = "blue-fox-42";
    private readonly FakeSessionStore _store = new();
    private readonly TestClock _clock = new();
    private readonly SessionService _sut;

    public SessionPasswordTests()
    {
        _sut = new SessionService(_store, new StubShortCodeGenerator(Code), _clock);
    }

    private Task CreateAsync(string? password) =>
        _sut.CreateAsync(new CreateSessionRequest("Sprint", DeckType.Fibonacci, null, "alice", "Alice", true, password));

    private Task<JoinResult> JoinAsync(string userId, string name, string? password) =>
        _sut.JoinAsync(new JoinSessionRequest(Code, userId, name, ParticipantRole.Voter, password));

    [Fact]
    public async Task Joining_a_protected_session_without_a_password_is_rejected()
    {
        await CreateAsync("hunter2");

        var result = await JoinAsync("bob", "Bob", null);

        result.Status.Should().Be(JoinStatus.PasswordRequired);
        result.Session.Should().BeNull();
    }

    [Fact]
    public async Task Joining_with_the_wrong_password_is_rejected()
    {
        await CreateAsync("hunter2");

        var result = await JoinAsync("bob", "Bob", "nope");

        result.Status.Should().Be(JoinStatus.WrongPassword);
    }

    [Fact]
    public async Task Joining_with_the_correct_password_succeeds()
    {
        await CreateAsync("hunter2");

        var result = await JoinAsync("bob", "Bob", "hunter2");

        result.Status.Should().Be(JoinStatus.Ok);
        result.Session!.Participants.Select(p => p.DisplayName).Should().Contain("Bob");
    }

    [Fact]
    public async Task A_session_without_a_password_lets_anyone_join()
    {
        await CreateAsync(null);

        var result = await JoinAsync("bob", "Bob", null);

        result.Status.Should().Be(JoinStatus.Ok);
    }

    [Fact]
    public async Task An_existing_participant_reconnecting_is_not_re_challenged()
    {
        await CreateAsync("hunter2");
        (await JoinAsync("bob", "Bob", "hunter2")).Status.Should().Be(JoinStatus.Ok);

        // Auto-rejoin on reconnect sends no password — already-present participants pass the gate.
        var rejoin = await JoinAsync("bob", "Bob", null);

        rejoin.Status.Should().Be(JoinStatus.Ok);
    }

    [Fact]
    public async Task Landing_reports_whether_a_password_is_required()
    {
        await CreateAsync("hunter2");

        var landing = await _sut.GetLandingAsync(Code);

        landing!.RequiresPassword.Should().BeTrue();
        landing.Name.Should().Be("Sprint");
    }

    [Fact]
    public async Task Landing_reports_no_password_when_none_is_set()
    {
        await CreateAsync(null);

        (await _sut.GetLandingAsync(Code))!.RequiresPassword.Should().BeFalse();
    }

    [Fact]
    public async Task Organiser_can_add_a_password_later()
    {
        await CreateAsync(null);

        var set = await _sut.SetPasswordAsync(Code, "alice", "newpass");

        set.Status.Should().Be(SessionActionStatus.Ok);
        (await JoinAsync("bob", "Bob", null)).Status.Should().Be(JoinStatus.PasswordRequired);
        (await JoinAsync("bob", "Bob", "newpass")).Status.Should().Be(JoinStatus.Ok);
    }

    [Fact]
    public async Task Organiser_can_clear_the_password()
    {
        await CreateAsync("hunter2");

        var cleared = await _sut.SetPasswordAsync(Code, "alice", null);

        cleared.Status.Should().Be(SessionActionStatus.Ok);
        (await _sut.GetLandingAsync(Code))!.RequiresPassword.Should().BeFalse();
        (await JoinAsync("bob", "Bob", null)).Status.Should().Be(JoinStatus.Ok);
    }

    [Fact]
    public async Task A_non_organiser_cannot_change_the_password()
    {
        await CreateAsync(null);
        await JoinAsync("bob", "Bob", null);

        var result = await _sut.SetPasswordAsync(Code, "bob", "sneaky");

        result.Status.Should().Be(SessionActionStatus.NotOrganiser);
    }

    [Fact]
    public async Task The_stored_hash_is_never_the_plaintext()
    {
        await CreateAsync("hunter2");

        var session = await _store.FindByShortCodeAsync(Code);

        session!.PasswordHash.Should().NotBeNull().And.NotContain("hunter2");
    }
}

/// <summary>Behaviour of the PBKDF2 password hasher (#2).</summary>
public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_then_verify_round_trips()
    {
        var hash = _hasher.Hash("correct horse");

        _hasher.Verify(hash, "correct horse").Should().BeTrue();
    }

    [Fact]
    public void Verify_rejects_the_wrong_password()
    {
        var hash = _hasher.Hash("correct horse");

        _hasher.Verify(hash, "battery staple").Should().BeFalse();
    }

    [Fact]
    public void Each_hash_uses_a_fresh_salt()
    {
        _hasher.Hash("same").Should().NotBe(_hasher.Hash("same"));
    }

    [Fact]
    public void Verify_rejects_a_malformed_encoded_hash()
    {
        _hasher.Verify("not-a-valid-hash", "whatever").Should().BeFalse();
    }
}
