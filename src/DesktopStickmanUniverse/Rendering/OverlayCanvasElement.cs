using System.Windows;
using System.Windows.Media;

namespace DesktopStickmanUniverse.Rendering;

/// <summary>
/// Lightweight Visual host rendering via a single DrawingVisual with zero XAML control overhead.
/// </summary>
public sealed class OverlayCanvasElement : FrameworkElement
{
    private readonly DrawingVisual _visual = new();
    private readonly VisualCollection _children;

    public OverlayCanvasElement()
    {
        _children = new VisualCollection(this) { _visual };
        IsHitTestVisible = true;
    }

    protected override int VisualChildrenCount => _children.Count;

    protected override Visual GetVisualChild(int index) => _children[index];

    public DrawingContext RenderOpen() => _visual.RenderOpen();
}
