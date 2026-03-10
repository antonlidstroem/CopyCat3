namespace CopyCat.Services;

public class MauiShareService : IShareService
{
    public Task ShareTextAsync(string text, string title) =>
        Share.Default.RequestAsync(new ShareTextRequest
        {
            Text  = text,
            Title = title
        });
}
