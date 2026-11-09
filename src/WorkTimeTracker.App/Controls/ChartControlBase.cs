using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

namespace WorkTimeTracker.App.Controls;

/// <summary>
/// Основа лёгких графиков (ADR-0005): отрисовка на <see cref="DrawingContext"/>,
/// подсказка с точным значением под указателем, общие кисти и текст.
/// </summary>
public abstract class ChartControlBase : Control
{
    public static readonly StyledProperty<IBrush?> AxisTextBrushProperty =
        AvaloniaProperty.Register<ChartControlBase, IBrush?>(nameof(AxisTextBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<ChartControlBase, IBrush?>(nameof(GridBrush), Brushes.LightGray);

    private int _hoverIndex = -1;

    static ChartControlBase()
    {
        AffectsRender<ChartControlBase>(AxisTextBrushProperty, GridBrushProperty);
    }

    public IBrush? AxisTextBrush
    {
        get => GetValue(AxisTextBrushProperty);
        set => SetValue(AxisTextBrushProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    /// <summary>Индекс элемента под указателем (−1 — нет).</summary>
    protected int HoverIndex => _hoverIndex;

    /// <summary>Индекс элемента в точке или −1.</summary>
    protected abstract int HitTestIndex(Point point);

    /// <summary>Текст подсказки для элемента.</summary>
    protected abstract string? TooltipFor(int index);

    protected FormattedText Text(string text, double size, IBrush? brush, FontWeight weight = FontWeight.Normal) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(GetValue(TextElement.FontFamilyProperty), FontStyle.Normal, weight), size, brush ?? Brushes.Gray);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetHover(HitTestIndex(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(-1);
    }

    private void SetHover(int index)
    {
        if (index == _hoverIndex)
        {
            return;
        }

        _hoverIndex = index;
        var tip = index >= 0 ? TooltipFor(index) : null;
        ToolTip.SetTip(this, tip);
        ToolTip.SetIsOpen(this, tip is not null);
        InvalidateVisual();
    }

    /// <summary>Сбрасывает подсказку при смене данных.</summary>
    protected void ResetHover()
    {
        _hoverIndex = -1;
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, null);
    }
}
