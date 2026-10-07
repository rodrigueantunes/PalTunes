using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace PalTunes.App.Views.Controls;

/// <summary>
/// Animations de PalTunes, pensées pour ne jamais ralentir l'application :
/// <list type="bullet">
/// <item>seules des transformations et des opacités sont animées (rendu composé, aucun recalcul de mise en page) ;</item>
/// <item>3D : une seule transformation partagée par couche (pas une par produit), durée totale bornée (≤ 1,3 s) ;</item>
/// <item>tout geste de l'utilisateur dans la vue 3D termine l'animation immédiatement ;</item>
/// <item>désactivables (préférence « Animations ») et coupées si Windows n'affiche pas les animations.</item>
/// </list>
/// </summary>
public static class Motion
{
    /// <summary>Préférence de l'utilisateur (réglages).</summary>
    public static bool UserEnabled { get; set; } = true;

    /// <summary>Vitesse de la construction 3D : 0,5 lente, 1 normale, 2 rapide, 4 très rapide (durées divisées d'autant).</summary>
    public static double BuildSpeed { get; set; } = 1;

    /// <summary>Animations jouées : préférence et réglage « Afficher les animations » de Windows.</summary>
    public static bool Enabled => UserEnabled && SystemParameters.ClientAreaAnimation;

    private static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction Smooth = Freeze(new SineEase { EasingMode = EasingMode.EaseInOut });

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    // ================================================================== 3D : construction de la scène

    /// <summary>Rôle d'un modèle de la scène pour l'animation de construction.</summary>
    public enum Part
    {
        /// <summary>Immobile (palette, parois, ombre) : visible dès le départ.</summary>
        Static,

        /// <summary>Produit ou intercalaire d'une couche : déposé avec sa couche.</summary>
        Layer,

        /// <summary>Coiffe, cornières : posées après la dernière couche.</summary>
        Top,

        /// <summary>Film, cerclage : déroulé du bas vers le haut à la fin.</summary>
        Wrap,

        /// <summary>Rabat de caisse : s'ouvre au début.</summary>
        Flap,

        /// <summary>Paroi avant translucide de la caisse ouverte : devient opaque à la fermeture.</summary>
        Glass,

        /// <summary>Ruban adhésif, caché dans la caisse ouverte : déroulé à la fermeture.</summary>
        Tape
    }

    public static readonly DependencyProperty PartProperty =
        DependencyProperty.RegisterAttached("Part", typeof(Part), typeof(Motion), new PropertyMetadata(Part.Static));

    public static void SetPart(DependencyObject d, Part value) => d.SetValue(PartProperty, value);
    public static Part GetPart(DependencyObject d) => (Part)d.GetValue(PartProperty);

    /// <summary>Numéro de couche (1 = bas) d'un modèle « Layer ».</summary>
    public static readonly DependencyProperty LayerProperty =
        DependencyProperty.RegisterAttached("Layer", typeof(int), typeof(Motion), new PropertyMetadata(0));

    public static void SetLayer(DependencyObject d, int value) => d.SetValue(LayerProperty, value);
    public static int GetLayer(DependencyObject d) => (int)d.GetValue(LayerProperty);

    /// <summary>Rabat : angle ouvert (°) ; fermé = ±90° vers l'intérieur.</summary>
    public static readonly DependencyProperty OpenAngleProperty =
        DependencyProperty.RegisterAttached("OpenAngle", typeof(double), typeof(Motion), new PropertyMetadata(0.0));

    public static void SetOpenAngle(DependencyObject d, double value) => d.SetValue(OpenAngleProperty, value);
    public static double GetOpenAngle(DependencyObject d) => (double)d.GetValue(OpenAngleProperty);

    /// <summary>Animation en cours d'une scène : propriétés animées et leur valeur finale (pour terminer d'un coup).</summary>
    private sealed class Run
    {
        public readonly List<(Animatable Target, DependencyProperty Property, double Final)> Tracks = [];

