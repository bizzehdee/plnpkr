using System.Net;

namespace PlanningPoker.Integrations.Tests;

/// <summary>Routes requests to canned responses by (method, path-prefix) so the Jira adapter is
/// tested without any network. Records requests for assertions.</summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(string Method, string PathContains, Func<HttpResponseMessage> Respond)> _routes = new();

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> RequestBodies { get; } = new();

    public StubHttpMessageHandler Map(string method, string pathContains, HttpStatusCode status, string? json)
    {
        _routes.Add((method, pathContains, () =>
        {
            var resp = new HttpResponseMessage(status);
            if (json is not null)
            {
                resp.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            }
            return resp;
        }));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));

        var path = request.RequestUri!.AbsolutePath;
        foreach (var (method, contains, respond) in _routes)
        {
            if (request.Method.Method == method && path.Contains(contains, StringComparison.Ordinal))
            {
                return respond();
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotImplemented)
        {
            Content = new StringContent($"No stub for {request.Method} {path}"),
        };
    }
}

/// <summary>Minimal IHttpClientFactory returning a client bound to the stub handler.</summary>
public sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}
