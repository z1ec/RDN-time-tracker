using Avalonia;
using Avalonia.Media;
using WorkTimeTracker.Core.Formatting;

namespace WorkTimeTracker.App.Controls;

/// <summary>
/// Столбчатая диаграмма (F3.3): столбец на элемент, ось часов с сеткой, необязательная линия нормы,
/// подсказка с точным значением. Значения — в минутах.
/// </summary>
public sealed class BarChart : ChartControlBase
{
    public static readonly StyledProperty<IReadOnlyList<ChartBar>?> ItemsProperty =
        AvaloniaProperty.Register<BarChart, IReadOnlyList<ChartBar>?>(nameof(Items));

    /// <summary>Значение линии нормы в минутах; <see cref="double.NaN"/> — не рисовать.</summary>
    public static readonly StyledProperty<double> NormValueProperty =
        AvaloniaProperty.Register<BarChart, double>(nameof(NormValue), double.NaN);

    public static readonly StyledProperty<IBrush?> BarBrushProperty =
        AvaloniaProperty.Register<BarChart, IBrush?>(nameof(BarBrush), Brushes.SlateBlue);

    public static readonly StyledProperty<IBrush?> HighlightBrushProperty =
        AvaloniaProperty.Register<BarChart, IBrush?>(nameof(HighlightBrush), Brushes.DarkSlateBlue);

    public static readonly StyledProperty<IBrush?> MutedBrushProperty =
        AvaloniaProperty.Register<BarChart, IBrush?>(nameof(MutedBrush), Brushes.LightSteelBlue);

    public static readonly StyledProperty<IBrush?> NormBrushProperty =
        AvaloniaProperty.Register<BarChart, IBrush?>(nameof(NormBrush), Brushes.OrangeRed);

    private const double AxisWidth = 44;
    private const double LabelHeight = 22;
    private const double TopPadding = 10;

    // Шаги сетки в минутах: от 30 мин до 1000 ч.
    private static readonly int[] Steps = [30, 60, 120, 180, 240, 300, 600, 1200, 1800, 3000, 6000, 12000, 30000, 60000];

    static BarChart()
    {
        AffectsRender<BarChart>(ItemsProperty, NormValueProperty, BarBrushProperty, HighlightBrushProperty, MutedBrushProperty, NormBrushProperty);
        ItemsProperty.Changed.AddClassHandler<BarChart>((c, _) => c.ResetHover());
    }

    public IReadOnlyList<ChartBar>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public double NormValue
    {
        get => GetValue(NormValueProperty);
        set => SetValue(NormValueProperty, value);
    }

    public IBrush? BarBrush
    {
        get => GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public IBrush? HighlightBrush
    {
        get => GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    public IBrush? MutedBrush
    {
        get => GetValue(MutedBrushProperty);
        set => SetValue(MutedBrushProperty, value);
    }

    public IBrush? NormBrush
    {
        get => GetValue(NormBrushProperty);
        set => SetValue(NormBrushProperty, value);
    }

    private Rect PlotRect => new(AxisWidth, TopPadding, Math.Max(0, Bounds.Width - AxisWidth), Math.Max(0, Bounds.Height - TopPadding - LabelHeight));

    /// <summary>Верх шкалы и шаг сетки для максимального значения.</summary>
    public static (double Top, int Step) Scale(double maxValue)
    {
        var max = maxValue <= 0 ? 60 : maxValue;
        var step = Steps.FirstOrDefault(s => max / s <= 4, Steps[^1]);
        return (Math.Ceiling(max / step) * step, step);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); // для подсказок по всей площади

        var items = Items;
        if (items is null || items.Count == 0)
        {
            return;
        }

        var plot = PlotRect;
        if (plot.Width < 10 || plot.Height < 10)
        {
            return;
        }

        var norm = double.IsNaN(NormValue) ? 0 : NormValue;
        var (top, step) = Scale(Math.Max(items.Max(i => i.Value), norm));
        double Y(double v) => plot.Bottom - (v / top * plot.Height);

        // Сетка и подписи оси.
        var gridPen = new Pen(GridBrush, 1);
        for (var v = 0.0; v <= top + 0.1; v += step)
        {
            var y = Math.Round(Y(v)) + 0.5;
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(v % 60 == 0 ? $"{v / 60:0} ч" : DurationFormatter.FormatCompact((int)v), 11, AxisTextBrush);
            context.DrawText(label, new Point(plot.Left - label.Width - 8, y - (label.Height / 2)));
        }

        // Столбцы.
        var slot = plot.Width / items.Count;
        var barWidth = Math.Min(48, Math.Max(2, slot * 0.62));
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var cx = plot.Left + (slot * i) + (slot / 2);
            if (item.Value > 0)
            {
                var h = Math.Max(2, plot.Bottom - Y(item.Value));
                var brush = i == HoverIndex || item.IsHighlighted ? HighlightBrush : item.IsMuted ? MutedBrush : BarBrush;
                var radius = Math.Min(4, barWidth / 2);
                context.DrawRectangle(brush, null, new RoundedRect(new Rect(cx - (barWidth / 2), plot.Bottom - h, barWidth, h), radius, radius, 0, 0));
            }

            if (item.Label.Length > 0)
            {
                var label = Text(item.Label, 11, AxisTextBrush, item.IsHighlighted ? FontWeight.SemiBold : FontWeight.Normal);
                context.DrawText(label, new Point(cx - (label.Width / 2), plot.Bottom + 5));
            }
        }

        // Линия нормы.
        if (norm > 0)
        {
            var y = Math.Round(Y(norm)) + 0.5;
            var pen = new Pen(NormBrush, 1.5, new DashStyle([4, 3], 0));
            context.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text($"норма {DurationFormatter.FormatShort((int)norm)}", 11, NormBrush, FontWeight.SemiBold);
            context.DrawText(label, new Point(plot.Right - label.Width, y - label.Height - 2));
        }
    }

    protected override int HitTestIndex(Point point)
    {
        var items = Items;
        var plot = PlotRect;
        if (items is null || items.Count == 0 || point.X < plot.Left || point.X > plot.Right || point.Y < 0 || point.Y > Bounds.Height)
        {
            return -1;
        }

        var i = (int)((point.X - plot.Left) / (plot.Width / items.Count));
        return Math.Clamp(i, 0, items.Count - 1);
    }

    protected override string? TooltipFor(int index) => Items is { } items && index < items.Count ? items[index].Tooltip : null;
}
