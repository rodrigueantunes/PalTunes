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

    private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as TreeView)?.SelectedItem is TreeNode { Packaging: not null })
        {
            Vm?.OpenCommand.Execute(null);
        }
    }
}