        /// <summary>Action à la fin de l'animation (fermeture de la caisse : vue fermée).</summary>
        public Action? OnFinish;
    }

    private static readonly ConditionalWeakTable<Model3DGroup, Run> Runs = new();

    /// <summary>
    /// Construction de la scène : rabats de la caisse qui s'ouvrent (<paramref name="openCase"/>), puis couches déposées
    /// une à une (du bas vers le haut), coiffe et cornières, et enfin film et cerclage déroulés.
    /// </summary>
    public static void PlayBuild(Model3DGroup? root, bool openCase = false, bool dropItems = true)
    {
        if (root == null || root.IsFrozen || !Enabled)
        {
            return;
        }

        Finish(root);
        var k = 1 / Math.Clamp(BuildSpeed, 0.25, 4); // facteur de durée
        var run = new Run();
        Runs.AddOrUpdate(root, run);
        var models = root.Children.ToList();
        var bounds = root.Bounds;
        var height = bounds.IsEmpty ? 1 : bounds.SizeZ;
        var drop = Math.Max(0.25, height * 0.55);

        // Ouverture, à l'inverse exact de la fermeture (on part de l'image de la caisse fermée) : ruban retiré (patte
        // d'arrivée, bande, patte de départ), grands rabats relevés, petits rabats ensuite, parois avant qui redeviennent
        // translucides.
        var flapTime = 0.0;
        if (openCase)
        {
            var geometry = models.OfType<GeometryModel3D>().ToList();
            var t0 = 0.0;
            foreach (var tape in geometry.Where(m => GetPart(m) == Part.Tape).OrderByDescending(GetLayer))
            {
                var b = tape.Geometry.Bounds;
                var strip = GetLayer(tape) == 1;
                var scale = strip
                    ? new ScaleTransform3D(1, 1, 1, b.X, b.Y + b.SizeY / 2, b.Z)
                    : new ScaleTransform3D(1, 1, 1, b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ);
                tape.Transform = scale;
                var duration = (strip ? 0.4 : 0.12) * k;
                Animate(run, scale, strip ? ScaleTransform3D.ScaleXProperty : ScaleTransform3D.ScaleZProperty, 1, 0, t0, duration, strip ? Smooth : EaseOut);
                t0 += duration;
            }

            var longBegin = t0 + 0.05 * k;
            var count = 0;
            foreach (var flap in geometry.Where(m => GetPart(m) == Part.Flap))
            {
                if (RotationOf(flap) is { IsFrozen: false } rotation)
                {
                    var open = GetOpenAngle(flap);
                    var closed = open >= 0 ? -90 : 90;
                    var shortFlap = Math.Abs(rotation.Axis.Y) > 0.5;
                    var begin = longBegin + (shortFlap ? 0.35 * k : 0);
                    Animate(run, rotation, AxisAngleRotation3D.AngleProperty, closed, open, begin, 0.6 * k, Smooth);
                    if (shortFlap)
                    {
                        // Cachés sous les grands rabats tant qu'ils sont fermés (pas de scintillement).
                        var show = new ScaleTransform3D(0, 0, 0);
                        Attach(flap, show);
                        foreach (var prop in new[] { ScaleTransform3D.ScaleXProperty, ScaleTransform3D.ScaleYProperty, ScaleTransform3D.ScaleZProperty })
                        {
                            var key = new DoubleAnimationUsingKeyFrames();
                            key.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                            key.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(longBegin + 0.1 * k))));
                            key.Freeze();
                            show.BeginAnimation(prop, key);
                            run.Tracks.Add((show, prop, 1));
                        }
                    }

                    count++;
                }
            }

            foreach (var brush in geometry.SelectMany(FadingBrushes))
            {
                Animate(run, brush, Brush.OpacityProperty, 1, brush.Opacity, longBegin, 0.6 * k, Smooth);
            }

            flapTime = count > 0 ? longBegin + 0.95 * k : 0;
        }

        if (!dropItems)
        {
            return;
        }

        // Couches : une transformation partagée par couche, invisible jusqu'à sa dépose.
        var layered = models.Where(m => GetPart(m) == Part.Layer).GroupBy(GetLayer).OrderBy(g => g.Key).ToList();
        var top = models.Where(m => GetPart(m) == Part.Top).ToList();
        var wrap = models.Where(m => GetPart(m) == Part.Wrap).ToList();
        var steps = layered.Count + (top.Count > 0 ? 1 : 0);
        // Vitesse normale : couches posées en 1,5 s environ (0,08 à 0,28 s d'écart), 0,55 s de descente chacune.
        var stagger = steps == 0 ? 0 : Math.Clamp(1.5 / steps, 0.08, 0.28) * k;
        var dur = 0.55 * k;
        var t = flapTime;
        foreach (var group in layered)
        {
            Drop(run, group.ToList(), drop, t, dur);
            t += stagger;
        }

        if (top.Count > 0)
        {
            Drop(run, top, drop * 0.6, t, dur);
            t += stagger;
        }

        // Film et cerclage : déroulés du bas (dessus de la palette) vers le haut.
        if (wrap.Count > 0 && !bounds.IsEmpty)
        {
            var bottom = wrap.Min(m => m.Bounds.IsEmpty ? bounds.Z : m.Bounds.Z);
            var scale = new ScaleTransform3D(1, 1, 0, bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bottom);
            foreach (var m in wrap)
            {
                Attach(m, scale);
            }

            Animate(run, scale, ScaleTransform3D.ScaleZProperty, 0, 1, t + dur * 0.5, 0.7 * k, EaseOut);
        }
    }

    /// <summary>
    /// Fermeture de la caisse : petits rabats d'abord, puis grands rabats, rabattus sur les produits ; <paramref name="done"/>
    /// est appelé à la fin (ou tout de suite si un clic termine l'animation). Faux si rien à animer (animations coupées).
    /// </summary>
    public static bool PlayClose(Model3DGroup? root, Action done)
    {
        if (root == null || root.IsFrozen || !Enabled)
        {
            return false;
        }

        Finish(root);
        var models = root.Children.OfType<GeometryModel3D>().ToList();
        var flaps = models.Where(m => GetPart(m) == Part.Flap)
            .Select(m => (Model: m, Rotation: RotationOf(m)))
            .Where(x => x.Rotation is { IsFrozen: false })
            .ToList();
        if (flaps.Count == 0)
        {
            return false;
        }

        // Déroulé (vitesse normale) : petits rabats 0 → 0,5 s ; grands rabats 0,35 → 0,9 s ; parois avant opaques
        // 0,3 → 0,9 s ; ruban : patte de départ, bande sur toute la longueur, patte d'arrivée, jusqu'à ≈ 1,7 s. L'image
        // finale est celle de la caisse fermée : le passage à la vue fermée ne se voit pas.
        var k = 1 / Math.Clamp(BuildSpeed, 0.25, 4);
        var run = new Run();
        Runs.AddOrUpdate(root, run);
        var end = 0.0;
        foreach (var (model, rotation) in flaps)
        {
            var open = GetOpenAngle(model);
            var closed = open >= 0 ? -90 : 90;
            var shortFlap = Math.Abs(rotation!.Axis.Y) > 0.5; // charnière le long de la largeur : petit rabat
            var begin = (shortFlap ? 0.0 : 0.35) * k;
            var duration = (shortFlap ? 0.5 : 0.55) * k;
            Animate(run, rotation, AxisAngleRotation3D.AngleProperty, open, closed, begin, duration, Smooth);
            end = Math.Max(end, begin + duration);
            if (shortFlap)
            {
                // Sous les grands rabats une fois fermés : retirés juste avant (pas de scintillement entre faces confondues).
                var hide = new ScaleTransform3D(1, 1, 1);
                Attach(model, hide);
                foreach (var prop in new[] { ScaleTransform3D.ScaleXProperty, ScaleTransform3D.ScaleYProperty, ScaleTransform3D.ScaleZProperty })
                {
                    var key = new DoubleAnimationUsingKeyFrames();
                    key.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    key.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.86 * k))));
                    key.Freeze();
                    hide.BeginAnimation(prop, key);
                    run.Tracks.Add((hide, prop, 0));
                }
            }
        }

        // Parois avant : de translucides à opaques pendant la fermeture des grands rabats.
        foreach (var brush in models.SelectMany(FadingBrushes))
        {
            Animate(run, brush, Brush.OpacityProperty, brush.Opacity, 1, 0.3 * k, 0.6 * k, Smooth);
        }

        // Ruban : patte de départ (descend sur le côté), bande déroulée sur la longueur, patte d'arrivée.
        var t = Math.Max(end, 0.9 * k) + 0.05 * k;
        foreach (var tape in models.Where(m => GetPart(m) == Part.Tape).OrderBy(GetLayer))
        {
            var b = tape.Geometry.Bounds;
            var strip = GetLayer(tape) == 1;
            var scale = strip
                ? new ScaleTransform3D(0, 1, 1, b.X, b.Y + b.SizeY / 2, b.Z)
                : new ScaleTransform3D(1, 1, 0, b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ);
            tape.Transform = scale;
            var duration = (strip ? 0.5 : 0.16) * k;
            Animate(run, scale, strip ? ScaleTransform3D.ScaleXProperty : ScaleTransform3D.ScaleZProperty, 0, 1, t, duration, strip ? Smooth : EaseOut);
            t += duration;
        }

        end = Math.Max(end, t);
        run.OnFinish = done;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(end + 0.04) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (Runs.TryGetValue(root, out var current) && ReferenceEquals(current, run))
            {
                Finish(root);
            }
        };
        timer.Start();
        return true;
    }

    /// <summary>Rotation d'un rabat, y compris quand une animation précédente l'a regroupée avec une autre transformation.</summary>
    private static AxisAngleRotation3D? RotationOf(Model3D model)
    {
        static AxisAngleRotation3D? Find(Transform3D? t) => t switch
        {
            RotateTransform3D { Rotation: AxisAngleRotation3D r } => r,
            Transform3DGroup g => g.Children.Select(Find).FirstOrDefault(r => r != null),
            _ => null
        };

        return Find(model.Transform);
    }

    /// <summary>Pinceaux translucides animables d'un modèle (parois et rabats avant de la caisse ouverte).</summary>
    private static IEnumerable<Brush> FadingBrushes(GeometryModel3D model) =>
        new[] { model.Material, model.BackMaterial }.OfType<MaterialGroup>().SelectMany(g => g.Children)
            .Select(c => c switch { DiffuseMaterial d => d.Brush, SpecularMaterial sp => sp.Brush, _ => null })
            .OfType<Brush>().Where(b => !b.IsFrozen).Distinct();

    /// <summary>Dépose d'un groupe de modèles : apparition à son heure, descente depuis le haut, arrivée amortie.</summary>
    private static void Drop(Run run, List<Model3D> group, double drop, double begin, double duration)
    {
        var b = Rect3D.Empty;
        foreach (var m in group)
        {
            b.Union(m.Bounds);
        }

        if (b.IsEmpty)
        {
            return;
        }

        var show = new ScaleTransform3D(0, 0, 0, b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z);
        var move = new TranslateTransform3D(0, 0, drop);
        var both = new Transform3DGroup { Children = { show, move } };
        foreach (var m in group)
        {
            Attach(m, both);
        }

        foreach (var p in new[] { ScaleTransform3D.ScaleXProperty, ScaleTransform3D.ScaleYProperty, ScaleTransform3D.ScaleZProperty })
        {
            var appear = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.Zero };
            appear.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            appear.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(begin))));
            appear.Freeze();
            show.BeginAnimation(p, appear);
            run.Tracks.Add((show, p, 1));
        }

        Animate(run, move, TranslateTransform3D.OffsetZProperty, drop, 0, begin, duration, EaseOut);
    }

    /// <summary>Ajoute la transformation d'animation à celle du modèle (rabats : rotation conservée).</summary>
    private static void Attach(Model3D model, Transform3D extra)
    {
        if (model.IsFrozen)
        {
            return;
        }

        model.Transform = model.Transform is { } existing && existing != Transform3D.Identity
            ? new Transform3DGroup { Children = { existing, extra } }
            : extra;
    }

    private static void Animate(Run run, Animatable target, DependencyProperty property, double from, double to, double begin, double duration, IEasingFunction ease)
    {
        target.SetValue(property, from);
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(duration))
        {
            BeginTime = TimeSpan.FromSeconds(begin),
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };
        animation.Freeze();
        target.BeginAnimation(property, animation);
        run.Tracks.Add((target, property, to));
    }

    /// <summary>Termine immédiatement l'animation de la scène (geste de l'utilisateur, nouvelle scène).</summary>
    public static void Finish(Model3DGroup? root)
    {
        if (root == null || !Runs.TryGetValue(root, out var run))
        {
            return;
        }

        foreach (var (target, property, final) in run.Tracks)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, final);
        }

        run.Tracks.Clear();
        Runs.Remove(root);
        var onFinish = run.OnFinish;
        run.OnFinish = null;
        onFinish?.Invoke();
    }

    // ================================================================== Interface : apparitions

    /// <summary>Style d'apparition d'un élément quand il devient visible (ou se charge).</summary>
    public enum Entrance
    {
        None,

        /// <summary>Écran : fondu et léger glissement de la droite.</summary>
        Slide,

        /// <summary>Fond de fenêtre modale : fondu.</summary>
        Fade,

        /// <summary>Fenêtre modale : fondu et léger grossissement.</summary>
        Pop,

        /// <summary>Carte d'une liste : fondu et légère montée.</summary>
        Rise
    }

    public static readonly DependencyProperty AppearProperty =
        DependencyProperty.RegisterAttached("Appear", typeof(Entrance), typeof(Motion), new PropertyMetadata(Entrance.None, OnAppearChanged));

    public static void SetAppear(UIElement e, Entrance value) => e.SetValue(AppearProperty, value);
    public static Entrance GetAppear(UIElement e) => (Entrance)e.GetValue(AppearProperty);

    private static void OnAppearChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnVisibleChanged;
        element.Loaded -= OnLoadedAppear;
        if ((Entrance)e.NewValue == Entrance.Rise)
        {
            element.Loaded += OnLoadedAppear;
        }
        else if ((Entrance)e.NewValue != Entrance.None)
        {
            element.IsVisibleChanged += OnVisibleChanged;
        }
    }

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is FrameworkElement element)
        {
            PlayEntrance(element, GetAppear(element));
        }
    }

    private static void OnLoadedAppear(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            PlayEntrance(element, GetAppear(element));
        }
    }

    public static void PlayEntrance(FrameworkElement element, Entrance kind)
    {
        if (!Enabled || kind == Entrance.None)
        {
            return;
        }

        var (duration, dx, dy, scale) = kind switch
        {
            Entrance.Slide => (0.22, 12.0, 0.0, 1.0),
            Entrance.Pop => (0.18, 0.0, 6.0, 0.97),
            Entrance.Rise => (0.16, 0.0, 6.0, 1.0),
            _ => (0.15, 0.0, 0.0, 1.0)
        };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(duration)) { EasingFunction = EaseOut };
        fade.Freeze();
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        if (dx == 0 && dy == 0 && Math.Abs(scale - 1) < 1e-9)
        {
            return;
        }

        // Transformation propre à l'élément (jamais celle d'un style partagé).
        var translate = new TranslateTransform(dx, dy);
        var scaling = new ScaleTransform(scale, scale);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new TransformGroup { Children = { scaling, translate } };
        var span = TimeSpan.FromSeconds(duration + 0.04);
        translate.BeginAnimation(TranslateTransform.XProperty, Frozen(new DoubleAnimation(dx, 0, span) { EasingFunction = EaseOut }));
        translate.BeginAnimation(TranslateTransform.YProperty, Frozen(new DoubleAnimation(dy, 0, span) { EasingFunction = EaseOut }));
        if (Math.Abs(scale - 1) > 1e-9)
        {
            scaling.BeginAnimation(ScaleTransform.ScaleXProperty, Frozen(new DoubleAnimation(scale, 1, span) { EasingFunction = EaseOut }));
            scaling.BeginAnimation(ScaleTransform.ScaleYProperty, Frozen(new DoubleAnimation(scale, 1, span) { EasingFunction = EaseOut }));
        }
    }

    private static DoubleAnimation Frozen(DoubleAnimation a)
    {
        a.Freeze();
        return a;
    }

    // ================================================================== Interface : appui des boutons

    /// <summary>Bouton : léger enfoncement à l'appui (60 ms), retour souple au relâchement.</summary>
    public static readonly DependencyProperty PressProperty =
        DependencyProperty.RegisterAttached("Press", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnPressChanged));

    public static void SetPress(UIElement e, bool value) => e.SetValue(PressProperty, value);
    public static bool GetPress(UIElement e) => (bool)e.GetValue(PressProperty);

    private static void OnPressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button)
        {
            return;
        }

        button.PreviewMouseLeftButtonDown -= OnPressDown;
        button.PreviewMouseLeftButtonUp -= OnPressUp;
        button.MouseLeave -= OnPressUp;
        if (e.NewValue is true)
        {
            button.PreviewMouseLeftButtonDown += OnPressDown;
            button.PreviewMouseLeftButtonUp += OnPressUp;
            button.MouseLeave += OnPressUp;
        }
    }

    private static ScaleTransform? PressTransform(UIElement element, bool create)
    {
        if (element.RenderTransform is ScaleTransform { IsFrozen: false } existing)
        {
            return existing;
        }

        if (!create || (element.RenderTransform != null && element.RenderTransform != Transform.Identity))
        {
            return null;
        }

        var scale = new ScaleTransform(1, 1);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        return scale;
    }

    private static void OnPressDown(object sender, MouseButtonEventArgs e)
    {
        if (!Enabled || sender is not UIElement { IsEnabled: true } element || PressTransform(element, true) is not { } scale)
        {
            return;
        }

        var down = Frozen(new DoubleAnimation(0.965, TimeSpan.FromSeconds(0.06)));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, down);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, down);
    }

    private static void OnPressUp(object sender, EventArgs e)
    {
        if (sender is not UIElement element || PressTransform(element, false) is not { } scale)
        {
            return;
        }

        var up = Frozen(new DoubleAnimation(1, TimeSpan.FromSeconds(0.14)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, up);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, up);
    }

    // ================================================================== Interface : onglets

    /// <summary>Contenu d'onglet : fondu léger à chaque changement d'onglet.</summary>
    public static readonly DependencyProperty TabFadeProperty =
        DependencyProperty.RegisterAttached("TabFade", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnTabFadeChanged));

    public static void SetTabFade(TabControl e, bool value) => e.SetValue(TabFadeProperty, value);
    public static bool GetTabFade(TabControl e) => (bool)e.GetValue(TabFadeProperty);

    private static void OnTabFadeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TabControl tabs)
        {
            return;
        }

        tabs.SelectionChanged -= OnTabChanged;
        if (e.NewValue is true)
        {
            tabs.SelectionChanged += OnTabChanged;
        }
    }

    private static void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!Enabled || e.OriginalSource != sender || sender is not TabControl tabs || !tabs.IsLoaded)
        {
            return;
        }

        if (tabs.Template?.FindName("PART_SelectedContentHost", tabs) is FrameworkElement host)
        {
            var fade = Frozen(new DoubleAnimation(0.2, 1, TimeSpan.FromSeconds(0.16)) { EasingFunction = EaseOut });
            host.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
