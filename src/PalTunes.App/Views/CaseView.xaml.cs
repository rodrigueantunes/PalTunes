using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Threading;
using PalTunes.App.ViewModels;

namespace PalTunes.App.Views;

public partial class CaseView : UserControl
{
    public CaseView()
    {
        InitializeComponent();
        Viewport.Loaded += (_, _) => Dispatcher.BeginInvoke(() => Controls.CameraHelper.Iso(Viewport, Vm?.Scene3D), DispatcherPriority.Background);
        Viewport.SizeChanged += (_, e) =>
        {
            if (e.PreviousSize.Width < 1)
            {
                Controls.CameraHelper.Iso(Viewport, Vm?.Scene3D);
            }
            else
            {
                Controls.CameraHelper.Refit(Viewport, Vm?.Scene3D);
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

    private CaseViewModel? Vm => DataContext as CaseViewModel;

    /// <summary>Nouvelle solution : vue 3/4 ; ouverture / fermeture de la caisse : même angle de vue, recadré.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CaseViewModel.CurrentSolution):
                Dispatcher.BeginInvoke(() => Controls.CameraHelper.Iso(Viewport, Vm?.Scene3D), DispatcherPriority.Background);
                break;
            case nameof(CaseViewModel.IsOpen):
                Dispatcher.BeginInvoke(() => Controls.CameraHelper.Refit(Viewport, Vm?.Scene3D), DispatcherPriority.Background);
                break;
        }
    }
}
