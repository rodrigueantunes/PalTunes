using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using PalTunes.App.ViewModels;

namespace PalTunes.App.Views;

public partial class ArticlesView : UserControl
{
    public ArticlesView()
    {
        InitializeComponent();
        // HelixToolkit réinitialise sa caméra au chargement : recadrage ensuite.
        Preview.Loaded += (_, _) => Dispatcher.BeginInvoke(() => Controls.CameraHelper.Iso(Preview, (DataContext as ArticlesViewModel)?.Preview), System.Windows.Threading.DispatcherPriority.Background);
        // Vue masquée au premier calcul (taille nulle) : recadrage dès qu'elle reçoit une taille.
        Preview.SizeChanged += (_, e) =>
        {
            if (e.PreviousSize.Width < 1)
            {
                Controls.CameraHelper.Iso(Preview, (DataContext as ArticlesViewModel)?.Preview);
            }
            else
            {
                Controls.CameraHelper.Refit(Preview, (DataContext as ArticlesViewModel)?.Preview);
            }
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old)
            {
                old.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (e.NewValue is INotifyPropertyChanged vm)
            {
                vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ArticlesViewModel.Preview))
        {
            Dispatcher.BeginInvoke(() => Controls.CameraHelper.Iso(Preview, ((ArticlesViewModel)sender!).Preview), DispatcherPriority.Background);
        }
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ArticlesViewModel vm)
        {
            vm.SelectNode(e.NewValue as TreeNode);
        }
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        if (NewButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = NewButton;
            menu.Placement = PlacementMode.Bottom;
            menu.DataContext = DataContext;
            menu.IsOpen = true;
        }
    }
}
