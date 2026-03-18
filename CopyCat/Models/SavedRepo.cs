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

    [Ignore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name)
            ? TrimmedUrl
            : $"{Name}  ·  {TrimmedUrl}";

    public override string ToString() => DisplayName;

    [Ignore]
    private string TrimmedUrl =>
        Url.Replace("https://github.com/", "").TrimEnd('/');
}
