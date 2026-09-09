using System.Net;
using FluentAssertions;
using TeamTools.Core.Integrations;
using Xunit;

namespace TeamTools.Integrations.Tests;

public class GitLabIssueTrackerTests
{
    private const string Base = "https://gitlab.com/acme/widgets";
    private static readonly TrackerConnection Conn =
        new(IntegrationProvider.GitLab, Base, null, "glpat-token");

    private static GitLabIssueTracker Build(StubHttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), new TrackerHostPolicy(new[] { "gitlab.com" }), new HtmlDescriptionSanitizer());

    [Fact]
    public async Task Validate_returns_the_authenticated_user_name()
    {
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/api/v4/user", HttpStatusCode.OK, """{ "name": "Grace Hopper", "username": "grace" }""");

        var result = await Build(handler).ValidateAsync(Conn);

        result.Ok.Should().BeTrue();
        result.AccountName.Should().Be("Grace Hopper");
    }

    [Fact]
    public async Task GetIssue_reads_title_description_and_the_native_weight_as_points()
    {
        var json = """
            {
              "iid": 7,
              "title": "Login page",
              "description": "Build the login",
              "web_url": "https://gitlab.com/acme/widgets/-/issues/7",
              "weight": 5
            }
            """;
        var handler = new StubHttpMessageHandler()
            // project path is URL-encoded (acme%2Fwidgets); match on the stable suffix.
            .Map("GET", "/issues/7", HttpStatusCode.OK, json);

        var issue = await Build(handler).GetIssueAsync(Conn, "7");

        issue.Key.Should().Be("7");
        issue.Title.Should().Be("Login page");
        issue.CurrentStoryPoints.Should().Be(5);
        issue.StoryPointsFieldAvailable.Should().BeTrue();
        issue.Url.Should().Be("https://gitlab.com/acme/widgets/-/issues/7");
    }

    [Fact]
    public async Task SetStoryPoints_sets_the_native_weight_for_a_whole_number()
    {
        var handler = new StubHttpMessageHandler()
            .Map("PUT", "/issues/7", HttpStatusCode.OK, """{ "iid": 7 }""");

        await Build(handler).SetStoryPointsAsync(Conn, "7", 8);

        var request = handler.Requests[^1];
        request.Method.Method.Should().Be("PUT");
        request.RequestUri!.Query.Should().Contain("weight=8");
    }

    [Fact]
    public async Task SetStoryPoints_falls_back_to_a_label_for_a_fractional_value()
    {
        var handler = new StubHttpMessageHandler()
            .Map("PUT", "/issues/7", HttpStatusCode.OK, """{ "iid": 7 }""");

        await Build(handler).SetStoryPointsAsync(Conn, "7", 0.5);

        var request = handler.Requests[^1];
        request.RequestUri!.Query.Should().Contain("add_labels=");
        request.RequestUri!.Query.Should().NotContain("weight=");
    }

    [Fact]
    public async Task Open_issues_query_lists_open_issues()
    {
        var json = """
            [
              { "iid": 1, "title": "One", "state": "opened", "web_url": "u1", "weight": 3 },
              { "iid": 2, "title": "Two", "state": "opened", "web_url": "u2" }
            ]
            """;
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/issues", HttpStatusCode.OK, json);

        var results = await Build(handler).SearchAsync(Conn, new OpenIssuesQuery(), 50);

        results.Should().HaveCount(2);
        results[0].Key.Should().Be("1");
        results[0].StoryPoints.Should().Be(3);
    }

    [Fact]
    public async Task A_bad_token_surfaces_as_unauthorized()
    {
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/api/v4/user", HttpStatusCode.Unauthorized, null);

        var act = async () => await Build(handler).ValidateAsync(Conn);

        (await act.Should().ThrowAsync<TrackerException>()).Which.Kind.Should().Be(TrackerErrorKind.Unauthorized);
    }
}
