namespace CopyCat.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text);
}
