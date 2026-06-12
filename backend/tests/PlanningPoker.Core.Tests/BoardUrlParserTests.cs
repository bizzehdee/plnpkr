using FluentAssertions;
using PlanningPoker.Core.Integrations;
using Xunit;

namespace PlanningPoker.Core.Tests;

public class BoardUrlParserTests
{
    private readonly BoardUrlParser _sut = new();

    [Fact]
    public void Parses_a_jira_board_url()
    {
        var q = _sut.Parse("https://acme.atlassian.net/jira/software/projects/PROJ/boards/123");
        q.Should().BeOfType<JiraBoardQuery>().Which.BoardId.Should().Be("123");
    }

    [Fact]
    public void Parses_a_classic_rapidview_board_url()
    {
        var q = _sut.Parse("https://acme.atlassian.net/secure/RapidBoard.jspa?rapidView=77");
        q.Should().BeOfType<JiraBoardQuery>().Which.BoardId.Should().Be("77");
    }

    [Fact]
    public void Parses_a_jira_saved_filter_url()
    {
        var q = _sut.Parse("https://acme.atlassian.net/issues/?filter=10001");
        q.Should().BeOfType<JiraFilterQuery>().Which.FilterId.Should().Be("10001");
    }

    [Fact]
    public void Parses_a_jira_jql_url()
    {
        var q = _sut.Parse("https://acme.atlassian.net/issues/?jql=project%20%3D%20PROJ");
        q.Should().BeOfType<JqlQuery>().Which.Jql.Should().Be("project = PROJ");
    }

    [Fact]
    public void Parses_an_azure_devops_shared_query_url()
    {
        var q = _sut.Parse("https://dev.azure.com/acme/MyProject/_queries/query/12345678-1234-1234-1234-123456789abc/");
        var ado = q.Should().BeOfType<AdoSharedQuery>().Subject;
        ado.Project.Should().Be("MyProject");
        ado.QueryId.Should().Be("12345678-1234-1234-1234-123456789abc");
    }

    [Theory]
    [InlineData("https://acme.atlassian.net/jira/dashboards")]
    [InlineData("https://acme.atlassian.net/issues/?filter=not-a-number")] // non-numeric filter → no match
    [InlineData("https://dev.azure.com/acme/MyProject/_boards/board")]     // ADO board (not a query) → no match
    [InlineData("not-a-url")]
    [InlineData("")]
    public void Returns_null_for_unrecognised_input(string url)
    {
        _sut.Parse(url).Should().BeNull();
    }

    [Fact]
    public void Ado_query_path_on_a_non_azure_host_is_ignored()
    {
        // Same path shape but not an Azure host → not treated as an ADO query.
        _sut.Parse("https://example.com/acme/MyProject/_queries/query/12345678-1234-1234-1234-123456789abc")
            .Should().BeNull();
    }
}
