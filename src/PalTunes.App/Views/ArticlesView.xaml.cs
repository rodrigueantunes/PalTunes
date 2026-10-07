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

    // ------------------------------------------------------------------ Liste des colisages (plusieurs)

    private void ColisagePopup_Opened(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            ColisageFilterBox.Focus();
            ColisageList.SelectedIndex = ColisageList.Items.Count > 0 ? 0 : -1;
        }, System.Windows.Threading.DispatcherPriority.Input);

    private void ColisageFilter_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case System.Windows.Input.Key.Down when ColisageList.Items.Count > 0:
                ColisageList.SelectedIndex = Math.Min(ColisageList.Items.Count - 1, ColisageList.SelectedIndex + 1);
                ColisageList.ScrollIntoView(ColisageList.SelectedItem);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Up when ColisageList.Items.Count > 0:
                ColisageList.SelectedIndex = Math.Max(0, ColisageList.SelectedIndex - 1);
                ColisageList.ScrollIntoView(ColisageList.SelectedItem);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Enter:
                OpenSelectedColisage();
                e.Handled = true;
                break;
        }
    }

    private void ColisageList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            OpenSelectedColisage();
            e.Handled = true;
        }
    }

    private void ColisageList_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: ViewModels.ColisageLink link } && DataContext is ViewModels.ArticlesViewModel vm)
        {
            vm.OpenColisageLinkCommand.Execute(link);
        }
    }

    private void OpenSelectedColisage()
    {
        if (DataContext is ViewModels.ArticlesViewModel vm)
        {
            vm.OpenColisageLinkCommand.Execute(ColisageList.SelectedItem as ViewModels.ColisageLink);
        }
    }
}
