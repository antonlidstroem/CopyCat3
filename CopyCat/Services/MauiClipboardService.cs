namespace CopyCat.Services;

public class MauiClipboardService : IClipboardService
{
    // Android's Binder IPC hard limit is ~1 MB. We use 800 KB as a safe ceiling
    // (leaving room for overhead). At 4 chars/token this equals ~200 000 tokens —
    // far above any realistic slider value, but the guard prevents a silent crash
    // or silent truncation on older/OEM Android builds.
    private const int MaxClipboardChars = 800_000;

    public async Task SetTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        string payload = text.Length <= MaxClipboardChars
            ? text
            : text[..MaxClipboardChars] +
              $"\n\n[⚠️ Urklipp trunkerat: innehållet översteg {MaxClipboardChars:N0} tecken]";

        await Clipboard.Default.SetTextAsync(payload);
    }
}
