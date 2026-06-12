using System.Net;
using PlanningPoker.Core.Integrations;

namespace PlanningPoker.Integrations;

/// <summary>
/// SSRF guard for user-supplied tracker base URLs: HTTPS only, host on the allowlist, no
/// private/loopback targets. See #4.
/// </summary>
public interface ITrackerHostPolicy
{
    /// <summary>Throws <see cref="TrackerException"/> if the base URL is not permitted.</summary>
    Uri Validate(string baseUrl);
}

public sealed class TrackerHostPolicy : ITrackerHostPolicy
{
    private readonly IReadOnlyList<string> _allowedSuffixes;

    /// <param name="allowedHostSuffixes">e.g. "atlassian.net", "dev.azure.com", "visualstudio.com".</param>
    public TrackerHostPolicy(IEnumerable<string>? allowedHostSuffixes = null)
    {
        _allowedSuffixes = (allowedHostSuffixes ?? new[] { "atlassian.net", "atlassian.com", "dev.azure.com", "visualstudio.com" })
            .Select(s => s.Trim().TrimStart('.').ToLowerInvariant())
            .Where(s => s.Length > 0)
            .ToArray();
    }

    public Uri Validate(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new TrackerException(TrackerErrorKind.InvalidRequest, "The tracker URL is not a valid absolute URL.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new TrackerException(TrackerErrorKind.InvalidRequest, "The tracker URL must use HTTPS.");
        }

        var host = uri.Host.ToLowerInvariant();

        if (IsPrivateOrLoopback(host))
        {
            throw new TrackerException(TrackerErrorKind.InvalidRequest, "The tracker host is not permitted.");
        }

        var allowed = _allowedSuffixes.Any(suffix =>
            host == suffix || host.EndsWith("." + suffix, StringComparison.Ordinal));
        if (!allowed)
        {
            throw new TrackerException(TrackerErrorKind.InvalidRequest,
                $"'{uri.Host}' is not an allowed tracker host.");
        }

        return uri;
    }

    private static bool IsPrivateOrLoopback(string host)
    {
        if (host is "localhost" or "127.0.0.1" or "::1")
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            var bytes = ip.GetAddressBytes();
            if (IPAddress.IsLoopback(ip)) return true;
            if (bytes.Length == 4)
            {
                // 10/8, 172.16/12, 192.168/16, 169.254/16
                return bytes[0] == 10
                    || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    || (bytes[0] == 192 && bytes[1] == 168)
                    || (bytes[0] == 169 && bytes[1] == 254);
            }
        }

        return false;
    }
}
