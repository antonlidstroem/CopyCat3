using System.Text.RegularExpressions;

namespace CopyCat.Services;

internal static class GitHubUrlParser
{
    private static readonly Regex RepoUrlRegex =
        new(@"github\.com/(?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?(?:/.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static (string Owner, string Repo) Parse(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Ange en giltig GitHub-URL.", nameof(url));

        var match = RepoUrlRegex.Match(url);
        if (!match.Success)
            throw new ArgumentException(
                "Ogiltig GitHub-URL. Exempel: https://github.com/owner/repo",
                nameof(url));

        return (
            match.Groups["owner"].Value,
            match.Groups["repo"].Value.TrimEnd('/')
        );
    }
}
