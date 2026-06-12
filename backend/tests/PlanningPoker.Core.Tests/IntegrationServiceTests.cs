using FluentAssertions;
using PlanningPoker.Core;
using PlanningPoker.Core.Contracts;
using PlanningPoker.Core.Integrations;
using PlanningPoker.Core.Models;
using PlanningPoker.Core.Tests.Fakes;
using Xunit;

namespace PlanningPoker.Core.Tests;

public class IntegrationServiceTests
{
    private readonly FakeSessionStore _store = new();
    private readonly TestClock _clock = new();
    private readonly FakeIssueTracker _tracker = new();
    private readonly InMemoryIntegrationConnectionStore _connections = new();
    private readonly SessionService _sessions;
    private readonly IntegrationService _sut;

    private const string Code = "blue-fox-42";
    private const string Organiser = "alice";
    private const string Bob = "bob";

    public IntegrationServiceTests()
    {
        _sessions = new SessionService(_store, new StubShortCodeGenerator(Code), _clock);
        _sut = new IntegrationService(_store, _tracker, _connections, new BoardUrlParser(), _clock, new IntegrationsOptions { Jira = new() { Enabled = true }, Ado = new() { Enabled = true } });
    }

    private async Task SeedAsync()
    {
        await _sessions.CreateAsync(new CreateSessionRequest("Sprint", DeckType.Fibonacci, null, Organiser, "Alice", true));
        await _sessions.JoinAsync(new JoinSessionRequest(Code, Bob, "Bob", ParticipantRole.Voter));
    }

    private async Task<Guid> SessionIdAsync() => (await _store.FindByShortCodeAsync(Code))!.Id;

