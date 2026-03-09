namespace CopyCat.Services;

public interface IShareService
{
    /// <summary>
    /// Öppnar OS:ets share sheet med den angivna texten.
    /// På Android används ACTION_SEND, på iOS UIActivityViewController.
    /// </summary>
    Task ShareTextAsync(string text, string title);
}
