using Avalonia;
using Avalonia.Media;
using WorkTimeTracker.Core.Formatting;

namespace WorkTimeTracker.App.Controls;

/// <summary>Тепловая карта года в стиле GitHub (F3.3, P2): клетка на день, цвет — уровень 0…4.</summary>
public sealed class YearHeatmap : ChartControlBase
{
    public static readonly StyledProperty<IReadOnlyList<HeatmapCell>?> CellsProperty =
        AvaloniaProperty.Register<YearHeatmap, IReadOnlyList<HeatmapCell>?>(nameof(Cells));

    public static readonly StyledProperty<IBrush?> Level0BrushProperty =
        AvaloniaProperty.Register<YearHeatmap, IBrush?>(nameof(Level0Brush), new SolidColorBrush(Color.Parse("#EBEDF0")));
    public static readonly StyledProperty<IBrush?> Level1BrushProperty =
        AvaloniaProperty.Register<YearHeatmap, IBrush?>(nameof(Level1Brush), new SolidColorBrush(Color.Parse("#C7D2FE")));
    public static readonly StyledProperty<IBrush?> Level2BrushProperty =
        AvaloniaProperty.Register<YearHeatmap, IBrush?>(nameof(Level2Brush), new SolidColorBrush(Color.Parse("#A5B4FC")));
    public static readonly StyledProperty<IBrush?> Level3BrushProperty =
        AvaloniaProperty.Register<YearHeatmap, IBrush?>(nameof(Level3Brush), new SolidColorBrush(Color.Parse("#6366F1")));
    public static readonly StyledProperty<IBrush?> Level4BrushProperty =
        AvaloniaProperty.Register<YearHeatmap, IBrush?>(nameof(Level4Brush), new SolidColorBrush(Color.Parse("#4338CA")));

    private const double LeftLabels = 28;
    private const double TopLabels = 18;
    private const double Gap = 3;

    static YearHeatmap()
    {
        AffectsRender<YearHeatmap>(CellsProperty, Level0BrushProperty, Level1BrushProperty, Level2BrushProperty, Level3BrushProperty, Level4BrushProperty);
        AffectsMeasure<YearHeatmap>(CellsProperty);
        CellsProperty.Changed.AddClassHandler<YearHeatmap>((c, _) => c.ResetHover());
    }

    public IReadOnlyList<HeatmapCell>? Cells
    {
        get => GetValue(CellsProperty);
        set => SetValue(CellsProperty, value);
    }

    public IBrush? Level0Brush { get => GetValue(Level0BrushProperty); set => SetValue(Level0BrushProperty, value); }

    public IBrush? Level1Brush { get => GetValue(Level1BrushProperty); set => SetValue(Level1BrushProperty, value); }

    public IBrush? Level2Brush { get => GetValue(Level2BrushProperty); set => SetValue(Level2BrushProperty, value); }

    public IBrush? Level3Brush { get => GetValue(Level3BrushProperty); set => SetValue(Level3BrushProperty, value); }

    public IBrush? Level4Brush { get => GetValue(Level4BrushProperty); set => SetValue(Level4BrushProperty, value); }

    private int Columns => Cells is { Count: > 0 } c ? c.Max(x => x.Column) + 1 : 53;

    private double CellSize => Math.Clamp(((Bounds.Width - LeftLabels) / Columns) - Gap, 4, 16);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width;
        var cell = Math.Clamp(((width - LeftLabels) / Columns) - Gap, 4, 16);
        return new Size(width, TopLabels + (7 * (cell + Gap)));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var cells = Cells;
        if (cells is null || cells.Count == 0)
        {
            return;
        }

        var size = CellSize;
        var pitch = size + Gap;
        foreach (var (row, name) in new[] { (0, "Пн"), (2, "Ср"), (4, "Пт") })
        {
            var t = Text(name, 10, AxisTextBrush);
            context.DrawText(t, new Point(0, TopLabels + (row * pitch) + ((size - t.Height) / 2)));
        }

        // Подписи месяцев над столбцом, где начинается месяц.
        var lastMonthColumn = -10;
        for (var i = 0; i < cells.Count; i++)
        {
            if ((i == 0 || cells[i].Date.Day == 1) && cells[i].Column - lastMonthColumn >= 3)
            {
                lastMonthColumn = cells[i].Column;
                context.DrawText(Text(RuFormat.MonthShortNominative(cells[i].Date.Month), 10, AxisTextBrush), new Point(LeftLabels + (cells[i].Column * pitch), 0));
            }
        }

        for (var i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            var brush = c.Level switch
            {
                1 => Level1Brush,
                2 => Level2Brush,
                3 => Level3Brush,
                4 => Level4Brush,
                _ => Level0Brush,
            };
            var rect = new Rect(LeftLabels + (c.Column * pitch), TopLabels + (c.Row * pitch), size, size);
            var pen = i == HoverIndex ? new Pen(AxisTextBrush, 1) : null;
            context.DrawRectangle(c.IsFuture ? null : brush, pen ?? (c.IsFuture ? new Pen(Level0Brush, 1) : null), rect, 2, 2);
        }
    }

    protected override int HitTestIndex(Point point)
    {
        var cells = Cells;
        if (cells is null)
        {
            return -1;
        }

        var pitch = CellSize + Gap;
        var col = (int)Math.Floor((point.X - LeftLabels) / pitch);
        var row = (int)Math.Floor((point.Y - TopLabels) / pitch);
        for (var i = 0; i < cells.Count; i++)
        {
            if (cells[i].Column == col && cells[i].Row == row)
            {
                return i;
            }
        }

        return -1;
    }

    protected override string? TooltipFor(int index) => Cells is { } cells && index < cells.Count ? cells[index].Tooltip : null;
}