    [Fact]
    public async Task Connect_validates_stores_the_connection_and_sets_the_provider()
    {
        await SeedAsync();

        var result = await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");

        result.Status.Should().Be(IntegrationStatus.Ok);
        result.AccountName.Should().Be("Test User");
        result.Session!.Integration!.Provider.Should().Be(IntegrationProvider.Jira);
        _sut.IsConnected(await SessionIdAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Connect_passes_the_story_points_field_override_to_the_connection()
    {
        await SeedAsync();

        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok", "customfield_10004");

        _tracker.LastConnection!.StoryPointsFieldOverride.Should().Be("customfield_10004");
    }

    [Fact]
    public async Task Connect_treats_a_blank_field_override_as_none()
    {
        await SeedAsync();

        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok", "   ");

        _tracker.LastConnection!.StoryPointsFieldOverride.Should().BeNull();
    }

    [Fact]
    public async Task Connect_is_rejected_when_the_feature_is_disabled()
    {
        await SeedAsync();
        var disabled = new IntegrationService(_store, _tracker, _connections, new BoardUrlParser(), _clock, new IntegrationsOptions { Jira = new() { Enabled = false }, Ado = new() { Enabled = false } });

        var result = await disabled.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");

        result.Status.Should().Be(IntegrationStatus.Disabled);
    }

    [Fact]
    public async Task Connect_is_rejected_for_a_provider_that_is_disabled_while_another_is_enabled()
    {
        await SeedAsync();
        // Only Jira is enabled (#43) — connecting to Azure DevOps must be rejected as disabled.
        var jiraOnly = new IntegrationService(_store, _tracker, _connections, new BoardUrlParser(), _clock,
            new IntegrationsOptions { Jira = new() { Enabled = true }, Ado = new() { Enabled = false } });

        var ado = await jiraOnly.ConnectAsync(Code, Organiser, IntegrationProvider.AzureDevOps, "https://dev.azure.com/acme", null, "tok");
        ado.Status.Should().Be(IntegrationStatus.Disabled);

        var jira = await jiraOnly.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");
        jira.Status.Should().Be(IntegrationStatus.Ok);
    }

    [Fact]
    public async Task Connect_by_a_non_organiser_is_rejected()
    {
        await SeedAsync();

        var result = await _sut.ConnectAsync(Code, Bob, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");

        result.Status.Should().Be(IntegrationStatus.NotOrganiser);
    }

    [Fact]
    public async Task Connect_surfaces_auth_failure_from_the_tracker()
    {
        await SeedAsync();
        _tracker.ValidateThrows = new TrackerException(TrackerErrorKind.Unauthorized, "Bad token");

        var result = await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");

        result.Status.Should().Be(IntegrationStatus.AuthFailed);
        result.Error.Should().Contain("Bad token");
    }

    [Fact]
    public async Task Link_issue_requires_a_connection_first()
    {
        await SeedAsync();

        var result = await _sut.LinkIssueAsync(Code, Organiser, "PROJ-1");

        result.Status.Should().Be(IntegrationStatus.NotConnected);
    }

    [Fact]
    public async Task Link_issue_fetches_and_stores_the_ticket_for_everyone()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");
        _tracker.Issue = new IssueDetails("PROJ-7", "Login page", "Build the login", "https://acme.atlassian.net/browse/PROJ-7", 3, "customfield_10016", "Story point estimate");

        var result = await _sut.LinkIssueAsync(Code, Organiser, "PROJ-7");

        result.Status.Should().Be(IntegrationStatus.Ok);
        var issue = result.Session!.Integration!.LinkedIssue!;
        issue.Key.Should().Be("PROJ-7");
        issue.Title.Should().Be("Login page");
        issue.StoryPoints.Should().Be(3);
        issue.StoryPointsFieldAvailable.Should().BeTrue();
        result.Session.CurrentStory.Should().Be("Login page");
    }

    [Fact]
    public async Task Link_issue_maps_not_found_from_the_tracker()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");
        _tracker.GetIssueThrows = new TrackerException(TrackerErrorKind.NotFound, "No such issue");

        var result = await _sut.LinkIssueAsync(Code, Organiser, "PROJ-404");

        result.Status.Should().Be(IntegrationStatus.IssueNotFound);
    }

    [Fact]
    public async Task Disconnect_clears_the_connection_provider_and_linked_issue()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");
        await _sut.LinkIssueAsync(Code, Organiser, "PROJ-7");

        var result = await _sut.DisconnectAsync(Code, Organiser);

        result.Status.Should().Be(IntegrationStatus.Ok);
        result.Session!.Integration.Should().BeNull();
        _sut.IsConnected(await SessionIdAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Connect_unknown_session_returns_not_found()
    {
        var result = await _sut.ConnectAsync("missing-code-1", Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");

        result.Status.Should().Be(IntegrationStatus.SessionNotFound);
    }

    [Fact]
    public async Task Link_is_rejected_when_the_feature_is_disabled()
    {
        await SeedAsync();
        var disabled = new IntegrationService(_store, _tracker, _connections, new BoardUrlParser(), _clock, new IntegrationsOptions { Jira = new() { Enabled = false }, Ado = new() { Enabled = false } });

        var result = await disabled.LinkIssueAsync(Code, Organiser, "PROJ-1");

        result.Status.Should().Be(IntegrationStatus.Disabled);
    }

    [Fact]
    public async Task Link_by_a_non_participant_is_rejected()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");

        var result = await _sut.LinkIssueAsync(Code, "stranger", "PROJ-1");

        result.Status.Should().Be(IntegrationStatus.NotParticipant);
    }

    [Fact]
    public async Task Connect_maps_rate_limit_to_provider_error()
    {
        await SeedAsync();
        _tracker.ValidateThrows = new TrackerException(TrackerErrorKind.RateLimited, "Slow down");

        var result = await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");

        result.Status.Should().Be(IntegrationStatus.ProviderError);
    }

    [Fact]
    public async Task Connect_with_invalid_validation_returns_auth_failed()
    {
        await SeedAsync();
        _tracker.Validation = new TrackerValidation(false, null, "Nope");

        var result = await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");

        result.Status.Should().Be(IntegrationStatus.AuthFailed);
    }

    [Fact]
    public async Task Submit_story_points_requires_a_linked_issue()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");

        var result = await _sut.SubmitStoryPointsAsync(Code, Organiser, 5);

        result.Status.Should().Be(IntegrationStatus.IssueNotFound);
    }

    [Fact]
    public async Task Submit_story_points_writes_to_the_tracker_and_updates_the_linked_issue()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");
        _tracker.Issue = new IssueDetails("PROJ-7", "Login", "desc", "url", 3, "customfield_10016", "Story point estimate");
        await _sut.LinkIssueAsync(Code, Organiser, "PROJ-7");
        // After submit the re-fetch returns the new value.
        _tracker.Issue = new IssueDetails("PROJ-7", "Login", "desc", "url", 8, "customfield_10016", "Story point estimate");

        var result = await _sut.SubmitStoryPointsAsync(Code, Organiser, 8);

        result.Status.Should().Be(IntegrationStatus.Ok);
        _tracker.LastSubmittedKey.Should().Be("PROJ-7");
        _tracker.LastSubmittedPoints.Should().Be(8);
        result.Session!.Integration!.LinkedIssue!.StoryPoints.Should().Be(8);
    }

