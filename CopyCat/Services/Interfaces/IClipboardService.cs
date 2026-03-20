namespace CopyCat.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text);

    /// <summary>
    /// Opens the platform share sheet so the user can send the text
    /// directly to another app (ChatGPT, Claude, Notes, etc.).
    /// Falls back to clipboard copy if sharing is unavailable.
    /// </summary>
    Task ShareAsync(string text, string title);
}
