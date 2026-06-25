using System.Net;
using FluentAssertions;
using PlanningPoker.Core.Integrations;
using Xunit;

namespace PlanningPoker.Integrations.Tests;

public class GitHubIssueTrackerTests
{
    private const string Base = "https://github.com/acme/widgets";
    private static readonly TrackerConnection Conn =
        new(IntegrationProvider.GitHub, Base, null, "ghp_token", AuthScheme.Bearer);

    private static GitHubIssueTracker Build(StubHttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), new TrackerHostPolicy(new[] { "github.com" }), new HtmlDescriptionSanitizer());

    [Fact]
    public async Task Validate_returns_the_authenticated_login()
    {
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/user", HttpStatusCode.OK, """{ "login": "octocat" }""");

        var result = await Build(handler).ValidateAsync(Conn);

        result.Ok.Should().BeTrue();
        result.AccountName.Should().Be("octocat");
    }

    [Fact]
    public async Task GetIssue_reads_title_body_and_points_from_a_label()
    {
        var json = """
            {
              "number": 42,
              "title": "Login page",
              "body": "Build the login",
              "html_url": "https://github.com/acme/widgets/issues/42",
              "labels": [ { "name": "bug" }, { "name": "points: 5" } ]
            }
            """;
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/repos/acme/widgets/issues/42", HttpStatusCode.OK, json);

        var issue = await Build(handler).GetIssueAsync(Conn, "42");

        issue.Key.Should().Be("42");
        issue.Title.Should().Be("Login page");
        issue.Description.Should().Contain("Build the login");
        issue.CurrentStoryPoints.Should().Be(5);
        issue.StoryPointsFieldAvailable.Should().BeTrue();
        issue.Url.Should().Be("https://github.com/acme/widgets/issues/42");
    }

    [Fact]
    public async Task SetStoryPoints_replaces_the_points_label_keeping_others()
    {
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/repos/acme/widgets/issues/42", HttpStatusCode.OK,
                """{ "number": 42, "labels": [ { "name": "bug" }, { "name": "points: 3" } ] }""")
            .Map("PATCH", "/repos/acme/widgets/issues/42", HttpStatusCode.OK, """{ "number": 42 }""");

        await Build(handler).SetStoryPointsAsync(Conn, "42", 8);

        var patchBody = handler.RequestBodies[^1];
        patchBody.Should().Contain("bug"); // unrelated label kept
        patchBody.Should().Contain("points: 8"); // new points label
        patchBody.Should().NotContain("points: 3"); // old points label dropped
    }

    [Fact]
    public async Task Search_by_key_list_fetches_each_issue()
    {
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/repos/acme/widgets/issues/1", HttpStatusCode.OK,
                """{ "number": 1, "title": "First", "state": "open", "labels": [] }""")
            .Map("GET", "/repos/acme/widgets/issues/2", HttpStatusCode.OK,
                """{ "number": 2, "title": "Second", "state": "closed", "labels": [ { "name": "points: 2" } ] }""");

        var results = await Build(handler).SearchAsync(Conn, new KeyListQuery(new[] { "1", "#2" }), 50);

        results.Should().HaveCount(2);
        results[0].Key.Should().Be("1");
        results[1].StoryPoints.Should().Be(2);
    }

    [Fact]
    public async Task Open_issues_query_lists_open_issues_and_skips_pull_requests()
    {
        var json = """
            [
              { "number": 1, "title": "Issue one", "state": "open", "labels": [] },
              { "number": 2, "title": "A PR", "state": "open", "labels": [], "pull_request": { "url": "x" } }
            ]
            """;
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/repos/acme/widgets/issues", HttpStatusCode.OK, json);

        var results = await Build(handler).SearchAsync(Conn, new OpenIssuesQuery(), 50);

        results.Should().ContainSingle();
        results[0].Key.Should().Be("1");
    }

    [Fact]
    public async Task A_bad_token_surfaces_as_unauthorized()
    {
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/user", HttpStatusCode.Unauthorized, null);

        var act = async () => await Build(handler).ValidateAsync(Conn);

        (await act.Should().ThrowAsync<TrackerException>()).Which.Kind.Should().Be(TrackerErrorKind.Unauthorized);
    }
}
