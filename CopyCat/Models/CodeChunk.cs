using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace CopyCat.Models;

public partial class CodeChunk : ObservableObject
{
    // ── Immutable data set by ChunkingService ─────────────────────────────

    public int    Index           { get; set; }
    public string ProjectName     { get; set; } = string.Empty;
    public string Content         { get; set; } = string.Empty;
    public int    EstimatedTokens { get; set; }

    // ── Observable state ──────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBackgroundColor))]
    [NotifyPropertyChangedFor(nameof(CardBorderColor))]
    [NotifyPropertyChangedFor(nameof(CopyButtonTextColor))]
    private bool _isCopied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBackgroundColor))]
    [NotifyPropertyChangedFor(nameof(CardBorderColor))]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewToggleIcon))]
    private bool _isPreviewExpanded;

    partial void OnIsPreviewExpandedChanged(bool value)
    {
        if (!value) return;
        if (_files is null) _files = ParseAndInitFiles();

        OnPropertyChanged(nameof(Files));
        OnPropertyChanged(nameof(DisplayFiles));
        OnPropertyChanged(nameof(HiddenFileCount));
        OnPropertyChanged(nameof(SubLabel));
        OnPropertyChanged(nameof(IncludedFileCountLabel));
        OnPropertyChanged(nameof(IncludedTokens));
        OnPropertyChanged(nameof(IncludedFileCount));
        OnPropertyChanged(nameof(TotalFileCount));
    }

    // ── Card colours (match Styles.xaml tokens) ───────────────────────────

    public Color CardBackgroundColor =>
        IsCopied   ? Color.FromArgb("#1A2E20") :
        IsSelected ? Color.FromArgb("#17213A") :
                     Color.FromArgb("#070F12");

    public Color CardBorderColor =>
        IsCopied   ? Color.FromArgb("#008C8B") :
        IsSelected ? Color.FromArgb("#F59E0B") :
                     Color.FromArgb("#006770");

    public Color CopyButtonTextColor =>
        IsCopied ? Color.FromArgb("#008C8B") : Color.FromArgb("#00A3A9");

    // ── Labels ────────────────────────────────────────────────────────────

    public string DisplayLabel => $"Chunk {Index + 1}  ·  {ProjectName}";

    public string SubLabel
    {
        get
        {
            if (_files is null)
                return $"~{EstimatedTokens:N0} tokens";

            int incTokens = IncludedTokens;
            int incCount  = IncludedFileCount;
            int totCount  = TotalFileCount;

            return incCount == totCount
                ? $"~{incTokens:N0} tokens  ·  {totCount} files"
                : $"~{incTokens:N0} tokens  ·  {incCount} / {totCount} files";
        }
    }

    public string PreviewToggleIcon => IsPreviewExpanded ? "▲" : "▼";

    // ── Per-file deselection ───────────────────────────────────────────────

    private ObservableCollection<CodeChunkFile>? _files;

    /// <summary>All parsed file sections (lazy, full list).</summary>
    public ObservableCollection<CodeChunkFile> Files
    {
        get
        {
            if (_files is null) _files = ParseAndInitFiles();
            return _files;
        }
    }

    // FIX 3 — BindableLayout performance: expose a capped view so the XAML
    // never renders more than MaxDisplayFiles rows. For chunks with more
    // files a footer label shows the hidden count. Typical chunks have
    // 2–20 files; this only bites at very small token limits (< 2 K).
    private const int MaxDisplayFiles = 50;

    /// <summary>
    /// The subset of <see cref="Files"/> shown in the preview panel.
    /// Capped at <c>50</c> to keep BindableLayout render time bounded.
    /// </summary>
    public IReadOnlyList<CodeChunkFile> DisplayFiles =>
        Files.Count <= MaxDisplayFiles
            ? (IReadOnlyList<CodeChunkFile>)Files
            : Files.Take(MaxDisplayFiles).ToList();

    /// <summary>
    /// Number of files not shown in the preview panel (0 when ≤ 50 files).
    /// Bound to a footer label: "… and 12 more files (not shown)"
    /// </summary>
    public int HiddenFileCount => Math.Max(0, Files.Count - MaxDisplayFiles);

    // ── Included-content helpers ───────────────────────────────────────────

    /// <summary>
    /// The text that goes to clipboard / share.
    ///
    /// FIX 6 — memory: we never stored substring copies.  Each CodeChunkFile
    /// holds (ContentStart, ContentLength) offsets into this chunk's Content.
    /// We slice the original string here, so only ONE allocation happens per
    /// Copy press — not one per file section at parse-time.
    ///
    /// Fast paths:
    ///   • Preview never opened → return Content unchanged (zero overhead).
    ///   • All files included   → return Content unchanged (no allocation).
    /// </summary>
    public string IncludedContent
    {
        get
        {
            if (_files is null) return Content;

            var included = _files.Where(f => f.IsIncluded).ToList();
            if (included.Count == _files.Count) return Content;    // nothing deselected
            if (included.Count == 0)            return string.Empty;

            var sb = new StringBuilder(Content.Length);
            foreach (var f in included)
                sb.Append(Content, f.ContentStart, f.ContentLength);
            return sb.ToString();
        }
    }

    public int IncludedTokens =>
        _files is null
            ? EstimatedTokens
            : _files.Where(f => f.IsIncluded).Sum(f => f.EstimatedTokens);

    public int IncludedFileCount => _files?.Count(f => f.IsIncluded) ?? 0;
    public int TotalFileCount    => _files?.Count ?? 0;

    public string IncludedFileCountLabel
    {
        get
        {
            if (_files is null || _files.Count == 0) return string.Empty;
            int inc = IncludedFileCount;
            int tot = TotalFileCount;
            return inc == tot ? $"{tot} files" : $"{inc} / {tot} files included";
        }
    }

    // ── Parse helpers ──────────────────────────────────────────────────────

    private ObservableCollection<CodeChunkFile> ParseAndInitFiles()
    {
        var col     = new ObservableCollection<CodeChunkFile>();
        var matches = HeaderRegex().Matches(Content);

        // Content before the first ==== header ==== is an oversized-file
        // continuation block — treat it as an unnamed section.
        bool hasPreamble = matches.Count == 0 || matches[0].Index > 0;
        if (hasPreamble)
        {
            int pStart = 0;
            int pEnd   = matches.Count > 0 ? matches[0].Index : Content.Length;
            if (pEnd > pStart && !string.IsNullOrWhiteSpace(Content[pStart..pEnd]))
                col.Add(Build("(continued)", pStart, pEnd - pStart));
        }

        for (int i = 0; i < matches.Count; i++)
        {
            var match    = matches[i];
            int blockEnd = i + 1 < matches.Count ? matches[i + 1].Index : Content.Length;
            int start    = match.Index;
            int length   = blockEnd - start;

            var rawPath = match.Groups["path"].Value.Trim().Replace('\\', '/');
            var leaf    = rawPath.Contains('/')
                ? rawPath[(rawPath.LastIndexOf('/') + 1)..]
                : rawPath;

            col.Add(Build(rawPath, start, length,
                          string.IsNullOrWhiteSpace(leaf) ? rawPath : leaf));
        }

        return col;
    }

    // FIX 6: Build takes offsets, not a pre-sliced string.
    private CodeChunkFile Build(string path, int start, int length, string? leafName = null)
    {
        var f = new CodeChunkFile
        {
            FilePath        = path,
            FileName        = leafName ?? path,
            ContentStart    = start,
            ContentLength   = length,
            EstimatedTokens = (int)Math.Ceiling(length / 4.0),
            IsIncluded      = true,
        };
        f.PropertyChanged += OnFilePropertyChanged;
        return f;
    }

    private void OnFilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CodeChunkFile.IsIncluded)) return;
        OnPropertyChanged(nameof(IncludedContent));
        OnPropertyChanged(nameof(IncludedTokens));
        OnPropertyChanged(nameof(IncludedFileCount));
        OnPropertyChanged(nameof(SubLabel));
        OnPropertyChanged(nameof(IncludedFileCountLabel));
    }

    // FIX 4: PreviewSnippet removed — it was only used by the old static
    // Label in the preview panel. The interactive BindableLayout now binds
    // to DisplayFiles directly and PreviewSnippet was dead code.
    // HeaderRegex is kept because ParseAndInitFiles still needs it.

    [GeneratedRegex(@"^====\s+(?<path>.+?)\s+====$", RegexOptions.Multiline)]
    private static partial Regex HeaderRegex();
}
