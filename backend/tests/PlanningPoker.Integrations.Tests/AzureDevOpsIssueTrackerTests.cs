using System.Net;
using FluentAssertions;
using PlanningPoker.Core.Integrations;
using Xunit;

namespace PlanningPoker.Integrations.Tests;

public class AzureDevOpsIssueTrackerTests
{
    private const string Base = "https://dev.azure.com/acme";
    private static readonly TrackerConnection Conn =
        new(IntegrationProvider.AzureDevOps, Base, null, "pat");

    private static AzureDevOpsIssueTracker Build(StubHttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), new TrackerHostPolicy(), new HtmlDescriptionSanitizer());

    [Fact]
    public async Task Validate_returns_the_authenticated_user_name()
    {
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/_apis/connectionData", HttpStatusCode.OK,
                """{ "authenticatedUser": { "providerDisplayName": "Grace Hopper" } }""");

        var result = await Build(handler).ValidateAsync(Conn);

        result.Ok.Should().BeTrue();
        result.AccountName.Should().Be("Grace Hopper");
    }

    [Fact]
    public async Task GetIssue_returns_title_description_and_story_points()
    {
        var json = """
            {
              "id": 1234,
              "fields": {
                "System.Title": "Login page",
                "System.Description": "<div>Build the <b>login</b>.</div>",
                "Microsoft.VSTS.Scheduling.StoryPoints": 5
              },
              "_links": { "html": { "href": "https://dev.azure.com/acme/_workitems/edit/1234" } }
            }
            """;
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/_apis/wit/workitems/", HttpStatusCode.OK, json);

        var issue = await Build(handler).GetIssueAsync(Conn, "1234");

        issue.Key.Should().Be("1234");
        issue.Title.Should().Be("Login page");
        issue.Description.Should().Contain("Build the <b>login</b>."); // HTML formatting preserved + sanitised
        issue.CurrentStoryPoints.Should().Be(5);
        issue.StoryPointsFieldId.Should().Be("Microsoft.VSTS.Scheduling.StoryPoints");
        issue.Url.Should().Be("https://dev.azure.com/acme/_workitems/edit/1234");
    }

    [Fact]
    public async Task GetIssue_appends_acceptance_criteria_under_a_subtitle()
    {
        var json = """
            {
              "id": 1234,
              "fields": {
                "System.Title": "Login page",
                "System.Description": "<div>Build the login.</div>",
                "Microsoft.VSTS.Common.AcceptanceCriteria": "<ul><li>Given a user</li></ul>"
              }
            }
            """;
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/_apis/wit/workitems/", HttpStatusCode.OK, json);

        var issue = await Build(handler).GetIssueAsync(Conn, "1234");

        issue.Description.Should().Contain("Build the login.");
        issue.Description.Should().Contain("<h4>Acceptance criteria</h4>");
        issue.Description.Should().Contain("Given a user");
    }

    [Fact]
    public async Task SetStoryPoints_sends_a_json_patch()
    {
        var handler = new StubHttpMessageHandler()
            .Map("PATCH", "/_apis/wit/workitems/", HttpStatusCode.OK, """{ "id": 1234 }""");

        await Build(handler).SetStoryPointsAsync(Conn, "1234", 8);

        handler.RequestBodies.Should().Contain(b =>
            b.Contains("Microsoft.VSTS.Scheduling.StoryPoints") && b.Contains("\"op\":\"add\"") && b.Contains("8"));
    }

    [Fact]
    public async Task Search_runs_a_shared_query_then_batch_fetches_work_items()
    {
        var wiqlJson = """{ "workItems": [ { "id": 11 }, { "id": 22 } ] }""";
        var batchJson = """
            { "value": [
              { "id": 11, "fields": { "System.Title": "First", "System.State": "New", "Microsoft.VSTS.Scheduling.StoryPoints": 3 } },
              { "id": 22, "fields": { "System.Title": "Second", "System.State": "Active" } }
            ] }
            """;
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/_apis/wit/wiql/", HttpStatusCode.OK, wiqlJson)
            .Map("GET", "/_apis/wit/workitems", HttpStatusCode.OK, batchJson);

        var results = await Build(handler).SearchAsync(
            Conn, new AdoSharedQuery("MyProject", "12345678-1234-1234-1234-123456789abc"), 100);

        results.Select(r => r.Key).Should().Equal("11", "22");
        results[0].Title.Should().Be("First");
        results[0].Status.Should().Be("New");
        results[0].StoryPoints.Should().Be(3);
        // The batch fetch was asked for both ids.
        handler.Requests.Should().Contain(r => r.RequestUri!.Query.Contains("ids=11,22"));
    }

    [Fact]
    public async Task GetIssue_with_field_override_resolves_display_name_to_reference_name()
    {
        var conn = Conn with { StoryPointsFieldOverride = "Effort" };
        var fieldsJson = """{ "value": [ { "name": "Effort", "referenceName": "Custom.Effort" } ] }""";
        var json = """{ "id": 1234, "fields": { "System.Title": "X", "Custom.Effort": 7 } }""";
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/_apis/wit/fields", HttpStatusCode.OK, fieldsJson)
            .Map("GET", "/_apis/wit/workitems/", HttpStatusCode.OK, json);

        var issue = await Build(handler).GetIssueAsync(conn, "1234");

        issue.StoryPointsFieldId.Should().Be("Custom.Effort");
        issue.CurrentStoryPoints.Should().Be(7);
    }

    [Fact]
    public async Task GetIssue_with_unknown_field_override_trusts_it_verbatim()
    {
        // Not present in the org field list — fall back to using the value as a reference name.
        var conn = Conn with { StoryPointsFieldOverride = "Custom.Points" };
        var fieldsJson = """{ "value": [ { "name": "Effort", "referenceName": "Custom.Effort" } ] }""";
        var json = """{ "id": 1234, "fields": { "System.Title": "X", "Custom.Points": 9 } }""";
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/_apis/wit/fields", HttpStatusCode.OK, fieldsJson)
            .Map("GET", "/_apis/wit/workitems/", HttpStatusCode.OK, json);

        var issue = await Build(handler).GetIssueAsync(conn, "1234");

        issue.StoryPointsFieldId.Should().Be("Custom.Points");
        issue.CurrentStoryPoints.Should().Be(9);
    }

    [Fact]
    public async Task GetIssue_maps_404_to_not_found()
    {
        var handler = new StubHttpMessageHandler()
            .Map("GET", "/_apis/wit/workitems/", HttpStatusCode.NotFound, null);

        var act = () => Build(handler).GetIssueAsync(Conn, "9999");

        (await act.Should().ThrowAsync<TrackerException>()).Which.Kind.Should().Be(TrackerErrorKind.NotFound);
    }
}
