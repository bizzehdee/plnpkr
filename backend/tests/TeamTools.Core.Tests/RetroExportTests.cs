using System.Text.Json;
using FluentAssertions;
using TeamTools.Core.Contracts;
using TeamTools.Core.Models;
using TeamTools.Core.Retro;
using TeamTools.Core.Tests.Fakes;
using Xunit;

namespace TeamTools.Core.Tests;

/// <summary>
/// Retro export (#28). A retro's output belongs in the team's wiki or ticket tracker, not in a room
/// that retention will eventually delete (#15) — so the export has to carry everything.
/// <para>
/// The guarantee that matters most here is a negative one: an anonymous board's export must not
/// name a single card author, in any format, ever. Several tests below exist only to hold that.
/// </para>
/// </summary>
public class RetroExportTests
{
    private readonly FakeRoomStore _store = new();
    private readonly TestClock _clock = new();
    private readonly RetroService _sut;

    public RetroExportTests()
    {
        var rooms = TestServices.Rooms(_store, new StubShortCodeGenerator(Code), _clock);
        _sut = new RetroService(_store, rooms, _clock);
    }

    private const string Code = "blue-fox-42";
    private const string Facilitator = "alice";
    private const string Bob = "bob";
    private static readonly DateTimeOffset Due = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A retro that looks finished: a card from Bob and one from Alice, Bob's gathered into a theme
    /// and given a dot, one action owned by Alice, and the board in Discuss where export opens up.
    /// <para>
    /// <b>Bob authors a card and owns nothing.</b> That is what lets the anonymity tests below
    /// simply assert that the string "Bob" appears nowhere: action owners are named on purpose
    /// (#26), so an owner called Bob would make the leak check meaningless.
    /// </para>
    /// </summary>
    private async Task SeedAsync(bool anonymous = false, string? password = null)
    {
        await _sut.CreateAsync(new CreateRetroRequest(
            "Sprint 24 retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice",
            Organise: true, Password: password, Anonymous: anonymous));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter, password));

        var columnId = (await _sut.GetByShortCodeAsync(Code, Facilitator))!.Columns[0].Id;
        var added = await _sut.AddCardAsync(Code, Bob, columnId, "CI is slow");
        var bobsCard = added.Board!.Columns[0].Cards[0].Id;
        await _sut.AddCardAsync(Code, Facilitator, columnId, "standups run long");

        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Group
        await _sut.GroupCardsAsync(Code, Facilitator, [bobsCard], null);
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Vote
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, bobsCard);
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Discuss
        await _sut.AddActionAsync(Code, Facilitator, "Speed up CI", Facilitator, null, Due, null);
    }

    /// <summary>Creates a board, optionally with the given cards, and walks it to Discuss.</summary>
    private async Task SeedToDiscussAsync(string name = "Quiet retro", params string[] cards)
    {
        await _sut.CreateAsync(new CreateRetroRequest(
            name, RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true));

        if (cards.Length > 0)
        {
            var columnId = (await _sut.GetByShortCodeAsync(Code, Facilitator))!.Columns[0].Id;
            foreach (var text in cards)
            {
                await _sut.AddCardAsync(Code, Facilitator, columnId, text);
            }
        }

        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Group
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Vote
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Discuss
    }

    private async Task<RetroExport> ExportAsync(string? password = null)
    {
        var (status, export) = await _sut.GetExportAsync(Code, password);
        status.Should().Be(RetroExportStatus.Ok);
        return export!;
    }

    // --- Contents ----------------------------------------------------------

    [Fact]
    public async Task The_export_carries_the_board_its_themes_cards_and_actions()
    {
        await SeedAsync();

        var export = await ExportAsync();

        export.ShortCode.Should().Be(Code);
        export.Name.Should().Be("Sprint 24 retro");
        export.Phase.Should().Be(RetroPhase.Discuss);
        export.Themes.Should().ContainSingle()
            .Which.Cards.Should().ContainSingle().Which.Text.Should().Be("CI is slow");
        export.LooseCards.Should().ContainSingle().Which.Text.Should().Be("standups run long");
        export.Actions.Should().ContainSingle().Which.Title.Should().Be("Speed up CI");
    }

    [Fact]
    public async Task A_card_carries_the_column_it_was_written_in()
    {
        // The column *is* the sentiment on a retro board — "CI is slow" under Mad and the same
        // words under Glad are different statements.
        await SeedAsync();

        var export = await ExportAsync();

        export.Themes[0].Cards[0].Column.Should().Be("Mad");
    }

    [Fact]
    public async Task A_theme_carries_its_dot_tally_and_its_label()
    {
        await SeedAsync();

        var export = await ExportAsync();

        export.Themes[0].Dots.Should().Be(1, "a theme's dots include the dots on its cards");
        export.Themes[0].Label.Should().Be("CI is slow", "the label was seeded from the card");
    }

    [Fact]
    public async Task Themes_come_out_highest_voted_first()
    {
        // The export is read as an agenda, so it has to arrive in the order the team would work it.
        await _sut.CreateAsync(new CreateRetroRequest(
            "Retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true));
        await _sut.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));
        var columnId = (await _sut.GetByShortCodeAsync(Code, Facilitator))!.Columns[0].Id;
        await _sut.AddCardAsync(Code, Facilitator, columnId, "one dot");
        await _sut.AddCardAsync(Code, Facilitator, columnId, "two dots");

        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Group
        var board = (await _sut.GetByShortCodeAsync(Code, Facilitator))!;
        var cards = board.Columns[0].Cards.ToDictionary(c => c.Text, c => c.Id);
        await _sut.GroupCardsAsync(Code, Facilitator, [cards["one dot"]], null);
        await _sut.GroupCardsAsync(Code, Facilitator, [cards["two dots"]], null);

        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Vote
        await _sut.CastVoteAsync(Code, Facilitator, RetroVoteTarget.Card, cards["one dot"]);
        await _sut.CastVoteAsync(Code, Facilitator, RetroVoteTarget.Card, cards["two dots"]);
        await _sut.CastVoteAsync(Code, Bob, RetroVoteTarget.Card, cards["two dots"]);
        await _sut.AdvancePhaseAsync(Code, Facilitator); // → Discuss

        var export = await ExportAsync();

        export.Themes.Select(t => t.Label).Should().Equal("two dots", "one dot");
        export.Themes.Select(t => t.Dots).Should().Equal(2, 1);
    }

    [Fact]
    public async Task An_action_carries_its_owner_due_date_and_done_state()
    {
        await SeedAsync();

        var action = (await ExportAsync()).Actions[0];

        action.Owner.Should().Be("Alice");
        action.DueDate.Should().Be(Due);
        action.Done.Should().BeFalse();
        action.CarriedOver.Should().BeFalse();
    }

    [Fact]
    public async Task Actions_still_outstanding_come_before_the_ones_already_done()
    {
        await SeedAsync();
        await _sut.AddActionAsync(Code, Facilitator, "Book the room", null, null, null, null);
        var board = (await _sut.GetByShortCodeAsync(Code, Facilitator))!;
        await _sut.ToggleActionDoneAsync(
            Code, Facilitator, board.Actions.First(a => a.Title == "Speed up CI").Id);

        var export = await ExportAsync();

        export.Actions.Select(a => a.Title).Should().Equal("Book the room", "Speed up CI");
    }

    // --- Markdown ----------------------------------------------------------

    [Fact]
    public async Task Markdown_leads_with_the_board_name_and_groups_by_theme()
    {
        await SeedAsync();

        var md = RetroExportRenderer.ToMarkdown(await ExportAsync());

        md.Should().StartWith("# Sprint 24 retro");
        md.Should().Contain("## Themes");
        md.Should().Contain("### CI is slow — 1 dot");
        md.Should().Contain("- **Mad** — CI is slow");
    }

    [Fact]
    public async Task Markdown_keeps_ungrouped_cards_in_a_section_of_their_own()
    {
        // Dropping them would silently lose whatever the team never got round to grouping.
        await SeedAsync();

        var md = RetroExportRenderer.ToMarkdown(await ExportAsync());

        md.Should().Contain("## Cards");
        md.Should().Contain("standups run long");
    }

    [Fact]
    public async Task Markdown_renders_actions_as_a_task_list()
    {
        // So it can be pasted straight into a wiki page or a ticket and stay actionable there.
        await SeedAsync();

        var md = RetroExportRenderer.ToMarkdown(await ExportAsync());

        md.Should().Contain("## Action items");
        md.Should().Contain("- [ ] Speed up CI — Alice (due 2026-03-01)");
    }

    [Fact]
    public async Task A_finished_action_is_a_ticked_task()
    {
        await SeedAsync();
        var board = (await _sut.GetByShortCodeAsync(Code, Facilitator))!;
        await _sut.ToggleActionDoneAsync(Code, Facilitator, board.Actions[0].Id);

        var md = RetroExportRenderer.ToMarkdown(await ExportAsync());

        md.Should().Contain("- [x] Speed up CI");
    }

    [Fact]
    public async Task Markdown_says_so_when_there_are_no_actions()
    {
        // An empty section reads as a bug; "None" reads as a fact about the retro.
        await SeedToDiscussAsync();

        var md = RetroExportRenderer.ToMarkdown(await ExportAsync());

        md.Should().Contain("## Action items");
        md.Should().Contain("_None._");
        md.Should().NotContain("## Themes");
    }

    // --- CSV ---------------------------------------------------------------

    [Fact]
    public async Task Csv_has_a_row_per_card_and_per_action()
    {
        await SeedAsync();

        var csv = RetroExportRenderer.ToCsv(await ExportAsync());
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().StartWith("Kind,Theme,Column,Text,Author,Dots");
        lines.Should().HaveCount(4, "a header, two cards and one action");
        lines.Should().Contain(l => l.StartsWith("card,") && l.Contains("CI is slow"));
        lines.Should().Contain(l => l.StartsWith("action,") && l.Contains("Speed up CI"));
    }

    [Fact]
    public async Task A_grouped_cards_csv_row_names_its_theme()
    {
        await SeedAsync();

        var csv = RetroExportRenderer.ToCsv(await ExportAsync());

        csv.Should().Contain("card,CI is slow,Mad,CI is slow,Bob,1");
    }

    [Fact]
    public async Task Csv_quotes_a_field_containing_a_comma_or_a_quote()
    {
        // Otherwise one card with a comma in it shifts every column of that row.
        await SeedToDiscussAsync("Retro", "slow, flaky, and \"loud\"");

        var csv = RetroExportRenderer.ToCsv(await ExportAsync());

        csv.Should().Contain("\"slow, flaky, and \"\"loud\"\"\"");
    }

    // --- JSON --------------------------------------------------------------

    [Fact]
    public async Task Json_carries_the_whole_board_under_stable_property_names()
    {
        await SeedAsync();

        var json = RetroExportRenderer.ToJson(await ExportAsync());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("shortCode").GetString().Should().Be(Code);
        root.GetProperty("name").GetString().Should().Be("Sprint 24 retro");
        root.GetProperty("anonymous").GetBoolean().Should().BeFalse();
        root.GetProperty("phase").GetString().Should().Be("Discuss", "the SPA reads the phase as a name, not an ordinal");
        root.GetProperty("themes").GetArrayLength().Should().Be(1);
        root.GetProperty("looseCards").GetArrayLength().Should().Be(1);
        root.GetProperty("actions").GetArrayLength().Should().Be(1);
    }

    // --- Anonymity (#22) — the guarantee that matters ----------------------

    [Fact]
    public async Task An_anonymous_boards_export_carries_no_author_in_any_format()
    {
        await SeedAsync(anonymous: true);

        var export = await ExportAsync();

        export.Anonymous.Should().BeTrue();
        export.Themes.SelectMany(t => t.Cards).Concat(export.LooseCards)
            .Should().OnlyContain(c => c.Author == null);

        var rendered = new[]
        {
            RetroExportRenderer.ToMarkdown(export),
            RetroExportRenderer.ToCsv(export),
            RetroExportRenderer.ToJson(export),
        };

        foreach (var text in rendered)
        {
            // Bob authored a card and owns nothing, so his name appearing at all is the leak.
            text.Should().NotContainEquivalentOf("Bob");
        }
    }

    [Fact]
    public async Task An_anonymous_csv_omits_the_author_column_entirely()
    {
        // A blank column invites someone to ask where the data went, or to go looking for it. No
        // column at all says it was never collected for this board.
        await SeedAsync(anonymous: true);

        var csv = RetroExportRenderer.ToCsv(await ExportAsync());

        csv.Split('\n')[0].Should().NotContain("Author");
    }

    [Fact]
    public async Task An_anonymous_markdown_export_says_the_cards_were_anonymous()
    {
        // Otherwise a reader might take the missing attribution for sloppiness rather than a choice.
        await SeedAsync(anonymous: true);

        var md = RetroExportRenderer.ToMarkdown(await ExportAsync());

        md.Should().Contain("anonymous");
    }

    [Fact]
    public async Task There_is_no_way_to_ask_for_the_attributed_version_of_an_anonymous_board()
    {
        // GetExportAsync takes no caller identity — only the board's password — so there is no code
        // path by which a facilitator could de-anonymise the export.
        await SeedAsync(anonymous: true);

        var export = await ExportAsync();

        export.Themes.SelectMany(t => t.Cards).Concat(export.LooseCards)
            .Should().OnlyContain(c => c.Author == null);
    }

    [Fact]
    public async Task An_attributed_boards_export_names_authors()
    {
        await SeedAsync(anonymous: false);

        var md = RetroExportRenderer.ToMarkdown(await ExportAsync());

        md.Should().Contain("_(Bob)_");
    }

    // --- Guards ------------------------------------------------------------

    [Fact]
    public async Task Exporting_an_unknown_board_is_reported_as_not_found()
    {
        var (status, export) = await _sut.GetExportAsync("no-such-board", null);

        status.Should().Be(RetroExportStatus.BoardNotFound);
        export.Should().BeNull();
    }

    [Fact]
    public async Task A_poker_room_is_not_an_exportable_board()
    {
        // Same room table, same short-code space — the retro export must not answer for a room
        // that has no retro board on it.
        var poker = TestServices.Poker(_store, new StubShortCodeGenerator(Code), _clock);
        await poker.CreateAsync(new CreateSessionRequest(
            "Sprint 24", DeckType.Fibonacci, null, Facilitator, "Alice", Organise: true));

        var (status, _) = await _sut.GetExportAsync(Code, null);

        status.Should().Be(RetroExportStatus.BoardNotFound);
    }

    [Fact]
    public async Task Exporting_a_protected_board_needs_its_password()
    {
        // The export is the board's entire contents in one file, and a short code is only a bearer
        // token.
        await SeedAsync(password: "hunter2");

        var (missing, _) = await _sut.GetExportAsync(Code, null);
        var (wrong, _) = await _sut.GetExportAsync(Code, "guess");
        var (right, export) = await _sut.GetExportAsync(Code, "hunter2");

        missing.Should().Be(RetroExportStatus.PasswordRequired);
        wrong.Should().Be(RetroExportStatus.PasswordRequired);
        right.Should().Be(RetroExportStatus.Ok);
        export.Should().NotBeNull();
    }

    [Fact]
    public async Task Exporting_before_the_discussion_is_refused()
    {
        // Otherwise export is a side door around hidden collection (#23) and the withheld dot
        // totals (#25) — handing out cards the team has not seen and a ranking it has not reached.
        await _sut.CreateAsync(new CreateRetroRequest(
            "Retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true));

        (await _sut.GetExportAsync(Code, null)).Status
            .Should().Be(RetroExportStatus.NotYetVisible, "during Collect");

        await _sut.AdvancePhaseAsync(Code, Facilitator);
        (await _sut.GetExportAsync(Code, null)).Status
            .Should().Be(RetroExportStatus.NotYetVisible, "during Group");

        await _sut.AdvancePhaseAsync(Code, Facilitator);
        (await _sut.GetExportAsync(Code, null)).Status
            .Should().Be(RetroExportStatus.NotYetVisible, "during Vote");

        await _sut.AdvancePhaseAsync(Code, Facilitator);
        (await _sut.GetExportAsync(Code, null)).Status
            .Should().Be(RetroExportStatus.Ok, "from Discuss on");
    }

    [Fact]
    public async Task A_closed_board_can_still_be_exported()
    {
        // Exporting after the retro has ended is the normal case, not the exception.
        await SeedAsync();
        await _sut.CloseBoardAsync(Code, Facilitator);

        var (status, export) = await _sut.GetExportAsync(Code, null);

        status.Should().Be(RetroExportStatus.Ok);
        export!.Actions.Should().ContainSingle();
    }

    [Fact]
    public async Task A_deleted_board_cannot_be_exported()
    {
        await SeedAsync();
        await _sut.DeleteBoardAsync(Code, Facilitator);

        var (status, _) = await _sut.GetExportAsync(Code, null);

        status.Should().Be(RetroExportStatus.BoardNotFound);
    }

    // --- Carry-over provenance (#27) ---------------------------------------

    [Fact]
    public async Task A_board_that_carried_nothing_says_nothing_about_carry_over()
    {
        await SeedAsync();

        var export = await ExportAsync();

        export.CarriedFromShortCode.Should().BeNull();
        RetroExportRenderer.ToMarkdown(export).Should().NotContain("carried");
    }

    [Fact]
    public async Task A_board_that_carried_actions_forward_says_where_they_came_from()
    {
        // Read months later, "who decided this?" is answered by the previous board's code.
        var codes = new SequentialShortCodes();
        var sut = new RetroService(_store, new RoomService(_store, codes, _clock), _clock);

        await sut.CreateAsync(new CreateRetroRequest(
            "Last retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true));
        await sut.AdvancePhaseAsync("retro-1", Facilitator);
        await sut.AdvancePhaseAsync("retro-1", Facilitator);
        await sut.AdvancePhaseAsync("retro-1", Facilitator);
        await sut.AddActionAsync("retro-1", Facilitator, "Fix the flaky test", null, null, null, null);

        await sut.CreateAsync(new CreateRetroRequest(
            "This retro", RetroTemplate.MadSadGlad, null, Facilitator, "Alice", Organise: true,
            PreviousBoardShortCode: "retro-1"));
        await sut.AdvancePhaseAsync("retro-2", Facilitator);
        await sut.AdvancePhaseAsync("retro-2", Facilitator);
        await sut.AdvancePhaseAsync("retro-2", Facilitator);

        var (status, export) = await sut.GetExportAsync("retro-2", null);

        status.Should().Be(RetroExportStatus.Ok);
        export!.CarriedFromShortCode.Should().Be("retro-1");
        export.Actions.Should().ContainSingle().Which.CarriedOver.Should().BeTrue();
        var md = RetroExportRenderer.ToMarkdown(export);
        md.Should().Contain("carried forward from `retro-1`");
        md.Should().Contain("- [ ] Fix the flaky test _(carried over)_");
    }

    /// <summary>Hands out a fresh short code per board, so two retros can coexist.</summary>
    private sealed class SequentialShortCodes : IShortCodeGenerator
    {
        private int _next;
        public string Generate() => $"retro-{++_next}";
    }
}
