using System.Net;
using FluentAssertions;
using PlanningPoker.Core.Integrations;
using Xunit;

namespace PlanningPoker.Integrations.Tests;

public class JiraIssueTrackerTests
{
    private const string Base = "https://acme.atlassian.net";
    private static readonly TrackerConnection Conn =
        new(IntegrationProvider.Jira, Base, "a@acme.io", "token");

    private const string FieldJson = """
        [
          { "id": "summary", "name": "Summary", "schema": { "type": "string" } },
          { "id": "customfield_10016", "name": "Story point estimate", "schema": { "type": "number" } },
          { "id": "customfield_10004", "name": "Story Points", "schema": { "type": "number" } },
          { "id": "customfield_10100", "name": "Acceptance Criteria", "schema": { "type": "string" } }
        ]
        """;

    private static JiraIssueTracker Build(StubHttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), new TrackerHostPolicy(), new HtmlDescriptionSanitizer());

    [Fact]
    public async Task Validate_returns_the_account_display_name()
    {
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/rest/api/3/myself", HttpStatusCode.OK, """{ "displayName": "Ada Lovelace" }""");

        var result = await Build(handler).ValidateAsync(Conn);

        result.Ok.Should().BeTrue();
        result.AccountName.Should().Be("Ada Lovelace");
    }

    [Fact]
    public async Task Validate_maps_401_to_unauthorized()
    {
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/rest/api/3/myself", HttpStatusCode.Unauthorized, null);

        var act = () => Build(handler).ValidateAsync(Conn);

        (await act.Should().ThrowAsync<TrackerException>()).Which.Kind.Should().Be(TrackerErrorKind.Unauthorized);
    }

    [Fact]
    public async Task GetIssue_returns_title_description_and_resolved_story_points()
    {
        var issueJson = """
            {
              "key": "PROJ-7",
              "fields": {
                "summary": "Login page",
                "customfield_10016": 5
              },
              "renderedFields": {
                "description": "<p>Build the <b>login</b>.</p><ul><li>item</li></ul>"
              }
            }
            """;
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/rest/api/3/field", HttpStatusCode.OK, FieldJson)
            .Map("GET", "/rest/api/3/issue/", HttpStatusCode.OK, issueJson);

        var issue = await Build(handler).GetIssueAsync(Conn, "PROJ-7");

        issue.Key.Should().Be("PROJ-7");
        issue.Title.Should().Be("Login page");
        issue.Description.Should().Contain("<b>login</b>").And.Contain("<li>item</li>"); // formatting preserved
        issue.CurrentStoryPoints.Should().Be(5);
        // Regression guard: 'description' must be requested AND renderedFields expanded, else Jira
        // returns no rendered HTML and the description shows blank.
        var issueRequest = handler.Requests.Single(r => r.RequestUri!.AbsolutePath.Contains("/rest/api/3/issue/"));
        issueRequest.RequestUri!.Query.Should().Contain("description").And.Contain("expand=renderedFields");
        issue.StoryPointsFieldId.Should().Be("customfield_10016"); // prefers "Story point estimate"
        issue.StoryPointsFieldAvailable.Should().BeTrue();
        issue.Url.Should().Be("https://acme.atlassian.net/browse/PROJ-7");
    }

    [Fact]
    public async Task GetIssue_sanitizes_dangerous_markup_in_the_description()
    {
        var issueJson = """
            {
              "key": "PROJ-9",
              "fields": { "summary": "X" },
              "renderedFields": { "description": "<p>Hi</p><script>alert(1)</script><img src=x onerror=alert(2)>" }
            }
            """;
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/rest/api/3/field", HttpStatusCode.OK, FieldJson)
            .Map("GET", "/rest/api/3/issue/", HttpStatusCode.OK, issueJson);

        var issue = await Build(handler).GetIssueAsync(Conn, "PROJ-9");

        issue.Description.Should().Contain("<p>Hi</p>");
        issue.Description.Should().NotContain("<script").And.NotContain("onerror");
    }

    [Fact]
    public async Task GetIssue_maps_404_to_not_found()
    {
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/rest/api/3/field", HttpStatusCode.OK, FieldJson)
            .Map("GET", "/rest/api/3/issue/", HttpStatusCode.NotFound, null);

        var act = () => Build(handler).GetIssueAsync(Conn, "PROJ-404");

        (await act.Should().ThrowAsync<TrackerException>()).Which.Kind.Should().Be(TrackerErrorKind.NotFound);
    }

    [Fact]
    public async Task SetStoryPoints_resolves_the_field_and_puts_the_value()
    {
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/rest/api/3/field", HttpStatusCode.OK, FieldJson)
            .Map("PUT", "/rest/api/3/issue/", HttpStatusCode.NoContent, null);

        await Build(handler).SetStoryPointsAsync(Conn, "PROJ-7", 8);

        handler.RequestBodies.Should().Contain(b => b.Contains("customfield_10016") && b.Contains("8"));
    }

    [Fact]
    public async Task GetIssue_over_oauth_uses_bearer_auth_path_prefixed_base_and_site_browse_url()
    {
        var oauthConn = new TrackerConnection(
            IntegrationProvider.Jira, "https://api.atlassian.com/ex/jira/cloud-1", null, "ACCESS-1",
            AuthScheme.Bearer, BrowseBaseUrl: "https://acme.atlassian.net");
        var issueJson = """{ "key": "PROJ-7", "fields": { "summary": "Login" } }""";
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/ex/jira/cloud-1/rest/api/3/field", HttpStatusCode.OK, FieldJson)
            .Map("GET", "/ex/jira/cloud-1/rest/api/3/issue/", HttpStatusCode.OK, issueJson);

        var issue = await Build(handler).GetIssueAsync(oauthConn, "PROJ-7");

        issue.Title.Should().Be("Login");
        issue.Url.Should().Be("https://acme.atlassian.net/browse/PROJ-7"); // browse uses the site, not the API gateway
        handler.Requests.Should().OnlyContain(r => r.Headers.Authorization!.Scheme == "Bearer");
    }

    [Fact]
    public async Task Search_by_keys_uses_a_jql_in_clause_and_maps_summaries()
    {
        var searchJson = """
            { "issues": [
              { "key": "PROJ-1", "fields": { "summary": "First", "status": { "name": "To Do" }, "customfield_10016": 3 } },
              { "key": "PROJ-2", "fields": { "summary": "Second", "status": { "name": "Done" } } }
            ] }
            """;
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/rest/api/3/field", HttpStatusCode.OK, FieldJson)
            .Map("GET", "/rest/api/3/search/jql", HttpStatusCode.OK, searchJson);

        var results = await Build(handler).SearchAsync(Conn, new KeyListQuery(new[] { "PROJ-1", "PROJ-2" }), 100);

        results.Select(r => r.Key).Should().Equal("PROJ-1", "PROJ-2");
        results[0].Status.Should().Be("To Do");
        results[0].StoryPoints.Should().Be(3);
        results[0].Url.Should().Be("https://acme.atlassian.net/browse/PROJ-1");
        var searchReq = handler.Requests.Single(r => r.RequestUri!.AbsolutePath.Contains("/search"));
        // Must use the enhanced-JQL endpoint — the legacy /rest/api/3/search returns 410 Gone now.
        searchReq.RequestUri!.AbsolutePath.Should().EndWith("/rest/api/3/search/jql");
        Uri.UnescapeDataString(searchReq.RequestUri!.Query).Should().Contain("key in (PROJ-1,PROJ-2)");
    }

    [Fact]
    public async Task Search_by_board_uses_the_agile_endpoint()
    {
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/rest/api/3/field", HttpStatusCode.OK, FieldJson)
            .Map("GET", "/rest/agile/1.0/board/42/issue", HttpStatusCode.OK,
                """{ "issues": [ { "key": "PROJ-9", "fields": { "summary": "Nine" } } ] }""");

        var results = await Build(handler).SearchAsync(Conn, new JiraBoardQuery("42"), 100);

        results.Should().ContainSingle(r => r.Key == "PROJ-9" && r.Title == "Nine");
    }

    [Fact]
    public async Task GetIssue_appends_acceptance_criteria_under_a_subtitle()
    {
        var issueJson = """
            {
              "key": "PROJ-7",
              "fields": { "summary": "Login" },
              "renderedFields": {
                "description": "<p>Build the login.</p>",
                "customfield_10100": "<ul><li>Given a user</li></ul>"
              }
            }
            """;
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/rest/api/3/field", HttpStatusCode.OK, FieldJson)
            .Map("GET", "/rest/api/3/issue/", HttpStatusCode.OK, issueJson);

        var issue = await Build(handler).GetIssueAsync(Conn, "PROJ-7");

        issue.Description.Should().Contain("Build the login.");
        issue.Description.Should().Contain("<h4>Acceptance criteria</h4>");
        issue.Description.Should().Contain("Given a user");
        // The AC field must be requested so Jira renders it under renderedFields.
        var issueReq = handler.Requests.Single(r => r.RequestUri!.AbsolutePath.Contains("/issue/"));
        issueReq.RequestUri!.Query.Should().Contain("customfield_10100");
    }

    [Fact]
    public async Task GetIssue_without_acceptance_criteria_leaves_the_description_unchanged()
    {
        var issueJson = """
            { "key": "PROJ-7", "fields": { "summary": "Login" },
              "renderedFields": { "description": "<p>Just a description.</p>" } }
            """;
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/rest/api/3/field", HttpStatusCode.OK, FieldJson)
            .Map("GET", "/rest/api/3/issue/", HttpStatusCode.OK, issueJson);

        var issue = await Build(handler).GetIssueAsync(Conn, "PROJ-7");

        issue.Description.Should().Be("<p>Just a description.</p>");
        issue.Description.Should().NotContain("Acceptance criteria");
    }

    [Fact]
    public async Task GetIssue_with_field_override_by_name_uses_that_field()
    {
        // Auto-detect would pick "Story point estimate" (customfield_10016); the override names the other.
        var conn = Conn with { StoryPointsFieldOverride = "Story Points" };
        var issueJson = """{ "key": "PROJ-7", "fields": { "summary": "X", "customfield_10004": 13 } }""";
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/rest/api/3/field", HttpStatusCode.OK, FieldJson)
            .Map("GET", "/rest/api/3/issue/", HttpStatusCode.OK, issueJson);

        var issue = await Build(handler).GetIssueAsync(conn, "PROJ-7");

        issue.StoryPointsFieldId.Should().Be("customfield_10004");
        issue.CurrentStoryPoints.Should().Be(13);
        issue.StoryPointsFieldAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task GetIssue_with_field_override_by_id_uses_that_field()
    {
        var conn = Conn with { StoryPointsFieldOverride = "customfield_10004" };
        var issueJson = """{ "key": "PROJ-7", "fields": { "summary": "X", "customfield_10004": 8 } }""";
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/rest/api/3/field", HttpStatusCode.OK, FieldJson)
            .Map("GET", "/rest/api/3/issue/", HttpStatusCode.OK, issueJson);

        var issue = await Build(handler).GetIssueAsync(conn, "PROJ-7");

        issue.StoryPointsFieldId.Should().Be("customfield_10004");
        issue.CurrentStoryPoints.Should().Be(8);
    }

    [Fact]
    public async Task GetIssue_with_unknown_customfield_override_trusts_it_verbatim()
    {
        // The id isn't in the field list, but it looks like a Jira custom field, so we use it as given.
        var conn = Conn with { StoryPointsFieldOverride = "customfield_99999" };
        var issueJson = """{ "key": "PROJ-7", "fields": { "summary": "X", "customfield_99999": 21 } }""";
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/rest/api/3/field", HttpStatusCode.OK, FieldJson)
            .Map("GET", "/rest/api/3/issue/", HttpStatusCode.OK, issueJson);

        var issue = await Build(handler).GetIssueAsync(conn, "PROJ-7");

        issue.StoryPointsFieldId.Should().Be("customfield_99999");
        issue.CurrentStoryPoints.Should().Be(21);
    }

    [Fact]
    public async Task SetStoryPoints_with_field_override_writes_to_that_field()
    {
        var conn = Conn with { StoryPointsFieldOverride = "Story Points" };
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/rest/api/3/field", HttpStatusCode.OK, FieldJson)
            .Map("PUT", "/rest/api/3/issue/", HttpStatusCode.NoContent, null);

        await Build(handler).SetStoryPointsAsync(conn, "PROJ-7", 5);

        handler.RequestBodies.Should().Contain(b => b.Contains("customfield_10004") && b.Contains("5"));
    }

    [Fact]
    public void Host_policy_rejects_non_allowlisted_hosts()
    {
        var policy = new TrackerHostPolicy();

        policy.Invoking(p => p.Validate("https://evil.example.com"))
            .Should().Throw<TrackerException>().Which.Kind.Should().Be(TrackerErrorKind.InvalidRequest);
        policy.Invoking(p => p.Validate("http://acme.atlassian.net"))
            .Should().Throw<TrackerException>(); // not HTTPS
        policy.Validate("https://acme.atlassian.net").Host.Should().Be("acme.atlassian.net");
    }
}
