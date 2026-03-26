namespace CopyCat.Services;

public class MauiClipboardService : IClipboardService
{
    private const int MaxClipboardChars = 800_000;

    public async Task SetTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var payload = text.Length <= MaxClipboardChars
            ? text
            : text[..MaxClipboardChars] +
              $"\n\n[⚠️ Clipboard truncated: content exceeded {MaxClipboardChars:N0} characters]";

        await Clipboard.Default.SetTextAsync(payload);
    }

    public async Task ShareAsync(string text, string title)
    {
        if (string.IsNullOrEmpty(text)) return;

        var payload = text.Length <= MaxClipboardChars
            ? text
            : text[..MaxClipboardChars] +
              $"\n\n[⚠️ Truncated: content exceeded {MaxClipboardChars:N0} characters]";

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Text  = payload,
            Title = title,
        });
    }
}

public class MauiShareService : IShareService
{
    public Task ShareTextAsync(string text, string title) =>
        Share.Default.RequestAsync(new ShareTextRequest
        {
            Text  = text,
            Title = title
        });
}
