using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NAP.App.Controls;

/// <summary>Uniform full-width tile grid. Generates only viewport rows; uses the WPF recycling generator.</summary>
public sealed class VirtualizingTilePanel : VirtualizingPanel, IScrollInfo
{
    private double _offset, _extent, _viewport, _width;
    private int _columns = 1;
    private double TileWidth => (double)FindResource("TileWidth");
    private double TileHeight => (double)FindResource("TileHeight");
    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this); if (owner is null) return availableSize;
        _width = double.IsInfinity(availableSize.Width) ? TileWidth : Math.Max(1, availableSize.Width);
        _viewport = double.IsInfinity(availableSize.Height) ? TileHeight * 3 : Math.Max(1, availableSize.Height);
        _columns = Math.Max(1, (int)(_width / TileWidth));
        _extent = Math.Ceiling((double)owner.Items.Count / _columns) * TileHeight;
        _offset = Math.Clamp(_offset, 0, Math.Max(0, _extent - _viewport));
        var first = Math.Min(owner.Items.Count, (int)(_offset / TileHeight) * _columns);
        var last = Math.Min(owner.Items.Count - 1, ((int)Math.Ceiling((_offset + _viewport) / TileHeight)) * _columns - 1);
        // Accessing InternalChildren initializes the host generator on the first layout pass.
        var children = InternalChildren;
        var generator = ItemContainerGenerator;
        if (generator is null) return new(_width, _viewport);
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var index = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (index >= first && index <= last) continue;
            if (generator is IRecyclingItemContainerGenerator recycling) recycling.Recycle(new(i, 0), 1); else generator.Remove(new(i, 0), 1);
            RemoveInternalChildRange(i, 1);
        }
        var position = generator.GeneratorPositionFromIndex(first);
        var childIndex = position.Offset == 0 ? position.Index : position.Index + 1;
        using (generator.StartAt(position, GeneratorDirection.Forward, true))
            for (var item = first; item <= last; item++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out var isNew);
                if (isNew || !children.Contains(child))
                {
                    if (childIndex >= InternalChildren.Count) AddInternalChild(child); else InsertInternalChild(childIndex, child);
                    generator.PrepareItemContainer(child);
                }
                child.Measure(new Size(_width / _columns, TileHeight));
            }
        ScrollOwner?.InvalidateScrollInfo(); return new(_width, _viewport);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var index = ItemContainerGenerator.IndexFromGeneratorPosition(new(i, 0));
            InternalChildren[i].Arrange(new Rect(index % _columns * _width / _columns, index / _columns * TileHeight - _offset, _width / _columns, TileHeight));
        }
        return finalSize;
    }
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    { if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) { RemoveInternalChildRange(0, InternalChildren.Count); _offset = 0; } InvalidateMeasure(); }
    protected override void BringIndexIntoView(int index) => SetVerticalOffset(index / _columns * TileHeight);
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var child = visual as DependencyObject;
        while (child is not null && VisualTreeHelper.GetParent(child) != this) child = VisualTreeHelper.GetParent(child);
        if (child is UIElement element && InternalChildren.Contains(element))
        {
            var index = ItemContainerGenerator.IndexFromGeneratorPosition(new(InternalChildren.IndexOf(element), 0)); var top = index / _columns * TileHeight;
            if (top < _offset) SetVerticalOffset(top); else if (top + TileHeight > _offset + _viewport) SetVerticalOffset(top + TileHeight - _viewport);
        }
        return rectangle;
    }
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; } = true;
    public double ExtentHeight => _extent;
    public double ExtentWidth => _width;
    public double ViewportHeight => _viewport;
    public double ViewportWidth => _width;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _offset;
    public ScrollViewer? ScrollOwner { get; set; }
    public void SetVerticalOffset(double offset) { _offset = Math.Clamp(offset, 0, Math.Max(0, _extent - _viewport)); InvalidateMeasure(); ScrollOwner?.InvalidateScrollInfo(); }
    public void SetHorizontalOffset(double offset) { }
    public void LineUp() => SetVerticalOffset(_offset - TileHeight / 3);
    public void LineDown() => SetVerticalOffset(_offset + TileHeight / 3);
    public void MouseWheelUp() => SetVerticalOffset(_offset - TileHeight);
    public void MouseWheelDown() => SetVerticalOffset(_offset + TileHeight);
    public void PageUp() => SetVerticalOffset(_offset - _viewport);
    public void PageDown() => SetVerticalOffset(_offset + _viewport);
    public void LineLeft() { } public void LineRight() { } public void MouseWheelLeft() { } public void MouseWheelRight() { } public void PageLeft() { } public void PageRight() { }
}
