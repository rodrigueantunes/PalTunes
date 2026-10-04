using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PalTunes.App.Views.Controls;

/// <summary>
/// Filtre intégré aux listes déroulantes : un champ « Rechercher… » en haut de la liste ouverte masque les éléments
/// qui ne contiennent pas tous les mots saisis (sans tenir compte des majuscules ni des accents). Taper une lettre sur
/// la liste fermée l'ouvre et commence la recherche ; Entrée choisit le premier élément trouvé, ↓ descend dans la liste.
/// Liste modifiable (IsEditable) : le texte saisi filtre directement la liste.
/// Le champ n'apparaît qu'à partir de <see cref="MinItemsProperty"/> éléments (0 = toujours).
/// </summary>
public static class ComboFilter
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ComboFilter), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty MinItemsProperty = DependencyProperty.RegisterAttached(
        "MinItems", typeof(int), typeof(ComboFilter), new PropertyMetadata(8));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(FilterState), typeof(ComboFilter), new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject o, bool value) => o.SetValue(IsEnabledProperty, value);
    public static int GetMinItems(DependencyObject o) => (int)o.GetValue(MinItemsProperty);
    public static void SetMinItems(DependencyObject o, int value) => o.SetValue(MinItemsProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox combo || !(bool)e.NewValue || combo.GetValue(StateProperty) != null)
        {
            return;
        }

        var state = new FilterState(combo);
        combo.SetValue(StateProperty, state);
        // Sur une vue propre, une liste synchronisée prendrait le premier élément de la vue et écraserait la liaison.
        combo.IsSynchronizedWithCurrentItem = false;
        combo.Loaded += (_, _) => state.Attach();
        combo.DropDownOpened += (_, _) => state.Opened();
        combo.DropDownClosed += (_, _) => state.Closed();
        combo.PreviewTextInput += (_, a) => state.PreviewTextInput(a);
        combo.PreviewKeyDown += (_, a) => state.PreviewKeyDown(a);
        combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, a) => state.EditableTextChanged(a)));
    }

    /// <summary>Texte comparable : minuscules, sans accents.</summary>
    internal static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    /// <summary>Vrai si le texte contient tous les mots de la recherche.</summary>
    internal static bool Matches(string foldedText, string query) => Matches(foldedText, Words(query));

    internal static bool Matches(string foldedText, string[] words) => words.All(foldedText.Contains);

    internal static string[] Words(string query) => Fold(query).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    private sealed class FilterState(ComboBox combo)
    {
        private Popup? _popup;
        private FrameworkElement? _host;
        private TextBox? _search;
        private TextBlock? _hint;
        private TextBlock? _empty;
        private bool _attached;
        private bool _userTyping;
        private string? _pending;
        private bool _opening;
        private readonly Dictionary<object, string> _folded = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Liste sur une vue propre (convertisseur OwnView) : filtrage de la vue, liste virtualisée (milliers d'éléments).
        /// Sinon : éléments masqués un par un (petites listes, tous les éléments créés).
        /// </summary>
        private ListCollectionView? View => combo.ItemsSource as ListCollectionView;

        private bool SearchVisible => !combo.IsEditable && combo.Items.Count >= Math.Max(0, GetMinItems(combo)) && _search != null;

        public void Attach()
        {
            if (_attached || combo.IsEditable)
            {
                return;
            }

            combo.ApplyTemplate();
            if (View != null)
            {
                combo.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
                VirtualizingPanel.SetIsVirtualizing(combo, true);
                VirtualizingPanel.SetVirtualizationMode(combo, VirtualizationMode.Recycling);
            }
            else
            {
                VirtualizingPanel.SetIsVirtualizing(combo, false);
            }

            _popup = combo.Template?.FindName("PART_Popup", combo) as Popup;
            if (_popup?.Child is not { } child)
            {
                return;
            }

            // Le champ de recherche est inséré au-dessus de la liste défilante, dans le même cadre (thèmes Aero2 et Fluent).
            if (FindScroller(child) is not { } scroller || LogicalTreeHelper.GetParent(scroller) is not { } parent)
            {
                return;
            }

            if (View != null)
            {
                scroller.CanContentScroll = true; // défilement par élément : condition de la virtualisation
            }

            _search = new TextBox { Margin = new Thickness(0), MinWidth = 120 };
            _search.SetResourceReference(FrameworkElement.StyleProperty, "FormBox");
            _search.TextChanged += (_, e) =>
            {
                Apply(_search.Text);
                e.Handled = true; // la recherche ne modifie pas le formulaire (pas de « Modifié »)
            };
            _search.PreviewKeyDown += SearchKeyDown;
            _hint = new TextBlock
            {
                Text = "Rechercher…",
                IsHitTestVisible = false,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic
            };
            _empty = new TextBlock
            {
                Text = "Aucun résultat",
                Margin = new Thickness(10, 6, 10, 4),
                Foreground = Brushes.Gray,
                Visibility = Visibility.Collapsed
            };
            var field = new Grid();
            field.Children.Add(_search);
            field.Children.Add(_hint);
            var panel = new StackPanel { Margin = new Thickness(4, 4, 4, 2) };
            panel.Children.Add(field);
            panel.Children.Add(_empty);
            _host = panel;
            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(panel, Dock.Top);
            dock.Children.Add(panel);
            switch (parent)
            {
                case Decorator frame:
                    frame.Child = null;
                    dock.Children.Add(scroller);
                    frame.Child = dock;
                    break;
                case Panel host:
                    var index = host.Children.IndexOf(scroller);
                    host.Children.RemoveAt(index);
                    Grid.SetRow(dock, Grid.GetRow(scroller));
                    Grid.SetColumn(dock, Grid.GetColumn(scroller));
                    Grid.SetRowSpan(dock, Grid.GetRowSpan(scroller));
                    Grid.SetColumnSpan(dock, Grid.GetColumnSpan(scroller));
                    dock.Margin = scroller.Margin;
                    scroller.Margin = new Thickness(0);
                    dock.Children.Add(scroller);
                    host.Children.Insert(index, dock);
                    break;
                case ContentControl content:
                    content.Content = null;
                    dock.Children.Add(scroller);
                    content.Content = dock;
                    break;
                default:
                    return;
            }

            _attached = true;
        }

        /// <summary>Liste défilante de la liste déroulante (la seule du cadre).</summary>
        private static ScrollViewer? FindScroller(DependencyObject node)
        {
            if (node is ScrollViewer sv)
            {
                return sv;
            }

            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            {
                if (FindScroller(child) is { } found)
                {
                    return found;
                }
            }

            return null;
        }


        public void Opened()
        {
            Attach();
            if (_host == null || _search == null)
            {
                return;
            }

            _host.Visibility = SearchVisible ? Visibility.Visible : Visibility.Collapsed;
            if (!SearchVisible)
            {
                return;
            }

            _opening = true;
            combo.Dispatcher.BeginInvoke(() =>
            {
                // Lettres tapées avant que le champ ait le focus (saisie rapide) : toutes reprises.
                var seed = _pending;
                _pending = null;
                _opening = false;
                _search.Text = seed ?? "";
                _search.CaretIndex = _search.Text.Length;
                _search.Focus();
                Keyboard.Focus(_search);
                Apply(_search.Text);
            }, DispatcherPriority.Input);
        }

        public void Closed()
        {
            _userTyping = false;
            _folded.Clear();
            if (_search != null)
            {
                _search.Text = "";
            }

            Apply("");
        }

        /// <summary>Liste fermée : une lettre tapée ouvre la liste et commence la recherche.</summary>
        public void PreviewTextInput(TextCompositionEventArgs e)
        {
            if (combo.IsEditable)
            {
                _userTyping = true;
                return;
            }

            if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]) || e.OriginalSource is TextBox)
            {
                return;
            }

            if (combo.IsDropDownOpen)
            {
                if (_opening)
                {
                    _pending += e.Text;
                    e.Handled = true;
                }
                else if (_search is { IsVisible: true } search)
                {
                    // Focus sur un élément de la liste : la saisie continue dans le champ de recherche.
                    search.Text += e.Text;
                    search.CaretIndex = search.Text.Length;
                    search.Focus();
                    e.Handled = true;
                }

                return;
            }

            Attach();
            if (!SearchVisible)
            {
                return;
            }

            _pending = e.Text;
            combo.IsDropDownOpen = true;
            e.Handled = true;
        }

        public void PreviewKeyDown(KeyEventArgs e)
        {
            if (combo.IsEditable && e.Key is Key.Back or Key.Delete)
            {
                _userTyping = true;
            }
        }

        /// <summary>Liste modifiable : le texte saisi au clavier filtre la liste.</summary>
        public void EditableTextChanged(TextChangedEventArgs e)
        {
            if (!combo.IsEditable || !_userTyping)
            {
                return;
            }

            _userTyping = false;
            var text = combo.Text;
            if (text.Length > 0 && !combo.IsDropDownOpen && combo.Items.Count > 0)
            {
                combo.IsDropDownOpen = true;
                combo.Dispatcher.BeginInvoke(() => Apply(text), DispatcherPriority.Loaded);
            }
            else
            {
                Apply(text);
            }
        }

        private void SearchKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    if (FirstMatch() is { } first)
                    {
                        combo.SelectedItem = first;
                    }

                    combo.IsDropDownOpen = false;
                    e.Handled = true;
                    break;
                case Key.Down:
                    if (FirstMatch() is { } match)
                    {
                        if (View != null)
                        {
                            combo.Dispatcher.BeginInvoke(() =>
                            {
                                if (combo.ItemContainerGenerator.ContainerFromItem(match) is ComboBoxItem c)
                                {
                                    c.Focus();
                                }
                            }, DispatcherPriority.Loaded);
                        }
                        else if (combo.ItemContainerGenerator.ContainerFromItem(match) is ComboBoxItem item)
                        {
                            item.Focus();
                        }
                    }

                    e.Handled = true;
                    break;
                case Key.Escape:
                    combo.IsDropDownOpen = false;
                    e.Handled = true;
                    break;
            }
        }

        private object? FirstMatch()
        {
            if (View is { } view)
            {
                return view.Cast<object>().FirstOrDefault(o => _search is not { Text.Length: > 0 } || !ReferenceEquals(o, combo.SelectedItem) || Matches(FoldOf(o), _search.Text));
            }

            return Visible().FirstOrDefault() is { } c ? combo.ItemContainerGenerator.ItemFromContainer(c) : null;
        }

        private string FoldOf(object item)
        {
            if (!_folded.TryGetValue(item, out var text))
            {
                text = Fold(item.ToString());
                _folded[item] = text;
            }

            return text;
        }

        private IEnumerable<ComboBoxItem> Visible()
        {
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if (combo.ItemContainerGenerator.ContainerFromIndex(i) is ComboBoxItem c && c.Visibility == Visibility.Visible)
                {
                    yield return c;
                }
            }
        }

        private void Apply(string query)
        {
            if (_hint != null)
            {
                _hint.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
            }

            if (View is { } view)
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    if (view.Filter != null)
                    {
                        view.Filter = null;
                    }
                }
                else
                {
                    var selected = combo.SelectedItem;
                    var words = Words(query);
                    // L'élément choisi reste dans la vue : la sélection n'est jamais perdue en filtrant.
                    view.Filter = o => ReferenceEquals(o, selected) || Matches(FoldOf(o), words);
                }

                if (_empty != null)
                {
                    _empty.Visibility = view.IsEmpty || (view.Count == 1 && combo.SelectedItem != null && !string.IsNullOrWhiteSpace(query) &&
                                                         !Matches(FoldOf(combo.SelectedItem), query))
                        ? Visibility.Visible : Visibility.Collapsed;
                }

                return;
            }

            var shown = 0;
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if (combo.ItemContainerGenerator.ContainerFromIndex(i) is not ComboBoxItem container)
                {
                    continue;
                }

                var match = string.IsNullOrWhiteSpace(query) || Matches(Fold(ItemText(combo.Items[i], container)), query);
                container.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                if (match)
                {
                    shown++;
                }
            }

            if (_empty != null)
            {
                _empty.Visibility = shown == 0 && combo.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>Texte d'un élément : ce qui est affiché dans la liste, plus sa représentation texte.</summary>
        private static string ItemText(object? item, DependencyObject container)
        {
            var sb = new StringBuilder();
            sb.Append(item?.ToString()).Append(' ');
            Collect(container, sb);
            return sb.ToString();
        }

        private static void Collect(DependencyObject node, StringBuilder sb)
        {
            if (node is TextBlock tb)
            {
                sb.Append(tb.Text).Append(' ');
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                Collect(VisualTreeHelper.GetChild(node, i), sb);
            }
        }
    }
}
