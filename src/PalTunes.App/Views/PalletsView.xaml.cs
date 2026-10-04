using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using PalTunes.App.ViewModels;

namespace PalTunes.App.Views;

public partial class PalletsView : UserControl
{
    public PalletsView()
    {
        InitializeComponent();
        // HelixToolkit réinitialise sa caméra au chargement : recadrage ensuite.
        Preview.Loaded += (_, _) => Dispatcher.BeginInvoke(() => Controls.CameraHelper.Iso(Preview, (DataContext as PalletsViewModel)?.Preview), System.Windows.Threading.DispatcherPriority.Background);
        // Vue masquée au premier calcul (taille nulle) : recadrage dès qu'elle reçoit une taille.
        Preview.SizeChanged += (_, e) =>
        {
            if (e.PreviousSize.Width < 1)
            {
                Controls.CameraHelper.Iso(Preview, (DataContext as PalletsViewModel)?.Preview);
            }
            else
            {
                Controls.CameraHelper.Refit(Preview, (DataContext as PalletsViewModel)?.Preview);
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
        if (e.PropertyName == nameof(PalletsViewModel.Preview))
        {
            Dispatcher.BeginInvoke(() => Controls.CameraHelper.Iso(Preview, ((PalletsViewModel)sender!).Preview), DispatcherPriority.Background);
        }
    }

    /// <summary>Dimension, construction ou couleur modifiée : l'aperçu 3D suit.</summary>
    private void Form_SourceUpdated(object? sender, DataTransferEventArgs e)
    {
        if (DataContext is PalletsViewModel vm)
        {
            vm.RefreshPreview();
        }
    }
}
