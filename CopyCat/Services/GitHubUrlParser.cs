using System.Text.RegularExpressions;

namespace CopyCat.Services;

internal static partial class GitHubUrlParser
{
    // Matches github.com/owner/repo — tolerates trailing slashes, query strings,
    // fragment identifiers, paths under the repo (e.g. /tree/main/src), and .git suffix.
    [GeneratedRegex(
        @"github\.com/(?<owner>[^/?#]+)/(?<repo>[^/?#.]+?)(?:\.git)?(?:[/?#].*)?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex RepoUrlRegex();

    public static (string Owner, string Repo) Parse(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Ange en giltig GitHub-URL.", nameof(url));

        // Normalise: strip leading/trailing whitespace and trailing slashes.
        var normalised = url.Trim().TrimEnd('/');

        var match = RepoUrlRegex().Match(normalised);
        if (!match.Success)
            throw new ArgumentException(
                "Ogiltig GitHub-URL. Exempel: https://github.com/owner/repo",
                nameof(url));

        return (
            match.Groups["owner"].Value,
            match.Groups["repo"].Value
        );
    }
}
