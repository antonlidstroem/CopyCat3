using SQLite;

namespace CopyCat.Models;

[Table("SavedRepos")]
public class SavedRepo
{
    [PrimaryKey, AutoIncrement]
    public int    Id       { get; set; }
    public string Name     { get; set; } = string.Empty;
    public string Url      { get; set; } = string.Empty;
    public string Branch   { get; set; } = string.Empty;
    public bool   HasToken { get; set; }
    public long   LastUsed { get; set; }

    // C6: Workspace snapshot columns
    public int    SavedMaxTokens        { get; set; } = 0;
    public string SavedEnabledExts      { get; set; } = string.Empty;
    public string SavedExcludedFolders  { get; set; } = string.Empty;
    public string SavedPatterns         { get; set; } = string.Empty;
    public int    SavedPromptSortOrder  { get; set; } = -1;

    [Ignore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name)
            ? TrimmedUrl
            : $"{Name}  ·  {TrimmedUrl}";

    public override string ToString() => DisplayName;

    [Ignore]
    public bool HasWorkspace =>
        SavedMaxTokens > 0 ||
        !string.IsNullOrEmpty(SavedEnabledExts) ||
        !string.IsNullOrEmpty(SavedExcludedFolders) ||
        !string.IsNullOrEmpty(SavedPatterns) ||
        SavedPromptSortOrder >= 0;

    [Ignore]
    private string TrimmedUrl =>
        Url.Replace("https://github.com/", "").TrimEnd('/');
}
