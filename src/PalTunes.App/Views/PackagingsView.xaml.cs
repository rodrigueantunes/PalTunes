using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using PalTunes.App.ViewModels;

namespace PalTunes.App.Views;

/// <summary>
/// Code-behind limité à la vue : caméras 3D, survol (test d'intersection), regroupement de la spécification,
/// marquage « modifié » de l'éditeur.
/// </summary>
public partial class PackagingsView : UserControl
{
    public PackagingsView()
    {
        InitializeComponent();
        // HelixToolkit réinitialise sa caméra au chargement : recadrage ensuite.
        Viewport.Loaded += (_, _) => Dispatcher.BeginInvoke(() => Controls.CameraHelper.Iso(Viewport, SceneModel), System.Windows.Threading.DispatcherPriority.Background);
        // Vue masquée au premier calcul (taille nulle) : recadrage dès qu'elle reçoit une taille.
        Viewport.SizeChanged += (_, e) =>
        {
            Controls.Motion.Finish(Vm?.Scene3D);
            if (e.PreviousSize.Width < 1)
            {
                Controls.CameraHelper.Iso(Viewport, SceneModel);
            }
            else
            {
                Controls.CameraHelper.Refit(Viewport, SceneModel);
            }
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is PackagingsViewModel old)
            {
                old.PropertyChanged -= OnViewModelPropertyChanged;
                old.BuildAnimationRequested -= OnBuildRequested;
            }

            if (e.NewValue is PackagingsViewModel vm)
            {
                vm.PropertyChanged += OnViewModelPropertyChanged;
                vm.BuildAnimationRequested += OnBuildRequested;
            }
        };

        // Un geste dans la vue 3D termine aussitôt l'animation et la transition de caméra.
        Viewport.PreviewMouseDown += (_, _) => StopMotion();
        Viewport.PreviewMouseWheel += (_, _) => StopMotion();
    }

    private void StopMotion()
    {
        Controls.Motion.Finish(Vm?.Scene3D);
        Controls.CameraHelper.Stop(Viewport);
    }

    /// <summary>Nouvelle solution : cadrage 3/4 puis construction (après le rendu de la scène, sans bloquer).</summary>
    private void OnBuildRequested() => Dispatcher.BeginInvoke(() =>
    {
        Controls.CameraHelper.Iso(Viewport, SceneModel);
        Controls.Motion.PlayBuild(Vm?.Scene3D);
    }, DispatcherPriority.Background);

    /// <summary>Vitesse changée par l'utilisateur : la construction est rejouée à la nouvelle vitesse.</summary>
    private void BuildSpeed_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { IsLoaded: true, IsKeyboardFocusWithin: true } || sender is ComboBox { IsDropDownOpen: true })
        {
            ReplayBuild_Click(sender, e);
        }
    }

    private void ReplayBuild_Click(object sender, RoutedEventArgs e)
    {
        Controls.Motion.Finish(Vm?.Scene3D);
        Controls.Motion.PlayBuild(Vm?.Scene3D);
    }

    private PackagingsViewModel? Vm => DataContext as PackagingsViewModel;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PackagingsViewModel.CurrentUnit):
                Dispatcher.BeginInvoke(SetIsoCamera, DispatcherPriority.Background);
                break;
            case nameof(PackagingsViewModel.SpecRows):
                var view = new ListCollectionView(Vm!.SpecRows.ToList());
                view.GroupDescriptions.Add(new PropertyGroupDescription("Group"));
                SpecGrid.ItemsSource = view;
                break;
            case nameof(PackagingsViewModel.HoverText):
                HoverCard.Visibility = Vm?.HoverText == null ? Visibility.Collapsed : Visibility.Visible;
                break;
        }
    }

    /// <summary>Seules les saisies de l'utilisateur (contrôle ayant le focus) marquent le conditionnement modifié.</summary>
    private void Editor_Changed(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is UIElement { IsKeyboardFocusWithin: true })
        {
            Vm?.MarkDirty();
        }
    }

    private GeometryModel3D? HitModel(Point position)
    {
        GeometryModel3D? hit = null;
        VisualTreeHelper.HitTest(Viewport.Viewport, null, result =>
        {
            if (result is RayMeshGeometry3DHitTestResult mesh && mesh.ModelHit is GeometryModel3D model)
            {
                hit = model;
                return HitTestResultBehavior.Stop;
            }

            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(position));
        return hit;
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (Vm == null)
        {
            return;
        }

        if (e.RightButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed)
        {
            Vm.HoverFromModel(null);
            return;
        }

        var position = e.GetPosition(Viewport.Viewport);
        Vm.HoverFromModel(HitModel(position));
        if (Vm.HoverText == null)
        {
            return;
        }

        HoverCard.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = HoverCard.DesiredSize;
        var host = new Size(Viewport.ActualWidth, Viewport.ActualHeight);
        var x = position.X + 18 + size.Width > host.Width ? position.X - size.Width - 12 : position.X + 18;
        var y = Math.Min(Math.Max(4, position.Y - 10), host.Height - size.Height - 4);
        Canvas.SetLeft(HoverCard, Math.Max(4, x));
        Canvas.SetTop(HoverCard, Math.Max(4, y));
    }

    private void Viewport_MouseLeave(object sender, MouseEventArgs e) => Vm?.HoverFromModel(null);

    private Model3D? SceneModel => Vm?.Scene3D;

    private void SetIsoCamera() => Controls.CameraHelper.Iso(Viewport, SceneModel);

    private void CameraIso_Click(object sender, RoutedEventArgs e) => Controls.CameraHelper.IsoSmooth(Viewport, SceneModel);

    private void CameraTop_Click(object sender, RoutedEventArgs e) =>
        Controls.CameraHelper.LookSmooth(Viewport, SceneModel, new Vector3D(0, 0, 1), new Vector3D(0, 1, 0));

    private void CameraSide_Click(object sender, RoutedEventArgs e) =>
        Controls.CameraHelper.LookSmooth(Viewport, SceneModel, new Vector3D(0, -1, 0), new Vector3D(0, 0, 1));

    private void CameraFront_Click(object sender, RoutedEventArgs e) =>
        Controls.CameraHelper.LookSmooth(Viewport, SceneModel, new Vector3D(-1, 0, 0), new Vector3D(0, 0, 1));

    private void ZoomExtents_Click(object sender, RoutedEventArgs e) => Controls.CameraHelper.RefitSmooth(Viewport, SceneModel);
}
