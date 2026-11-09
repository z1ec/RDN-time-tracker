namespace WorkTimeTracker.App.Controls;

/// <summary>Столбец графика: подпись под осью, значение в минутах, подсказка.</summary>
/// <param name="Label">Подпись под столбцом (может быть пустой, чтобы не загромождать ось).</param>
/// <param name="Value">Значение в минутах.</param>
/// <param name="Tooltip">Текст подсказки с точным значением.</param>
/// <param name="IsHighlighted">Выделить (сегодня / текущий месяц).</param>
/// <param name="IsMuted">Приглушить (выходной или будущий день).</param>
public sealed record ChartBar(string Label, double Value, string Tooltip, bool IsHighlighted = false, bool IsMuted = false);
