using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PalTunes.App.ViewModels;

namespace PalTunes.App.Views;

/// <summary>Espace « Gestion des conditionnements » : arborescence, fiche résumée, aperçu 3D de la solution enregistrée.</summary>
public partial class PackagingLibraryView : UserControl
{
    public PackagingLibraryView()
    {
        InitializeComponent();
        Preview.Loaded += (_, _) => Dispatcher.BeginInvoke(() => Controls.CameraHelper.Iso(Preview, Vm?.Preview), DispatcherPriority.Background);
        Preview.SizeChanged += (_, e) =>
        {
            if (e.PreviousSize.Width < 1)
            {
                Controls.CameraHelper.Iso(Preview, Vm?.Preview);
            }
            else
            {
                Controls.CameraHelper.Refit(Preview, Vm?.Preview);
            }
        };
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is INotifyPropertyChanged vm)
            {
                vm.PropertyChanged += (_, a) =>
                {
                    if (a.PropertyName == nameof(PackagingLibraryViewModel.Preview))
                    {
                        Dispatcher.BeginInvoke(() => Controls.CameraHelper.Iso(Preview, Vm?.Preview), DispatcherPriority.Background);
                    }
                };
            }
        };
    }

    private PackagingLibraryViewModel? Vm => DataContext as PackagingLibraryViewModel;

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => Vm?.SelectNode(e.NewValue as TreeNode);

    /// <summary>Un seul colisage : ouvert directement ; plusieurs : menu de choix sous le bouton.</summary>
    private void ColisageButton_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return;
        }

        if (vm.Colisages.Count == 1)
        {
            vm.OpenColisageCommand.Execute(vm.Colisages[0]);
        }
        else if (ColisageButton.ContextMenu is { } menu)
        {
            menu.DataContext = vm;
            menu.PlacementTarget = ColisageButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as TreeView)?.SelectedItem is TreeNode { Packaging: not null })
        {
            Vm?.OpenCommand.Execute(null);
        }
    }
}
