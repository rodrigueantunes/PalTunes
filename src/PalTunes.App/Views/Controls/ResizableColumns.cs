using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PalTunes.App.Views.Controls;

/// <summary>
/// Colonnes redimensionnables : <c>ctl:ResizableColumns.Key="Articles"</c> sur une grille ajoute un séparateur à glisser
/// entre chaque colonne (largeur mémorisée sous cette clé, double-clic : largeur d'origine). Chaque colonne garde une
/// largeur minimale.
/// </summary>
public static class ResizableColumns
{
    /// <summary>Lecture / écriture des largeurs mémorisées (branchées sur les réglages par l'application).</summary>
    public static Func<string, double[]?>? LoadWidths { get; set; }

    public static Action<string, double[]?>? SaveWidths { get; set; }

    public const double DefaultMinWidth = 180;

    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached("Key", typeof(string), typeof(ResizableColumns), new PropertyMetadata(null, OnKeyChanged));

    public static string? GetKey(Grid grid) => (string?)grid.GetValue(KeyProperty);

    public static void SetKey(Grid grid, string? value) => grid.SetValue(KeyProperty, value);

    private static readonly DependencyProperty OriginalProperty =
        DependencyProperty.RegisterAttached("Original", typeof(GridLength[]), typeof(ResizableColumns));

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Grid grid && e.NewValue is string)
        {
            grid.Loaded -= OnLoaded;
            grid.Loaded += OnLoaded;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var grid = (Grid)sender;
        if (grid.GetValue(OriginalProperty) != null || GetKey(grid) is not { } key || grid.ColumnDefinitions.Count < 2)
        {
            return;
        }

        var columns = grid.ColumnDefinitions;
        grid.SetValue(OriginalProperty, columns.Select(c => c.Width).ToArray());
        foreach (var c in columns.Where(c => c.MinWidth <= 0))
        {
            c.MinWidth = DefaultMinWidth;
        }

        Restore(grid, LoadWidths?.Invoke(key));
        for (var i = 0; i < columns.Count - 1; i++)
        {
            var splitter = new GridSplitter
            {
                Width = 8,
                Margin = new Thickness(0, 0, -4, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.CurrentAndNext,
                Background = Brushes.Transparent,
                ShowsPreview = false,
                Focusable = false,
                Cursor = Cursors.SizeWE,
                ToolTip = "Glisser pour élargir ou réduire · double-clic : largeur d'origine"
            };
            splitter.MouseEnter += (_, _) => splitter.Background = Application.Current?.TryFindResource("SplitterHoverBrush") as Brush ?? Brushes.SteelBlue;
            splitter.MouseLeave += (_, _) => splitter.Background = Brushes.Transparent;
            // Le séparateur traite lui-même la fin du glissement : écoute des événements déjà traités.
            splitter.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
                SaveWidths?.Invoke(key, columns.Select(c => Math.Round(c.ActualWidth)).ToArray())), handledEventsToo: true);
            splitter.MouseDoubleClick += (_, args) =>
            {
                var original = (GridLength[])grid.GetValue(OriginalProperty);
                for (var k = 0; k < columns.Count && k < original.Length; k++)
                {
                    columns[k].Width = original[k];
                }

                SaveWidths?.Invoke(key, null);
                args.Handled = true;
            };
            Grid.SetColumn(splitter, i);
            Grid.SetRowSpan(splitter, Math.Max(1, grid.RowDefinitions.Count));
            Panel.SetZIndex(splitter, 1000);
            grid.Children.Add(splitter);
        }
    }

    /// <summary>Largeurs mémorisées : colonnes fixes en pixels, colonnes proportionnelles en proportions.</summary>
    private static void Restore(Grid grid, double[]? widths)
    {
        var columns = grid.ColumnDefinitions;
        if (widths == null || widths.Length != columns.Count || widths.Any(w => w <= 0))
        {
            return;
        }

        for (var i = 0; i < columns.Count; i++)
        {
            var w = Math.Max(widths[i], columns[i].MinWidth);
            columns[i].Width = columns[i].Width.IsStar ? new GridLength(w, GridUnitType.Star) : new GridLength(w);
        }
    }
}
