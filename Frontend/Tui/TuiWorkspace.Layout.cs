using XenoAtom.Terminal.UI.Controls;

namespace UIEngine.Frontend.Tui;

public sealed partial class TuiWorkspace
{
    private void _RebuildGrid()
    {
        foreach (var cell in _NavigatorGrid.Cells)
        {
            cell.Content = null!;
        }

        _NavigatorGrid.Cells.Clear();
        _NavigatorGrid.RowDefinitions.Clear();
        _NavigatorGrid.ColumnDefinitions.Clear();
        if (_Presentations.Count == 0)
        {
            var add = new Button("Add Navigator");
            add.ClickRouted += (_, _) => _ShowRootDialog();
            _NavigatorGrid.Cells.Add(new GridCell(add));
            return;
        }

        var maxRow = _Presentations.Values.Max(static item => item.Configuration.Row);
        var maxColumn = _Presentations.Values.Max(static item => item.Configuration.Column);
        for (var row = 0; row <= maxRow; row++)
        {
            var height = _Presentations.Values
                .Where(item => item.Configuration.Row == row)
                .Select(static item => item.Configuration.Height)
                .DefaultIfEmpty(1)
                .Max();
            _NavigatorGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Fixed(height),
            });
        }

        for (var column = 0; column <= maxColumn; column++)
        {
            var width = _Presentations.Values
                .Where(item => item.Configuration.Column == column)
                .Select(static item => item.Configuration.Width)
                .DefaultIfEmpty(1)
                .Max();
            _NavigatorGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Fixed(width),
            });
        }

        foreach (var presentation in _Presentations.Values)
        {
            _NavigatorGrid.Cells.Add(new GridCell(presentation.Container)
            {
                Row = presentation.Configuration.Row,
                Column = presentation.Configuration.Column,
            });
        }
    }

    private void _NormalizeConfigurations()
    {
        var occupied = new HashSet<(int Row, int Column)>();
        foreach (var presentation in _Presentations.Values)
        {
            var configuration = presentation.Configuration;
            if (!occupied.Add((configuration.Row, configuration.Column)))
            {
                configuration = _FindFreeConfiguration(
                    occupied,
                    configuration.Width,
                    configuration.Height);
                presentation.ApplyConfiguration(configuration);
                occupied.Add((configuration.Row, configuration.Column));
            }
        }
    }

    private NavigatorPresentationConfiguration _FindFreeConfiguration(
        HashSet<(int Row, int Column)>? occupied = null,
        int width = 40,
        int height = 12,
        int row = 0,
        int column = 0)
    {
        occupied ??= _Presentations.Values
            .Select(static item => (item.Configuration.Row, item.Configuration.Column))
            .ToHashSet();
        while (occupied.Contains((row, column)))
        {
            column++;
        }

        return new NavigatorPresentationConfiguration(row, column, width, height);
    }

    private void _ScrollSelectionIntoView()
    {
        if (SelectedNavigatorId is not Guid id || !_Presentations.TryGetValue(id, out var selected))
        {
            return;
        }

        var x = Enumerable.Range(0, selected.Configuration.Column)
            .Sum(column => _Presentations.Values
                .Where(item => item.Configuration.Column == column)
                .Select(static item => item.Configuration.Width)
                .DefaultIfEmpty(1)
                .Max() + 1);
        var y = Enumerable.Range(0, selected.Configuration.Row)
            .Sum(row => _Presentations.Values
                .Where(item => item.Configuration.Row == row)
                .Select(static item => item.Configuration.Height)
                .DefaultIfEmpty(1)
                .Max() + 1);
        _NavigatorScroll.HorizontalOffset = _VisibleOffset(
            _NavigatorScroll.HorizontalOffset,
            _NavigatorScroll.ViewportWidth,
            x,
            selected.Configuration.Width);
        _NavigatorScroll.VerticalOffset = _VisibleOffset(
            _NavigatorScroll.VerticalOffset,
            _NavigatorScroll.ViewportHeight,
            y,
            selected.Configuration.Height);
    }

    private static int _VisibleOffset(int offset, int viewport, int start, int length)
    {
        if (start < offset)
        {
            return start;
        }

        return viewport > 0 && start + length > offset + viewport
            ? start + length - viewport
            : offset;
    }
}
