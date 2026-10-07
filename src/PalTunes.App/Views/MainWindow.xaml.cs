using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PalTunes.App.Services;
using PalTunes.App.ViewModels;

namespace PalTunes.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
        : this(new MainViewModel(new DialogService(), new SettingsService(Environment.GetEnvironmentVariable("PALTUNES_SETTINGS"))))
    {
    }

    /// <summary>Fenêtre sur un modèle existant (changement de thème : même données, même écran, saisies conservées).</summary>
    public MainWindow(MainViewModel vm)
    {
        vm.ConnectColumnWidths();
        InitializeComponent();
        DataContext = vm;
        vm.Search.PropertyChanged += OnSearchChanged;
        vm.ThemeChangeRequested += RebuildForTheme;
        Closed += (_, _) =>
        {
            vm.Search.PropertyChanged -= OnSearchChanged;
            vm.ThemeChangeRequested -= RebuildForTheme;
        };
    }

    private void OnSearchChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GlobalSearchViewModel.IsOpen) && Vm.Search.IsOpen)
        {
            Dispatcher.BeginInvoke(() =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }, DispatcherPriority.Input);
        }
    }

    /// <summary>Nouveau thème : styles rechargés, fenêtre recréée au même endroit sur le même modèle.</summary>
    private void RebuildForTheme()
    {
        var vm = Vm;
        ThemeService.Apply(ThemeService.Resolve(vm.Settings.Current.Theme));
        var next = new MainWindow(vm)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = RestoreBounds.Left,
            Top = RestoreBounds.Top,
            Width = RestoreBounds.Width,
            Height = RestoreBounds.Height,
            WindowState = WindowState
        };
        Application.Current.MainWindow = next;
        next.Show();
        Close();
        vm.NotifyThemeChanged();
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    /// <summary>Boutons latéraux de la souris : écran précédent / suivant.</summary>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (e.ChangedButton == MouseButton.XButton1 && Vm.GoBackCommand.CanExecute(null))
        {
            Vm.GoBackCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.XButton2 && Vm.GoForwardCommand.CanExecute(null))
        {
            Vm.GoForwardCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Vm.Search.Move(1);
                SearchResults.ScrollIntoView(Vm.Search.Selected);
                e.Handled = true;
                break;
            case Key.Up:
                Vm.Search.Move(-1);
                SearchResults.ScrollIntoView(Vm.Search.Selected);
                e.Handled = true;
                break;
            case Key.Enter:
                Vm.Search.GoCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void SearchResults_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: SearchHit hit })
        {
            Vm.Search.GoCommand.Execute(hit);
        }
    }

    private void SearchBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Vm.Search.IsOpen = false;

    private void SearchCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
