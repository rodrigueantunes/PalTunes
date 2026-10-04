using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Threading;
using PalTunes.App.ViewModels;

namespace PalTunes.App.Views;

/// <summary>Espace « Caisses » : catalogue des caisses, à l'image des palettes.</summary>
public partial class CaseTypesView : UserControl
{
    public CaseTypesView()
    {
        InitializeComponent();
        Preview.Loaded += (_, _) => Dispatcher.BeginInvoke(() => Controls.CameraHelper.Iso(Preview, Vm?.CatalogPreview), DispatcherPriority.Background);
        Preview.SizeChanged += (_, e) =>
        {
            if (e.PreviousSize.Width < 1)
            {
                Controls.CameraHelper.Iso(Preview, Vm?.CatalogPreview);
            }
            else
            {
                Controls.CameraHelper.Refit(Preview, Vm?.CatalogPreview);
            }
        };
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is INotifyPropertyChanged vm)
            {
                vm.PropertyChanged += (_, a) =>
                {
                    if (a.PropertyName == nameof(CaseViewModel.CatalogPreview))
                    {
                        Dispatcher.BeginInvoke(() => Controls.CameraHelper.Iso(Preview, Vm?.CatalogPreview), DispatcherPriority.Background);
                    }
                };
            }
        };
    }

    private CaseViewModel? Vm => DataContext as CaseViewModel;
}
