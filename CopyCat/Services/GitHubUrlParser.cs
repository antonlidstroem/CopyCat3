using System.Text.RegularExpressions;

namespace CopyCat.Services;

internal static partial class GitHubUrlParser
{
    // FIX: Repo-gruppen använde [^/?#.] vilket exkluderade punkter och bröt
    // repos som "dotnet/dotnet.github.io" eller "user/my.app".
    // Ändrat till [^/?#] för att tillåta punkter, men (?:\.git)? fångar
    // fortfarande .git-suffixet korrekt eftersom regex är girigt vänster-till-höger.
    [GeneratedRegex(
        @"github\.com/(?<owner>[^/?#]+)/(?<repo>[^/?#]+?)(?:\.git)?(?:[/?#]|$)",
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
