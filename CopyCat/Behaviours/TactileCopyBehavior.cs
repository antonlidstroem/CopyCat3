using CopyCat.Models;
using System.ComponentModel;

namespace CopyCat.Behaviors;

/// <summary>
/// Attach to a chunk-card Border to get a tactile scale-up animation
/// whenever the bound <see cref="CodeChunk.IsCopied"/> property turns true.
/// </summary>
public class TactileCopyBehavior : Behavior<Border>
{
    private Border?    _border;
    private CodeChunk? _chunk;

    protected override void OnAttachedTo(Border border)
    {
        base.OnAttachedTo(border);
        _border = border;
        border.BindingContextChanged += OnBindingContextChanged;
        TrySubscribeChunk(border.BindingContext);
    }

    protected override void OnDetachingFrom(Border border)
    {
        border.BindingContextChanged -= OnBindingContextChanged;
        TryUnsubscribeChunk();
        _border = null;
        base.OnDetachingFrom(border);
    }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        TryUnsubscribeChunk();
        TrySubscribeChunk(_border?.BindingContext);
    }

    private void TrySubscribeChunk(object? context)
    {
        if (context is CodeChunk chunk)
        {
            _chunk = chunk;
            _chunk.PropertyChanged += OnChunkPropertyChanged;
        }
    }

    private void TryUnsubscribeChunk()
    {
        if (_chunk is not null)
        {
            _chunk.PropertyChanged -= OnChunkPropertyChanged;
            _chunk = null;
        }
    }

    private async void OnChunkPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CodeChunk.IsCopied)) return;
        if (_chunk?.IsCopied != true)                      return;
        if (_border is null)                               return;

        await _border.ScaleTo(1.02, 100, Easing.CubicOut);
        await _border.ScaleTo(1.00, 100, Easing.CubicIn);
    }
}
