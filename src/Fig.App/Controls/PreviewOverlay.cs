using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;

namespace Fig.App.Controls;

/// <summary>A view model drawn over the preview picture: it says how wide the picture is for its height.</summary>
public interface IPreviewOverlayModel : INotifyPropertyChanged
{
    double FrameAspect { get; }
}

/// <summary>
/// What every on-preview control has in common: its view model (from the data context; any change redraws), where
/// the letterboxed picture sits inside the control, and the mapping between frame points (0..1) and the screen.
/// </summary>
public abstract class PreviewOverlay<TModel> : Control where TModel : class, IPreviewOverlayModel
{
    protected TModel? Model { get; private set; }

    protected PreviewOverlay()
    {
        ClipToBounds = true;
        Focusable = false;
        DataContextChanged += (_, _) =>
        {
            if (Model is not null)
                Model.PropertyChanged -= OnModelChanged;
            Model = DataContext as TModel;
            if (Model is not null)
                Model.PropertyChanged += OnModelChanged;
            InvalidateVisual();
        };
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    /// <summary>Where the picture sits inside this control (letterboxed to keep its aspect).</summary>
    protected Rect PictureRect()
    {
        var aspect = Model?.FrameAspect > 0 ? Model.FrameAspect : 16.0 / 9;
        var w = Bounds.Width;
        var h = w / aspect;
        if (h > Bounds.Height)
        {
            h = Bounds.Height;
            w = h * aspect;
        }
        return new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
    }

    /// <summary>A frame point (0..1 across and down) on screen.</summary>
    protected static Point ToScreen(Rect picture, (double X, double Y) p)
        => new(picture.X + p.X * picture.Width, picture.Y + p.Y * picture.Height);

    /// <summary>A screen point as a frame point (0..1 across and down).</summary>
    protected static (double X, double Y) ToFrame(Rect picture, Point p)
        => ((p.X - picture.X) / picture.Width, (p.Y - picture.Y) / picture.Height);
}
