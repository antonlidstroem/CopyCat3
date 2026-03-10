using SQLite;

namespace CopyCat.Models;

[Table("SavedRepos")]
public class SavedRepo
{
    [PrimaryKey, AutoIncrement]
    public int    Id       { get; set; }

    /// <summary>User-given friendly name. Falls back to trimmed URL if empty.</summary>
    public string Name     { get; set; } = string.Empty;

    public string Url      { get; set; } = string.Empty;
    public string Branch   { get; set; } = string.Empty;

    /// <summary>Whether a GitHub token is stored in SecureStorage for this repo.</summary>
    public bool   HasToken { get; set; }

    /// <summary>Unix timestamp (UTC) of last use.</summary>
    public long   LastUsed { get; set; }

    [Ignore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name)
            ? TrimmedUrl
            : $"{Name}  ·  {TrimmedUrl}";

    public override string ToString() => DisplayName;

    [Ignore]
    private string TrimmedUrl =>
        Url.Replace("https://github.com/", "")
           .TrimEnd('/');
}
