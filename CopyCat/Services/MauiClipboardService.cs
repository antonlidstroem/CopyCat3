namespace CopyCat.Services;

public class MauiClipboardService : IClipboardService
{
    // Android's Binder IPC hard limit is ~1 MB. 800 KB is a safe ceiling.
    // At 4 chars/token this is ~200 000 tokens — above any realistic slider value.
    private const int MaxClipboardChars = 800_000;

    public async Task SetTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var payload = text.Length <= MaxClipboardChars
            ? text
            : text[..MaxClipboardChars] +
              $"\n\n[⚠️ Urklipp trunkerat: innehållet översteg {MaxClipboardChars:N0} tecken]";

        await Clipboard.Default.SetTextAsync(payload);
    }

    public async Task ShareAsync(string text, string title)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // Cap at the same limit as clipboard for consistency.
        var payload = text.Length <= MaxClipboardChars
            ? text
            : text[..MaxClipboardChars] +
              $"\n\n[⚠️ Trunkerat: innehållet översteg {MaxClipboardChars:N0} tecken]";

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Text  = payload,
            Title = title,
        });
    }
}
