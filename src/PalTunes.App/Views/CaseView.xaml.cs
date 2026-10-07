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
            Controls.Motion.Finish(Vm?.Scene3D);
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
            if (e.OldValue is CaseViewModel old)
            {
                old.PropertyChanged -= OnViewModelPropertyChanged;
                old.BuildAnimationRequested -= OnBuildRequested;
            }

            if (e.NewValue is CaseViewModel vm)
            {
                vm.PropertyChanged += OnViewModelPropertyChanged;
                vm.BuildAnimationRequested += OnBuildRequested;
            }
        };

        // Un geste dans la vue 3D termine aussitôt l'animation.
        Viewport.PreviewMouseDown += (_, _) => StopMotion();
        Viewport.PreviewMouseWheel += (_, _) => StopMotion();
    }

    private void StopMotion()
    {
        Controls.Motion.Finish(Vm?.Scene3D);
        Controls.CameraHelper.Stop(Viewport);
    }

    /// <summary>
    /// Nouvelle caisse : les rabats s'ouvrent puis les produits sont déposés couche par couche ; ouverture seule : les
    /// rabats s'ouvrent (caisse fermée : rien à animer).
    /// </summary>
    private void OnBuildRequested(bool full) => Dispatcher.BeginInvoke(() =>
    {
        if (full)
        {
            Controls.CameraHelper.Iso(Viewport, Vm?.Scene3D);
        }
        else
        {
            // Ouverture : cadrage final (rabats ouverts) calculé avant l'animation, rejoint en douceur.
            Controls.CameraHelper.RefitSmooth(Viewport, Vm?.Scene3D);
        }

        if (Vm is { IsOpen: true } vm)
        {
            Controls.Motion.PlayBuild(vm.Scene3D, openCase: true, dropItems: full);
        }
        else if (full)
        {
            Controls.Motion.PlayBuild(Vm?.Scene3D);
        }
    }, DispatcherPriority.Background);

    /// <summary>Vitesse changée par l'utilisateur : la construction est rejouée à la nouvelle vitesse.</summary>
    private void BuildSpeed_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { IsLoaded: true, IsKeyboardFocusWithin: true } || sender is ComboBox { IsDropDownOpen: true })
        {
            ReplayBuild_Click(sender, e);
        }
    }

    private bool _closing;

    /// <summary>
    /// Ouvrir / fermer la caisse. Fermeture : les petits rabats puis les grands se rabattent sur les produits, et la
    /// caisse fermée (ruban adhésif) remplace la vue ouverte à la fin du mouvement.
    /// </summary>
    private void ToggleOpen_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Vm is not { } vm || _closing)
        {
            return;
        }

        if (!vm.IsOpen)
        {
            vm.ToggleOpenCommand.Execute(null);
            return;
        }

        _closing = true;
        var done = Controls.Motion.PlayClose(vm.Scene3D, () =>
        {
            _closing = false;
            if (vm.IsOpen)
            {
                vm.ToggleOpenCommand.Execute(null);
            }
        });
        if (!done)
        {
            _closing = false;
            vm.ToggleOpenCommand.Execute(null);
        }
    }

    private void ReplayBuild_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Controls.Motion.Finish(Vm?.Scene3D);
        Controls.Motion.PlayBuild(Vm?.Scene3D, openCase: Vm?.IsOpen == true);
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
            case nameof(CaseViewModel.IsOpen) when Vm is { IsOpen: false }:
                // Fermeture : la caméra glisse vers le cadrage de la caisse fermée (ouverture : voir OnBuildRequested).
                Dispatcher.BeginInvoke(() => Controls.CameraHelper.RefitSmooth(Viewport, Vm?.Scene3D), DispatcherPriority.Background);
                break;
        }
    }
}
