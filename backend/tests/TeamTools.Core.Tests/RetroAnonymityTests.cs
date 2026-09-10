using System.Text.Json;
using FluentAssertions;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Retro;
using TeamTools.Core.Tests.Fakes;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>
/// Anonymity (#22) is a property of the **wire**, not of the UI, so these tests assert on what the
/// snapshot carries — including by serializing it, because "the client doesn't render it" is not
/// anonymity. Psychological safety is the whole point of a retro; a hide that a devtools panel
/// disproves is worse than no promise at all.
/// </summary>
public class RetroAnonymityTests
{
    private readonly FakeRoomStore _store = new();
    private readonly TestClock _clock = new();
    private readonly RetroService _sut;

    public RetroAnonymityTests()
    {
        var rooms = TestServices.Rooms(_store, new StubShortCodeGenerator(Code), _clock);
        _sut = new RetroService(_store, rooms, _clock);
    }

    private const string Code = "blue-fox-42";
    private const string Facilitator = "alice";
    private const string Bob = "bob";

    private async Task<RetroBoardSnapshot> SeedAsync(bool anonymous)
    {
        await _sut.CreateAsync(new CreateRetroRequest(
            "Retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true,
            Anonymous: anonymous));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));
        return (await _sut.GetByShortCodeAsync(Code, Facilitator))!;
    }

    private async Task<RetroBoardSnapshot> BoardAsync(string forUserId) =>
        (await _sut.GetByShortCodeAsync(Code, forUserId))!;

    // --- The guarantee -----------------------------------------------------

    [Fact]
    public async Task An_anonymous_board_sends_no_authorship_to_other_participants()
    {
        var board = await SeedAsync(anonymous: true);
        await _sut.AddCardAsync(Code, Bob, board.Columns[0].Id, "I felt rushed");

        var asSeenByAlice = (await BoardAsync(Facilitator)).Columns[0].Cards[0];

        asSeenByAlice.AuthorUserId.Should().BeNull();
        asSeenByAlice.AuthorDisplayName.Should().BeNull();
        asSeenByAlice.IsMine.Should().BeFalse();
        asSeenByAlice.Text.Should().Be("I felt rushed", "the card itself is still the point");
    }

    [Fact]
    public async Task An_anonymous_board_sends_no_authorship_even_to_the_author()
    {
        // "Null for everyone but you" would still put a userId on the wire. IsMine carries
        // ownership instead, so there is no field left to leak.
        var board = await SeedAsync(anonymous: true);
        await _sut.AddCardAsync(Code, Bob, board.Columns[0].Id, "I felt rushed");

        var asSeenByBob = (await BoardAsync(Bob)).Columns[0].Cards[0];

        asSeenByBob.AuthorUserId.Should().BeNull();
        asSeenByBob.AuthorDisplayName.Should().BeNull();
        asSeenByBob.IsMine.Should().BeTrue("the author still needs to know which card to edit");
    }

    [Fact]
    public async Task No_serialized_card_on_an_anonymous_board_names_its_author()
    {
        // The strongest form of the assertion: look at the bytes that would go over the wire.
        //
        // Scoped to the cards on purpose. The participant list still names everyone in the room,
        // and it must — people can see who joined the call. Anonymity is about which card belongs
        // to whom, so the cards are where the guarantee has to hold.
        var board = await SeedAsync(anonymous: true);
        await _sut.AddCardAsync(Code, Bob, board.Columns[0].Id, "I felt rushed");

        foreach (var viewer in new[] { Facilitator, Bob, "carol" })
        {
            var cardsJson = JsonSerializer.Serialize((await BoardAsync(viewer)).Columns);

            cardsJson.Should().Contain("I felt rushed", "the card's content is still delivered");
            cardsJson.Should().NotContain(Bob, $"cards sent to '{viewer}' must not name the author");
            cardsJson.Should().NotContain("Bob");
        }
    }

    [Fact]
    public async Task An_attributed_board_still_names_authors()
    {
        // Anonymity is opt-in; the default retro shows who said what.
        var board = await SeedAsync(anonymous: false);
        await _sut.AddCardAsync(Code, Bob, board.Columns[0].Id, "Deploys got faster");

        var card = (await BoardAsync(Facilitator)).Columns[0].Cards[0];

        card.AuthorUserId.Should().Be(Bob);
        card.AuthorDisplayName.Should().Be("Bob");
    }

    // --- Editing still works under anonymity -------------------------------

    [Fact]
    public async Task An_author_can_still_edit_and_delete_their_own_anonymous_card()
    {
        var board = await SeedAsync(anonymous: true);
        var added = await _sut.AddCardAsync(Code, Bob, board.Columns[0].Id, "typo");
        var cardId = added.Board!.Columns[0].Cards[0].Id;

        var edited = await _sut.EditCardAsync(Code, Bob, cardId, "fixed");
        edited.Status.Should().Be(RetroActionStatus.Ok);

        var deleted = await _sut.DeleteCardAsync(Code, Bob, cardId);
        deleted.Status.Should().Be(RetroActionStatus.Ok);
    }

    [Fact]
    public async Task Someone_else_still_cannot_edit_an_anonymous_card()
    {
        // Authorship is hidden from participants, not from the server's authorization check.
        var board = await SeedAsync(anonymous: true);
        await _sut.JoinAsync(new JoinSessionRequest(Code, "carol", "Carol", ParticipantRole.Voter));
        var added = await _sut.AddCardAsync(Code, Bob, board.Columns[0].Id, "not yours");
        var cardId = added.Board!.Columns[0].Cards[0].Id;

        var result = await _sut.EditCardAsync(Code, "carol", cardId, "rewritten");

        result.Status.Should().Be(RetroActionStatus.NotCardAuthor);
    }

    [Fact]
    public async Task A_facilitator_can_still_moderate_an_anonymous_card()
    {
        var board = await SeedAsync(anonymous: true);
        var added = await _sut.AddCardAsync(Code, Bob, board.Columns[0].Id, "off topic");
        var cardId = added.Board!.Columns[0].Cards[0].Id;

        var result = await _sut.DeleteCardAsync(Code, Facilitator, cardId);

        result.Status.Should().Be(RetroActionStatus.Ok);
    }

    // --- The lock ----------------------------------------------------------

    [Fact]
    public async Task A_facilitator_can_switch_anonymity_while_the_board_is_empty()
    {
        await SeedAsync(anonymous: false);

        var result = await _sut.SetAnonymousAsync(Code, Facilitator, true);

        result.Status.Should().Be(RetroActionStatus.Ok);
        result.Board!.Anonymous.Should().BeTrue();
    }

    [Fact]
    public async Task Turning_anonymity_off_is_refused_once_cards_exist()
    {
        // The decisive case: cards were written under a promise of anonymity, and nothing in the
        // product may retroactively break it — not even the facilitator.
        var board = await SeedAsync(anonymous: true);
        await _sut.AddCardAsync(Code, Bob, board.Columns[0].Id, "said in confidence");

        var result = await _sut.SetAnonymousAsync(Code, Facilitator, false);

        result.Status.Should().Be(RetroActionStatus.AnonymityLocked);
        (await BoardAsync(Facilitator)).Columns[0].Cards[0].AuthorUserId.Should().BeNull();
    }

    [Fact]
    public async Task Turning_anonymity_on_is_also_refused_once_cards_exist()
    {
        // The mirror image: it would retroactively hide attributed cards, changing what people
        // agreed to when they wrote them.
        var board = await SeedAsync(anonymous: false);
        await _sut.AddCardAsync(Code, Bob, board.Columns[0].Id, "happy to own this");

        var result = await _sut.SetAnonymousAsync(Code, Facilitator, true);

        result.Status.Should().Be(RetroActionStatus.AnonymityLocked);
        (await BoardAsync(Facilitator)).Columns[0].Cards[0].AuthorUserId.Should().Be(Bob);
    }

    [Fact]
    public async Task Setting_anonymity_to_its_current_value_is_a_no_op_even_with_cards()
    {
        var board = await SeedAsync(anonymous: true);
        await _sut.AddCardAsync(Code, Bob, board.Columns[0].Id, "already anonymous");

        var result = await _sut.SetAnonymousAsync(Code, Facilitator, true);

        result.Status.Should().Be(RetroActionStatus.Ok);
    }

    [Fact]
    public async Task The_snapshot_says_whether_anonymity_can_still_be_changed()
    {
        // So the UI can disable the toggle with an explanation instead of offering a control that
        // fails when used.
        var board = await SeedAsync(anonymous: false);
        board.CanChangeAnonymity.Should().BeTrue();

        await _sut.AddCardAsync(Code, Bob, board.Columns[0].Id, "first card");

        (await BoardAsync(Facilitator)).CanChangeAnonymity.Should().BeFalse();
    }

    [Fact]
    public async Task A_participant_cannot_change_anonymity()
    {
        await SeedAsync(anonymous: false);

        var result = await _sut.SetAnonymousAsync(Code, Bob, true);

        result.Status.Should().Be(RetroActionStatus.NotOrganiser);
    }

    [Fact]
    public async Task Anonymity_cannot_be_changed_on_a_closed_board()
    {
        await SeedAsync(anonymous: false);
        await _sut.CloseBoardAsync(Code, Facilitator);

        var result = await _sut.SetAnonymousAsync(Code, Facilitator, true);

        result.Status.Should().Be(RetroActionStatus.BoardClosed);
    }
}
