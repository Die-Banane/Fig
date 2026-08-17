using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Fig.App.Services;

namespace Fig.App.Controls;

/// <summary>
/// Compact editor slider with a thin value rail and a small circular thumb. The generous
/// transparent hit area keeps it easy to grab without making the control visually heavy.
/// </summary>
public sealed class FigSlider : Border
{
    public static readonly StyledProperty<double> MinimumProperty = RangeBase.MinimumProperty.AddOwner<FigSlider>();
    public static readonly StyledProperty<double> MaximumProperty = RangeBase.MaximumProperty.AddOwner<FigSlider>();
    public static readonly StyledProperty<double> ValueProperty = RangeBase.ValueProperty.AddOwner<FigSlider>();

    private const double TrackHeight = 3;
    private const double ThumbSize = 10;

    private readonly Border _track;
    private readonly Border _fill;
    private readonly Ellipse _thumb;
    private readonly Grid _hitArea;
    private readonly ScaleTransform _fillScale = new(0, 1);
    private readonly TranslateTransform _thumbTranslate = new(0, 0);
    private bool _dragging;

    static FigSlider()
    {
        MinimumProperty.OverrideDefaultValue<FigSlider>(0);
        MaximumProperty.OverrideDefaultValue<FigSlider>(100);
        ValueProperty.OverrideDefaultValue<FigSlider>(0);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public FigSlider()
    {
        MinHeight = 18;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);

        _fill = new Border
        {
            Background = EditorTheme.AccentBrush,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            RenderTransform = _fillScale,
            RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            IsHitTestVisible = false,
        };
        _track = new Border
        {
            Height = TrackHeight,
            Background = new SolidColorBrush(Color.Parse("#44474d")),
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
            Child = _fill,
            IsHitTestVisible = false,
        };
        _thumb = new Ellipse
        {
            Width = ThumbSize,
            Height = ThumbSize,
            Fill = new SolidColorBrush(Color.Parse("#d9dce1")),
            Stroke = new SolidColorBrush(Color.Parse("#17181a")),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = _thumbTranslate,
            IsHitTestVisible = false,
        };

        _hitArea = new Grid { Background = Brushes.Transparent };
        _hitArea.Children.Add(_track);
        _hitArea.Children.Add(_thumb);
        _hitArea.PointerPressed += OnPressed;
        _hitArea.PointerMoved += OnMoved;
        _hitArea.PointerReleased += OnReleased;
        _hitArea.PointerCaptureLost += (_, _) => _dragging = false;
        Child = _hitArea;

        AttachedToVisualTree += (_, _) => ApplyVisuals();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyVisuals();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == MinimumProperty || change.Property == MaximumProperty)
            ApplyVisuals();
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_hitArea).Properties.IsLeftButtonPressed)
            return;
        _dragging = true;
        SetFromPointer(e.GetPosition(_hitArea).X);
        e.Pointer.Capture(_hitArea);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
            return;
        SetFromPointer(e.GetPosition(_hitArea).X);
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void SetFromPointer(double x)
    {
        var width = Math.Max(1, _hitArea.Bounds.Width - ThumbSize);
        var t = Math.Clamp((x - ThumbSize / 2) / width, 0, 1);
        var next = Minimum + t * (Maximum - Minimum);
        SetCurrentValue(ValueProperty, Math.Clamp(next, Minimum, Maximum));
    }

    private void ApplyVisuals()
    {
        var range = Math.Max(0.0001, Maximum - Minimum);
        var t = Math.Clamp((Value - Minimum) / range, 0, 1);
        _fillScale.ScaleX = t;
        var travel = Math.Max(0, _hitArea.Bounds.Width - ThumbSize);
        _thumbTranslate.X = t * travel;
    }
}