    [Fact]
    public async Task Submit_by_a_non_organiser_is_rejected()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");
        await _sut.LinkIssueAsync(Code, Organiser, "PROJ-7");

        var result = await _sut.SubmitStoryPointsAsync(Code, Bob, 8);

        result.Status.Should().Be(IntegrationStatus.NotOrganiser);
    }

    [Fact]
    public async Task ConnectedAccount_reports_the_validated_name()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");

        _sut.ConnectedAccount(await SessionIdAsync()).Should().Be("Test User");
    }

    // --- Ticket queue (#38) ----------------------------------------------

    [Fact]
    public async Task Load_queue_from_keys_populates_the_queue()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");
        _tracker.SearchResult = new[]
        {
            new IssueSummary("PROJ-1", "First", "To Do", 3, "u1"),
            new IssueSummary("PROJ-2", "Second", "In Progress", null, "u2"),
        };

        var result = await _sut.LoadQueueFromKeysAsync(Code, Organiser, new[] { "PROJ-1", "PROJ-2" });

        result.Status.Should().Be(IntegrationStatus.Ok);
        result.Session!.Integration!.Queue.Select(q => q.Key).Should().Equal("PROJ-1", "PROJ-2");
        _tracker.LastQuery.Should().BeOfType<KeyListQuery>();
    }

    [Fact]
    public async Task Loading_tickets_auto_selects_the_first_so_a_single_id_links_it()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");
        _tracker.SearchResult = new[] { new IssueSummary("PROJ-7", "Only one", null, null, "u7") };
        _tracker.Issue = new IssueDetails("PROJ-7", "Only one", "desc", "u7", 3, "customfield_10016", "Story point estimate");

        var result = await _sut.LoadQueueFromKeysAsync(Code, Organiser, new[] { "PROJ-7" });

        // The single ticket is loaded into the queue AND linked (like the old "link ticket").
        result.Session!.Integration!.LinkedIssue!.Key.Should().Be("PROJ-7");
        result.Session.Integration.Queue.Single(q => q.Key == "PROJ-7").IsSelected.Should().BeTrue();
    }

    [Fact]
    public async Task Load_queue_from_url_parses_then_searches()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");
        _tracker.SearchResult = new[] { new IssueSummary("PROJ-9", "Nine", null, null, "u9") };

        var result = await _sut.LoadQueueFromUrlAsync(Code, Organiser, "https://acme.atlassian.net/jira/software/projects/PROJ/boards/42");

        result.Status.Should().Be(IntegrationStatus.Ok);
        _tracker.LastQuery.Should().BeOfType<JiraBoardQuery>().Which.BoardId.Should().Be("42");
        result.Session!.Integration!.Queue.Should().ContainSingle(q => q.Key == "PROJ-9");
    }

    [Fact]
    public async Task Load_queue_from_unrecognised_url_is_rejected_with_guidance()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");

        var result = await _sut.LoadQueueFromUrlAsync(Code, Organiser, "https://acme.atlassian.net/some/random/page");

        result.Status.Should().Be(IntegrationStatus.ProviderError);
        result.Error.Should().Contain("paste the ticket IDs");
    }

    [Fact]
    public async Task Load_queue_requires_a_connection()
    {
        await SeedAsync();

        var result = await _sut.LoadQueueFromKeysAsync(Code, Organiser, new[] { "PROJ-1" });

        result.Status.Should().Be(IntegrationStatus.NotConnected);
    }

    [Fact]
    public async Task Selecting_a_queued_ticket_marks_it_selected_in_the_queue()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");
        _tracker.SearchResult = new[]
        {
            new IssueSummary("PROJ-1", "First", null, null, "u1"),
            new IssueSummary("PROJ-2", "Second", null, null, "u2"),
        };
        await _sut.LoadQueueFromKeysAsync(Code, Organiser, new[] { "PROJ-1", "PROJ-2" });
        _tracker.Issue = new IssueDetails("PROJ-2", "Second", "desc", "u2", 5, "customfield_10016", "Story point estimate");

        // Selecting is the existing link flow.
        var result = await _sut.LinkIssueAsync(Code, Organiser, "PROJ-2");

        var queue = result.Session!.Integration!.Queue;
        queue.Single(q => q.Key == "PROJ-2").IsSelected.Should().BeTrue();
        queue.Single(q => q.Key == "PROJ-1").IsSelected.Should().BeFalse();
    }

    [Fact]
    public async Task Load_queue_is_rejected_when_disabled()
    {
        await SeedAsync();
        var disabled = new IntegrationService(_store, _tracker, _connections, new BoardUrlParser(), _clock, new IntegrationsOptions { Jira = new() { Enabled = false }, Ado = new() { Enabled = false } });

        (await disabled.LoadQueueFromKeysAsync(Code, Organiser, new[] { "PROJ-1" })).Status.Should().Be(IntegrationStatus.Disabled);
    }

    [Fact]
    public async Task Load_queue_by_a_non_organiser_is_rejected()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");

        (await _sut.LoadQueueFromKeysAsync(Code, Bob, new[] { "PROJ-1" })).Status.Should().Be(IntegrationStatus.NotOrganiser);
    }

    [Fact]
    public async Task Load_queue_with_empty_keys_is_rejected()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");

        (await _sut.LoadQueueFromKeysAsync(Code, Organiser, new[] { "  ", "" })).Status.Should().Be(IntegrationStatus.ProviderError);
    }

    [Fact]
    public async Task Load_queue_surfaces_a_search_failure()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");
        _tracker.SearchThrows = new TrackerException(TrackerErrorKind.RateLimited, "slow down");

        (await _sut.LoadQueueFromKeysAsync(Code, Organiser, new[] { "PROJ-1" })).Status.Should().Be(IntegrationStatus.ProviderError);
    }

    [Fact]
    public async Task Clear_queue_by_a_non_organiser_is_rejected()
    {
        await SeedAsync();

        (await _sut.ClearQueueAsync(Code, Bob)).Status.Should().Be(IntegrationStatus.NotOrganiser);
    }

    [Fact]
    public async Task Clear_queue_empties_it()
    {
        await SeedAsync();
        await _sut.ConnectAsync(Code, Organiser, IntegrationProvider.Jira, "https://acme.atlassian.net", "a@acme.io", "tok");
        _tracker.SearchResult = new[] { new IssueSummary("PROJ-1", "First", null, null, "u1") };
        await _sut.LoadQueueFromKeysAsync(Code, Organiser, new[] { "PROJ-1" });

        var result = await _sut.ClearQueueAsync(Code, Organiser);

        result.Session!.Integration!.Queue.Should().BeEmpty();
    }
}
