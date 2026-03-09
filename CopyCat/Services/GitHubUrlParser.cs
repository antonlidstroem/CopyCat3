using System.Text.RegularExpressions;
namespace CopyCat.Services;
internal static partial class GitHubUrlParser
{
    [GeneratedRegex(
        @"github\.com/(?<owner>[^/?#]+)/(?<repo>[^/?#.]+?)(?:\.git)?(?:[/?#].*)?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex RepoUrlRegex();
    public static (string Owner, string Repo) Parse(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Ange en giltig GitHub-URL.", nameof(url));
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
