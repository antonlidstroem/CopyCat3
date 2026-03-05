namespace CopyCat.Services;

public class MauiClipboardService : IClipboardService
{
    public Task SetTextAsync(string text) =>
        Clipboard.Default.SetTextAsync(text);
}
