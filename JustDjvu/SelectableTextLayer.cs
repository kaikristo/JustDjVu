using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace JustDjvu;

public sealed class SelectableTextLayer : FrameworkElement
{
    public static readonly DependencyProperty PageTextProperty =
        DependencyProperty.Register(
            nameof(PageText),
            typeof(PageTextLayer),
            typeof(SelectableTextLayer),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnPageTextChanged));

    private static readonly Brush SelectionBrush = CreateSelectionBrush();
    private readonly MenuItem _copyMenuItem;
    private readonly MenuItem _selectAllMenuItem;
    private int _anchorIndex = -1;
    private int _activeIndex = -1;

    public SelectableTextLayer()
    {
        Focusable = true;
        Cursor = Cursors.IBeam;

        _copyMenuItem = new MenuItem();
        _copyMenuItem.Click += (_, _) => CopySelection();
        _selectAllMenuItem = new MenuItem();
        _selectAllMenuItem.Click += (_, _) => SelectAllText();

        ContextMenu = new ContextMenu();
        ContextMenu.Items.Add(_copyMenuItem);
        ContextMenu.Items.Add(_selectAllMenuItem);
        ContextMenuOpening += (_, _) =>
        {
            _copyMenuItem.Header = LocalizationService.Translate("Копировать выделенный текст");
            _selectAllMenuItem.Header = LocalizationService.Translate("Выделить весь текст");
            _copyMenuItem.IsEnabled = HasSelection;
            _selectAllMenuItem.IsEnabled = PageText?.Fragments.Count > 0;
        };
    }

    public PageTextLayer? PageText
    {
        get => (PageTextLayer?)GetValue(PageTextProperty);
        set => SetValue(PageTextProperty, value);
    }

    public bool HasSelection =>
        PageText is { Fragments.Count: > 0 } &&
        _anchorIndex >= 0 &&
        _activeIndex >= 0;

    public void CopySelection()
    {
        var text = GetSelectedText();
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
        }
    }

    public void SelectAllText()
    {
        if (PageText is not { Fragments.Count: > 0 } pageText)
        {
            return;
        }

        Focus();
        _anchorIndex = 0;
        _activeIndex = pageText.Fragments.Count - 1;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        if (!HasSelection || PageText is not { } pageText)
        {
            return;
        }

        var start = Math.Min(_anchorIndex, _activeIndex);
        var end = Math.Max(_anchorIndex, _activeIndex);
        for (var index = start; index <= end; index++)
        {
            var rectangle = GetDisplayRectangle(pageText.Fragments[index], pageText);
            drawingContext.DrawRectangle(SelectionBrush, null, rectangle);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        var index = HitTestFragment(e.GetPosition(this));
        if (index < 0)
        {
            ClearSelection();
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _anchorIndex >= 0)
        {
            _activeIndex = index;
        }
        else
        {
            _anchorIndex = index;
            _activeIndex = index;
        }

        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var index = HitTestFragment(e.GetPosition(this), findNearest: true);
        if (index >= 0 && index != _activeIndex)
        {
            _activeIndex = index;
            InvalidateVisual();
        }
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            CopySelection();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
        {
            SelectAllText();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClearSelection();
            e.Handled = true;
        }
    }

    private int HitTestFragment(Point point, bool findNearest = false)
    {
        if (PageText is not { Fragments.Count: > 0 } pageText ||
            ActualWidth <= 0 || ActualHeight <= 0)
        {
            return -1;
        }

        var nearestIndex = -1;
        var nearestDistance = double.MaxValue;
        for (var index = 0; index < pageText.Fragments.Count; index++)
        {
            var rectangle = GetDisplayRectangle(pageText.Fragments[index], pageText);
            var hitRectangle = rectangle;
            hitRectangle.Inflate(2, Math.Max(2, rectangle.Height * 0.2));
            if (hitRectangle.Contains(point))
            {
                return index;
            }

            if (!findNearest)
            {
                continue;
            }

            var dx = Math.Max(rectangle.Left - point.X, Math.Max(0, point.X - rectangle.Right));
            var dy = Math.Max(rectangle.Top - point.Y, Math.Max(0, point.Y - rectangle.Bottom));
            var distance = dx * dx + dy * dy;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = index;
            }
        }

        var tolerance = Math.Max(18, Math.Min(ActualWidth, ActualHeight) * 0.025);
        return nearestDistance <= tolerance * tolerance ? nearestIndex : -1;
    }

    private Rect GetDisplayRectangle(DjVuTextFragment fragment, PageTextLayer pageText)
    {
        var scaleX = ActualWidth / pageText.Width;
        var scaleY = ActualHeight / pageText.Height;
        var left = fragment.XMin * scaleX;
        var top = (pageText.Height - fragment.YMax) * scaleY;
        var width = Math.Max(1, (fragment.XMax - fragment.XMin) * scaleX);
        var height = Math.Max(1, (fragment.YMax - fragment.YMin) * scaleY);
        return new Rect(left, top, width, height);
    }

    private string GetSelectedText()
    {
        if (!HasSelection || PageText is not { } pageText)
        {
            return string.Empty;
        }

        var start = Math.Min(_anchorIndex, _activeIndex);
        var end = Math.Max(_anchorIndex, _activeIndex);
        var result = new StringBuilder();
        DjVuTextFragment? previous = null;
        for (var index = start; index <= end; index++)
        {
            var fragment = pageText.Fragments[index];
            if (previous is not null)
            {
                if (previous.Line != fragment.Line)
                {
                    result.AppendLine();
                }
                else if (NeedsSpace(previous.Text, fragment.Text))
                {
                    result.Append(' ');
                }
            }
            result.Append(fragment.Text);
            previous = fragment;
        }
        return result.ToString();
    }

    private static bool NeedsSpace(string previous, string current)
    {
        if (string.IsNullOrEmpty(previous) || string.IsNullOrEmpty(current) ||
            char.IsWhiteSpace(previous[^1]) || char.IsWhiteSpace(current[0]))
        {
            return false;
        }

        const string noSpaceBefore = ".,;:!?%)]}»”";
        const string noSpaceAfter = "([{«“";
        return !noSpaceBefore.Contains(current[0]) && !noSpaceAfter.Contains(previous[^1]);
    }

    private void ClearSelection()
    {
        _anchorIndex = -1;
        _activeIndex = -1;
        InvalidateVisual();
    }

    private static void OnPageTextChanged(
        DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _)
    {
        ((SelectableTextLayer)dependencyObject).ClearSelection();
    }

    private static Brush CreateSelectionBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(105, 70, 130, 245));
        brush.Freeze();
        return brush;
    }
}
