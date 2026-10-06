using System.Windows.Media;
using System.Windows.Media.Media3D;
using PalTunes.Core.Models;

namespace PalTunes.App.Services;

public sealed class SceneResult
{
    public Model3DGroup Root { get; } = new();
    public Dictionary<GeometryModel3D, Placement> ByModel { get; } = [];

    /// <summary>Dimensions de la scène en mètres (caméras).</summary>
    public double SizeX { get; set; } = 1.2;

    public double SizeY { get; set; } = 0.8;
    public double SizeZ { get; set; } = 1.5;
}

/// <summary>
/// Scène 3D d'une unité de charge (mètres, Z vers le haut, Z = 0 sur le dessus des palettes) : palettes physiques
/// selon leur construction, produits (pavés, cylindres), intercalaires, coiffe et cornières.
/// </summary>
public static class Scene3DBuilder
{
    private const double Scale = 0.001;
    private const double Gap = 0.0015;
    private const int MergeThreshold = 1500;
    private const int ShellThreshold = 8000;

    private static readonly Dictionary<Color, Material> Cache = [];

    public static readonly Material Selected = Freeze(new MaterialGroup
    {
        Children =
        {
            new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x00))),
            new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x90, 0x60, 0x00)))
        }
    });

    public static readonly Material Hover = Freeze(new MaterialGroup
    {
        Children =
        {
            new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))),
            new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x30, 0x60, 0x90)))
        }
    });

    public static Material Solid(Color color)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(color, out var material))
            {
                material = Freeze(new MaterialGroup
                {
                    Children =
                    {
                        new DiffuseMaterial(new SolidColorBrush(color)),
                        new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 25)
                    }
                });
                Cache[color] = material;
            }

            return material;
        }
    }

    public static Color Shade(Color c, double f) =>
        Color.FromRgb((byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));

    // ------------------------------------------------------------------ Unité de charge

    /// <summary>Rendu d'une caisse de colisage : paroi, couleur, ouverte (contenu visible) ou fermée (vue extérieure).</summary>
    public sealed record CaseRender(double Wall, string Color, bool Open);

    /// <summary>Type de l'article d'un produit placé (forme dessinée : bidon, bouteille, seau, cuve) ; renseigné par la fenêtre principale.</summary>
    public static Func<Guid, ArticleKind?> KindOf { get; set; } = _ => null;

    public static SceneResult Build(Solution solution, LoadUnit unit, PackagingConstraints c, Func<Guid, Color> colorOf, int visibleLayers,
        CaseRender? caseRender = null)
    {
        var scene = new SceneResult();
        var closedCase = false;
        if (solution.Base.IsCase)
        {
            var render = caseRender ?? new CaseRender(0, "#C9A26B", true);
            closedCase = !render.Open;
            if (closedCase)
            {
                AddCase(scene.Root, solution.Base.Length, solution.Base.Width, c.MaxTotalHeight, render, translucent: []);
            }
        }
        else
        {
            AddBase(scene.Root, solution.Base);
        }

        var visible = closedCase ? [] : unit.Items.Where(p => p.Layer <= visibleLayers).ToList();
        if (visible.Count > ShellThreshold)
        {
            visible = Shell(visible);
        }

        var merge = visible.Count > MergeThreshold;
        var segments = visible.Count > ShellThreshold ? 8 : visible.Count > 600 ? 12 : 24;
        if (merge)
        {
            foreach (var group in visible.GroupBy(p => (p.Layer, p.ArticleId)))
            {
                var mesh = new MeshGeometry3D();
                var kind = KindOf(group.Key.ArticleId) ?? ArticleKind.Caisse;
                foreach (var p in group)
                {
                    AddItem(mesh, p, segments, kind);
                }

                var color = colorOf(group.Key.ArticleId);
                var model = Model(mesh, Solid(group.Key.Layer % 2 == 0 ? Shade(color, 0.9) : color));
                scene.Root.Children.Add(model);
                scene.ByModel[model] = group.First();
            }
        }
        else
        {
            foreach (var p in visible)
            {
                var mesh = new MeshGeometry3D();
                AddItem(mesh, p, segments, KindOf(p.ArticleId) ?? ArticleKind.Caisse);
                var model = Model(mesh, Solid(ItemColor(p, colorOf(p.ArticleId))));
                scene.Root.Children.Add(model);
                scene.ByModel[model] = p;
            }
        }

        var m = unit.Metrics;
        if (unit.Items.Count > 0 && !solution.Base.IsCase)
        {
            AddAccessories(scene.Root, unit, c, visibleLayers, solution.Base);
        }

        if (solution.Base.IsCase && !closedCase)
        {
            // Caisse ouverte : parois du fond opaques, parois avant translucides, rabats relevés (ajoutée après le contenu).
            var render = caseRender ?? new CaseRender(0, "#C9A26B", true);
            AddCase(scene.Root, solution.Base.Length, solution.Base.Width, c.MaxTotalHeight, render, translucent: ["x0", "y0"]);
        }

        AddShadow(scene.Root);
        scene.SizeX = Math.Max(solution.Base.Length, m.EnclosureLength) * Scale;
        scene.SizeY = Math.Max(solution.Base.Width, m.EnclosureWidth) * Scale;
        scene.SizeZ = Math.Max(m.EnclosureHeight, solution.Base.PalletHeight) * Scale;
        return scene;
    }

    /// <summary>
    /// Très grand nombre de produits (petits articles : des dizaines de milliers) : seuls les produits visibles de
    /// l'extérieur sont dessinés — pourtour de chaque couche et couche du dessus. Les autres sont cachés par eux.
    /// </summary>
    private static List<Placement> Shell(List<Placement> items)
    {
        var top = items.Max(p => p.Layer);
        var shell = new List<Placement>();
        foreach (var layer in items.GroupBy(p => p.Layer))
        {
            if (layer.Key == top)
            {
                shell.AddRange(layer);
                continue;
            }

            var minX = layer.Min(p => p.X);
            var maxX = layer.Max(p => p.MaxX);
            var minY = layer.Min(p => p.Y);
            var maxY = layer.Max(p => p.MaxY);
            shell.AddRange(layer.Where(p => p.X <= minX + p.DX || p.MaxX >= maxX - p.DX || p.Y <= minY + p.DY || p.MaxY >= maxY - p.DY));
        }

        return shell;
    }

    /// <summary>Nuances en damier (pose et couche) pour distinguer deux produits voisins du même article.</summary>
    public static Color ItemColor(Placement p, Color color)
    {
        var ix = (long)Math.Floor((p.X + p.DX / 2) / Math.Max(1, p.DX));
        var iy = (long)Math.Floor((p.Y + p.DY / 2) / Math.Max(1, p.DY));
        return Math.Abs(ix + iy + p.Layer) % 2 == 0 ? Shade(color, 0.86) : color;
    }

    private static void AddItem(MeshGeometry3D mesh, Placement p, int segments, ArticleKind kind = ArticleKind.Caisse)
    {
        double x0 = p.X * Scale, y0 = p.Y * Scale, z0 = p.Z * Scale, x1 = p.MaxX * Scale, y1 = p.MaxY * Scale, z1 = p.MaxZ * Scale;
        var inner = p.InnerDiameter > 0 ? p.InnerDiameter / 2 * Scale : 0; // tube ou bobine creux

        // Autres types : forme reconnaissable dans l'enveloppe du produit (le calcul reste sur l'enveloppe).
        switch (kind)
        {
            case ArticleKind.Bidon when p.Shape == ShapeKind.Box:
                AddJerrican(mesh, x0 + Gap, y0 + Gap, z0 + Gap, x1 - Gap, y1 - Gap, z1 - Gap, segments);
                return;
            case ArticleKind.Cuve when p.Shape == ShapeKind.Box:
                AddIbc(mesh, x0 + Gap, y0 + Gap, z0 + Gap, x1 - Gap, y1 - Gap, z1 - Gap, segments);
                return;
            case ArticleKind.Bouteille when p.Shape == ShapeKind.CylinderZ:
                AddBottle(mesh, (x0 + x1) / 2, (y0 + y1) / 2, Math.Min(x1 - x0, y1 - y0) / 2 - Gap, z0 + Gap, z1 - Gap, segments);
                return;
            case ArticleKind.Seau when p.Shape == ShapeKind.CylinderZ:
                AddPail(mesh, (x0 + x1) / 2, (y0 + y1) / 2, Math.Min(x1 - x0, y1 - y0) / 2 - Gap, z0 + Gap, z1 - Gap, segments);
                return;
        }

        switch (p.Shape)
        {
            case ShapeKind.CylinderZ:
                AddCylinder(mesh, 2, z0 + Gap, z1 - Gap, (x0 + x1) / 2, (y0 + y1) / 2, Math.Min(x1 - x0, y1 - y0) / 2 - Gap, segments, inner);
                break;
            case ShapeKind.CylinderX:
                AddCylinder(mesh, 0, x0 + Gap, x1 - Gap, (y0 + y1) / 2, (z0 + z1) / 2, Math.Min(y1 - y0, z1 - z0) / 2 - Gap, segments, inner);
                break;
            case ShapeKind.CylinderY:
                AddCylinder(mesh, 1, y0 + Gap, y1 - Gap, (z0 + z1) / 2, (x0 + x1) / 2, Math.Min(x1 - x0, z1 - z0) / 2 - Gap, segments, inner);
                break;
            default:
                AddBox(mesh, x0 + Gap, y0 + Gap, z0 + Gap, x1 - Gap, y1 - Gap, z1 - Gap);
                break;
        }
    }

    private static void AddAccessories(Model3DGroup root, LoadUnit unit, PackagingConstraints c, int visibleLayers, BaseInfo baseInfo)
    {
        var m = unit.Metrics;
        double x0 = m.MinX * Scale, y0 = m.MinY * Scale, x1 = m.MaxX * Scale, y1 = m.MaxY * Scale;
        if (c.SlipSheetThickness > 0)
        {
            var sheets = new MeshGeometry3D();
            foreach (var layer in unit.Layers.Where(l => l.SlipSheetBelow && l.Index <= visibleLayers))
            {
                var z = layer.Z * Scale;
                AddBox(sheets, x0, y0, z - c.SlipSheetThickness * Scale, x1, y1, z);
            }

            if (sheets.Positions.Count > 0)
            {
                root.Children.Add(Model(sheets, Solid(Color.FromRgb(0xE5, 0xD3, 0xA8))));
            }
        }

        var allVisible = visibleLayers >= unit.Layers.Count;
        if (c.CapHeight > 0 && allVisible)
        {
            var cap = new MeshGeometry3D();
            var z = m.LoadHeight * Scale;
            AddBox(cap, x0 - 0.003, y0 - 0.003, z, x1 + 0.003, y1 + 0.003, z + c.CapHeight * Scale);
            root.Children.Add(Model(cap, Solid(Color.FromRgb(0xB9, 0x92, 0x5E))));
        }

        if (c.Corners)
        {
            var (fx0, fy0, fx1, fy1) = unit.CornerBounds(m);
            var (k0, j0, k1, j1) = (fx0 * Scale, fy0 * Scale, fx1 * Scale, fy1 * Scale);
            var t = Math.Max(1, c.CornerThickness) * Scale;
            var leg = Math.Max(10, c.CornerLeg) * Scale;
            var h = (c.CornerHeight > 0 ? c.CornerHeight : m.LoadHeight) * Scale;
            var corners = new MeshGeometry3D();
            foreach (var (cx, cy, sx, sy) in new[] { (k0, j0, 1, 1), (k1, j0, -1, 1), (k0, j1, 1, -1), (k1, j1, -1, -1) })
            {
                // Aile le long de X et aile le long de Y, posées contre la charge, à l'extérieur.
                AddBox(corners, Math.Min(cx - sx * t, cx + sx * leg), Math.Min(cy - sy * t, cy), 0,
                    Math.Max(cx - sx * t, cx + sx * leg), Math.Max(cy - sy * t, cy), h);
                AddBox(corners, Math.Min(cx - sx * t, cx), Math.Min(cy - sy * t, cy + sy * leg), 0,
                    Math.Max(cx - sx * t, cx), Math.Max(cy - sy * t, cy + sy * leg), h);
            }

            root.Children.Add(Model(corners, Solid(Color.FromRgb(0x8D, 0x6E, 0x63))));
        }

        var wrap = ((c.Corners ? c.CornerThickness : 0) + c.FilmThickness) * Scale;
        var top = (m.LoadHeight + c.CapHeight) * Scale;
        var palletH = baseInfo.PalletHeight * Scale;
        if (c.Straps > 0 && allVisible)
        {
            // Cerclages dans le sens de la longueur : dessus, extrémités, passage sous le plateau de la palette.
            var straps = new MeshGeometry3D();
            const double sw = 0.008, st = 0.0025;
            var under = -palletH * 0.2;
            var xa = x0 - wrap - st;
            var xb = x1 + wrap + st;
            for (var i = 0; i < c.Straps; i++)
            {
                var y = y0 + (i + 0.5) * (y1 - y0) / c.Straps;
                AddBox(straps, xa, y - sw, top, xb, y + sw, top + st);
                AddBox(straps, xa - st, y - sw, under, xa, y + sw, top + st);
                AddBox(straps, xb, y - sw, under, xb + st, y + sw, top + st);
                AddBox(straps, xa, y - sw, under - st, xb, y + sw, under);
            }

            root.Children.Add(Model(straps, Solid(Color.FromRgb(0x1F, 0x61, 0x8D))));
        }

        if (c.FilmThickness > 0 && allVisible)
        {
            // Film étirable : enveloppe translucide de la charge et du haut de la palette (ajoutée en dernier).
            var film = new MeshGeometry3D();
            var f = Math.Max(0.001, c.FilmThickness * Scale);
            var zb = -palletH * 0.35;
            var fx0 = x0 - wrap;
            var fx1 = x1 + wrap;
            var fy0 = y0 - wrap;
            var fy1 = y1 + wrap;
            AddBox(film, fx0, fy0, zb, fx1, fy0 + f, top);
            AddBox(film, fx0, fy1 - f, zb, fx1, fy1, top);
            AddBox(film, fx0, fy0, zb, fx0 + f, fy1, top);
            AddBox(film, fx1 - f, fy0, zb, fx1, fy1, top);
            film.Freeze();
            var material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0x55, 0xA9, 0xDF, 0xD8)));
            material.Freeze();
            root.Children.Add(new GeometryModel3D(film, material) { BackMaterial = material });
        }
    }

    /// <summary>Aperçu d'une caisse du catalogue, fermée (espace Caisses).</summary>
    public static Model3DGroup BuildCasePreview(CaseType type)
    {
        var root = new Model3DGroup();
        if (type.InnerLength > 0 && type.InnerWidth > 0 && type.InnerHeight > 0)
        {
            AddCase(root, type.InnerLength, type.InnerWidth, type.InnerHeight, new CaseRender(type.WallThickness, type.Color, Open: false), translucent: []);
            AddShadow(root);
        }

        return root;
    }

    /// <summary>
    /// Caisse américaine (FEFCO 0201) autour de l'intérieur utile [0, L] × [0, W] × [0, H] : fond, 4 parois d'épaisseur
    /// <c>Wall</c>. Fermée : couvercle (rabats) et bande adhésive. Ouverte : rabats relevés vers l'extérieur ; les parois
    /// listées dans <paramref name="translucent"/> (« x0 », « y0 » : côté caméra) sont translucides pour voir le contenu.
    /// </summary>
    private static void AddCase(Model3DGroup root, double length, double width, double height, CaseRender render, IReadOnlyCollection<string> translucent)
    {
        double l = length * Scale, w = width * Scale, h = height * Scale;
        var t = Math.Max(0.002, render.Wall * Scale);
        var color = ArticleColors.Parse(render.Color, Color.FromRgb(0xC9, 0xA2, 0x6B));
        var solid = Solid(color);
        var inner = Solid(Shade(color, 0.82));
        var glass = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0x48, color.R, color.G, color.B)));
        glass.Freeze();

        GeometryModel3D Slab(double x0, double y0, double z0, double x1, double y1, double z1, Material front, Material? back = null)
        {
            var mesh = new MeshGeometry3D();
            AddBox(mesh, x0, y0, z0, x1, y1, z1);
            mesh.Freeze();
            return new GeometryModel3D(mesh, front) { BackMaterial = back ?? front };
        }

        var opaque = new List<GeometryModel3D> { Slab(-t, -t, -t, l + t, w + t, 0, solid, inner) };
        var clear = new List<GeometryModel3D>();
        void Wall(string key, GeometryModel3D model)
        {
            if (translucent.Contains(key))
            {
                model.Material = glass;
                model.BackMaterial = glass;
                clear.Add(model);
            }
            else
            {
                opaque.Add(model);
            }
        }

        Wall("x0", Slab(-t, -t, 0, 0, w + t, h, solid, inner));
        Wall("x1", Slab(l, -t, 0, l + t, w + t, h, solid, inner));
        Wall("y0", Slab(0, -t, 0, l, 0, h, solid, inner));
        Wall("y1", Slab(0, w, 0, l, w + t, h, solid, inner));

        if (!render.Open)
        {
            // Couvercle : deux grands rabats jointifs au milieu, bande adhésive dans la longueur.
            opaque.Add(Slab(-t, -t, h, l + t, w / 2 - 0.0005, h + t, solid, inner));
            opaque.Add(Slab(-t, w / 2 + 0.0005, h, l + t, w + t, h + t, solid, inner));
            var tape = Solid(Color.FromRgb(0xE8, 0xD5, 0xA8));
            opaque.Add(Slab(-t - 0.001, w / 2 - 0.025, h + t, l + t + 0.001, w / 2 + 0.025, h + t + 0.0008, tape));
            opaque.Add(Slab(-t - 0.0008, w / 2 - 0.025, h - 0.06, -t, w / 2 + 0.025, h + t, tape));
            opaque.Add(Slab(l + t, w / 2 - 0.025, h - 0.06, l + t + 0.0008, w / 2 + 0.025, h + t, tape));
        }
        else
        {
            // Rabats ouverts : hauteur W/2 (caisse américaine), inclinés de 25° vers l'extérieur.
            var flap = Math.Min(w / 2, Math.Max(h * 0.8, 0.05));
            GeometryModel3D Flap(string key, double x0, double y0, double x1, double y1, Vector3D axis, double angle, Point3D hinge)
            {
                var model = Slab(x0, y0, h, x1, y1, h + flap, solid, inner);
                model.Transform = new RotateTransform3D(new AxisAngleRotation3D(axis, angle), hinge);
                Wall(key, model);
                return model;
            }

            Flap("y1", 0, w, l, w + t, new Vector3D(1, 0, 0), -25, new Point3D(0, w, h));
            Flap("y0", 0, -t, l, 0, new Vector3D(1, 0, 0), 25, new Point3D(0, 0, h));
            Flap("x1", l, -t, l + t, w + t, new Vector3D(0, 1, 0), 25, new Point3D(l, 0, h));
            Flap("x0", -t, -t, 0, w + t, new Vector3D(0, 1, 0), -25, new Point3D(0, 0, h));
        }

        foreach (var model in opaque.Concat(clear))
        {
            root.Children.Add(model);
        }
    }

    // ------------------------------------------------------------------ Palettes

    public static void AddBase(Model3DGroup root, BaseInfo b)
    {
        for (var i = 0; i < b.CountAlongLength; i++)
        {
            for (var j = 0; j < b.CountAlongWidth; j++)
            {
                AddPallet(root, b.Construction, b.Color, i * b.PalletLength, j * b.PalletWidth, b.PalletLength, b.PalletWidth, b.PalletHeight, b.Rotated);
            }
        }
    }

    public static SceneResult BuildPallet(PalletType p)
    {
        var scene = new SceneResult { SizeX = p.Length * Scale, SizeY = p.Width * Scale, SizeZ = p.Height * Scale };
        AddPallet(scene.Root, p.Construction, p.Color, 0, 0, p.Length, p.Width, p.Height, false);
        AddShadow(scene.Root);
        return scene;
    }

    /// <summary>
    /// Une palette dans son repère propre (u selon sa longueur, v selon sa largeur), tournée de 90° si elle est
    /// posée « largeur dans la longueur » de la base. Toutes les cotes sont proportionnelles à la palette EUR.
    /// </summary>
    private static void AddPallet(Model3DGroup root, PalletConstruction construction, string colorHex, double ox, double oy,
        double sizeX, double sizeY, double height, bool rotated)
    {
        var baseColor = ArticleColors.Parse(colorHex, Color.FromRgb(0xC8, 0xA1, 0x65));
        var light = new MeshGeometry3D();
        var dark = new MeshGeometry3D();
        var lu = rotated ? sizeY : sizeX;
        var lv = rotated ? sizeX : sizeY;
        var h = height;
        const double g = 1;

        void Part(MeshGeometry3D mesh, double u0, double v0, double z0, double u1, double v1, double z1)
        {
            // Repère local → monde (mm), puis mètres.
            double x0, y0, x1, y1;
            if (rotated)
            {
                (x0, x1) = (ox + v0, ox + v1);
                (y0, y1) = (oy + u0, oy + u1);
            }
            else
            {
                (x0, x1) = (ox + u0, ox + u1);
                (y0, y1) = (oy + v0, oy + v1);
            }

            AddBox(mesh, (x0 + g) * Scale, (y0 + g) * Scale, z0 * Scale, (x1 - g) * Scale, (y1 - g) * Scale, z1 * Scale);
        }

        var t = h * 22 / 144;
        double[] Spread(double length, double width, int count) =>
            Enumerable.Range(0, count).Select(k => count == 1 ? (length - width) / 2 : k * (length - width) / (count - 1)).ToArray();

        var blockU = lu * 145 / 1200;
        var blockV = lv * 125 / 800;
        var us = Spread(lu, blockU, 3);
        var vs = Spread(lv, blockV, 3);

        switch (construction)
        {
            case PalletConstruction.Blocs9Semelles3:
            case PalletConstruction.Blocs9:
            {
                var withSkids = construction == PalletConstruction.Blocs9Semelles3;
                var bottom = withSkids ? t : 0;
                var block = h - bottom - 2 * t;
                var boards = lv > 1000 ? 7 : 5;
                var bw = lv * 0.14;
                foreach (var v in Spread(lv, bw, boards))
                {
                    Part(light, 0, v, -t, lu, v + bw, 0);
                }

                foreach (var u in us)
                {
                    Part(light, u, 0, -2 * t, u + blockU, lv, -t);
                    foreach (var v in vs)
                    {
                        Part(dark, u, v, -h + bottom, u + blockU, v + blockV, -2 * t);
                    }
                }

                if (withSkids)
                {
                    foreach (var v in vs)
                    {
                        Part(light, 0, v, -h, lu, v + blockV, -h + t);
                    }
                }

                break;
            }

            case PalletConstruction.Patins3:
            {
                var deck = h * 0.22;
                var runner = h * 0.16;
                Part(light, 0, 0, -deck, lu, lv, 0);
                foreach (var u in us)
                {
                    foreach (var v in vs)
                    {
                        Part(dark, u, v, -h + runner, u + blockU, v + blockV, -deck);
                    }
                }

                foreach (var v in vs)
                {
                    Part(light, 0, v, -h, lu, v + blockV, -h + runner);
                }

                break;
            }

            case PalletConstruction.Pieds9:
            {
                var deck = h * 0.22;
                Part(light, 0, 0, -deck, lu, lv, 0);
                foreach (var u in us)
                {
                    foreach (var v in vs)
                    {
                        Part(dark, u + blockU * 0.1, v + blockV * 0.1, -h, u + blockU * 0.9, v + blockV * 0.9, -deck);
                    }
                }

                break;
            }

            default:
                Part(light, 0, 0, -h, lu, lv, 0);
                break;
        }

        root.Children.Add(Model(light, Solid(baseColor)));
        if (dark.Positions.Count > 0)
        {
            root.Children.Add(Model(dark, Solid(Shade(baseColor, 0.8))));
        }
    }

    // ------------------------------------------------------------------ Article seul (aperçu)

    public static SceneResult BuildArticle(Article a, Color color)
    {
        a = a.WithEffectiveDiameter(); // tube / bobine sans diamètre extérieur : diamètre intérieur
        var scene = new SceneResult();
        var p = a.Kind switch
        {
            ArticleKind.Tube when a.CoilAxis == CoilAxis.Vertical => new Placement { DX = a.Diameter, DY = a.Diameter, DZ = a.Length, Shape = ShapeKind.CylinderZ },
            ArticleKind.Tube => new Placement { DX = a.Length, DY = a.Diameter, DZ = a.Diameter, Shape = ShapeKind.CylinderX },
            ArticleKind.Bobine when a.CoilAxis == CoilAxis.Horizontal => new Placement { DX = a.Width, DY = a.Diameter, DZ = a.Diameter, Shape = ShapeKind.CylinderX },
            ArticleKind.Bobine => new Placement { DX = a.Diameter, DY = a.Diameter, DZ = a.Width, Shape = ShapeKind.CylinderZ },
            ArticleKind.Fut or ArticleKind.Seau or ArticleKind.Bouteille => new Placement { DX = a.Diameter, DY = a.Diameter, DZ = a.Height, Shape = ShapeKind.CylinderZ },
            _ => new Placement { DX = a.Length, DY = a.Width, DZ = a.Height }
        };
        if (p.DX <= 0 || p.DY <= 0 || p.DZ <= 0)
        {
            return scene;
        }

        if (a.Kind == ArticleKind.Tube)
        {
            p.InnerDiameter = a.HollowDiameter; // tube creux : le creux est visible
        }

        var mesh = new MeshGeometry3D();
        AddItem(mesh, p, 32, a.Kind);
        scene.Root.Children.Add(Model(mesh, Solid(color)));
        if (a.Kind == ArticleKind.Bobine && a.InnerDiameter > 0 && a.InnerDiameter < a.Diameter)
        {
            var core = new MeshGeometry3D();
            var c = new Placement
            {
                X = p.Shape == ShapeKind.CylinderZ ? (a.Diameter - a.InnerDiameter) / 2 : -2,
                Y = (a.Diameter - a.InnerDiameter) / 2,
                Z = p.Shape == ShapeKind.CylinderZ ? -2 : (a.Diameter - a.InnerDiameter) / 2,
                DX = p.Shape == ShapeKind.CylinderZ ? a.InnerDiameter : p.DX + 4,
                DY = a.InnerDiameter,
                DZ = p.Shape == ShapeKind.CylinderZ ? p.DZ + 4 : a.InnerDiameter,
                Shape = p.Shape
            };
            AddItem(core, c, 32);
            scene.Root.Children.Add(Model(core, Solid(Color.FromRgb(0x2C, 0x3E, 0x50))));
        }

        AddShadow(scene.Root);
        scene.SizeX = p.DX * Scale;
        scene.SizeY = p.DY * Scale;
        scene.SizeZ = p.DZ * Scale;
        return scene;
    }

    // ------------------------------------------------------------------ Autres types (formes reconnaissables)

    /// <summary>
    /// Bidon / jerrican : corps, poignée en arceau et bouchon sur le dessus, alignés sur le grand côté. Les proportions
    /// suivent l'enveloppe saisie (hauteur hors tout).
    /// </summary>
    private static void AddJerrican(MeshGeometry3D mesh, double x0, double y0, double z0, double x1, double y1, double z1, int segments)
    {
        var alongX = x1 - x0 >= y1 - y0;
        var lu = alongX ? x1 - x0 : y1 - y0;
        var lv = alongX ? y1 - y0 : x1 - x0;
        var h = z1 - z0;
        var body = z0 + 0.80 * h;

        // Repère local : u le long du grand côté, v le long du petit.
        void Box(double u0, double u1, double v0, double v1, double za, double zb)
        {
            if (alongX)
            {
                AddBox(mesh, x0 + u0 * lu, y0 + v0 * lv, za, x0 + u1 * lu, y0 + v1 * lv, zb);
            }
            else
            {
                AddBox(mesh, x0 + v0 * lv, y0 + u0 * lu, za, x0 + v1 * lv, y0 + u1 * lu, zb);
            }
        }

        Box(0, 1, 0, 1, z0, body);
        Box(0.04, 0.70, 0.08, 0.92, body, body + 0.04 * h); // épaulement
        Box(0.12, 0.20, 0.42, 0.58, body, z1 - 0.05 * h); // montants de la poignée
        Box(0.50, 0.58, 0.42, 0.58, body, z1 - 0.05 * h);
        Box(0.12, 0.58, 0.42, 0.58, z1 - 0.08 * h, z1); // poignée
        var (cu, cv) = (0.82 * lu, 0.5 * lv); // bouchon
        var r = Math.Min(0.16 * lv, 0.09 * lu);
        AddCylinder(mesh, 2, body, z1 - 0.03 * h, alongX ? x0 + cu : x0 + cv, alongX ? y0 + cv : y0 + cu, r, Math.Max(8, segments / 2));
    }

    /// <summary>Bouteille / flacon : corps, épaule, col et bouchon.</summary>
    private static void AddBottle(MeshGeometry3D mesh, double cx, double cy, double r, double z0, double z1, int segments)
    {
        var h = z1 - z0;
        AddCylinder(mesh, 2, z0, z0 + 0.62 * h, cx, cy, r, segments);
        AddCylinder(mesh, 2, z0 + 0.62 * h, z0 + 0.68 * h, cx, cy, r * 0.82, segments);
        AddCylinder(mesh, 2, z0 + 0.68 * h, z0 + 0.74 * h, cx, cy, r * 0.58, segments);
        AddCylinder(mesh, 2, z0 + 0.74 * h, z0 + 0.93 * h, cx, cy, r * 0.32, segments);
        AddCylinder(mesh, 2, z0 + 0.92 * h, z1, cx, cy, r * 0.38, segments);
    }

    /// <summary>Seau / pot : corps légèrement évasé, rebord et couvercle.</summary>
    private static void AddPail(MeshGeometry3D mesh, double cx, double cy, double r, double z0, double z1, int segments)
    {
        var h = z1 - z0;
        AddCylinder(mesh, 2, z0, z0 + 0.45 * h, cx, cy, r * 0.86, segments);
        AddCylinder(mesh, 2, z0 + 0.45 * h, z0 + 0.90 * h, cx, cy, r * 0.93, segments);
        AddCylinder(mesh, 2, z0 + 0.90 * h, z1, cx, cy, r, segments);
    }

    /// <summary>Cuve IBC / GRV : palette intégrée, cuve en retrait, cage (montants et ceintures) et bouchon.</summary>
    private static void AddIbc(MeshGeometry3D mesh, double x0, double y0, double z0, double x1, double y1, double z1, int segments)
    {
        var h = z1 - z0;
        var t = Math.Min(x1 - x0, y1 - y0) * 0.025; // section des tubes de la cage
        var baseTop = z0 + 0.12 * h;
        var cageTop = z0 + 0.95 * h;
        var ix = (x1 - x0) * 0.04;
        var iy = (y1 - y0) * 0.04;
        AddBox(mesh, x0, y0, z0, x1, y1, baseTop); // palette intégrée
        AddBox(mesh, x0 + ix, y0 + iy, baseTop, x1 - ix, y1 - iy, cageTop - t); // cuve
        foreach (var (cx, cy) in new[] { (x0, y0), (x1 - t, y0), (x0, y1 - t), (x1 - t, y1 - t) })
        {
            AddBox(mesh, cx, cy, baseTop, cx + t, cy + t, cageTop); // montants d'angle
        }

        foreach (var z in new[] { baseTop + (cageTop - baseTop) * 0.5, cageTop - t })
        {
            AddBox(mesh, x0, y0, z, x1, y0 + t, z + t); // ceintures de la cage
            AddBox(mesh, x0, y1 - t, z, x1, y1, z + t);
            AddBox(mesh, x0, y0, z, x0 + t, y1, z + t);
            AddBox(mesh, x1 - t, y0, z, x1, y1, z + t);
        }

        AddCylinder(mesh, 2, cageTop - t, z1, (x0 + x1) / 2, (y0 + y1) / 2, Math.Min(x1 - x0, y1 - y0) * 0.12, Math.Max(8, segments / 2)); // bouchon
    }

    // ------------------------------------------------------------------ Ombre au sol

    /// <summary>Ombre douce : dégradé radial sombre et translucide, plus foncé au centre, fondu vers les bords.</summary>
    private static readonly Material ShadowMaterial = Freeze(new DiffuseMaterial(new RadialGradientBrush
    {
        GradientStops =
        [
            new GradientStop(Color.FromArgb(0x46, 0x10, 0x1A, 0x24), 0),
            new GradientStop(Color.FromArgb(0x34, 0x10, 0x1A, 0x24), 0.45),
            new GradientStop(Color.FromArgb(0x12, 0x10, 0x1A, 0x24), 0.75),
            new GradientStop(Color.FromArgb(0x00, 0x10, 0x1A, 0x24), 1)
        ]
    }));

    /// <summary>
    /// Ombre portée sous la scène (ajoutée en dernier : objet translucide), juste sous son point le plus bas. L'ellipse
    /// déborde de l'emprise pour en couvrir les coins ; invisible vue de dessous.
    /// </summary>
    public static bool IsShadow(Model3D model) => model is GeometryModel3D g && ReferenceEquals(g.Material, ShadowMaterial);

    /// <summary>Emprise de la scène sans l'ombre au sol (cadrage des caméras).</summary>
    public static Rect3D BoundsOf(Model3D model)
    {
        if (model is not Model3DGroup group)
        {
            return model.Bounds;
        }

        var bounds = Rect3D.Empty;
        foreach (var child in group.Children)
        {
            if (!IsShadow(child))
            {
                bounds.Union(child.Bounds);
            }
        }

        return bounds;
    }

    private static void AddShadow(Model3DGroup root)
    {
        var b = root.Bounds;
        if (b.IsEmpty || b.SizeX <= 0 || b.SizeY <= 0)
        {
            return;
        }

        double cx = b.X + b.SizeX / 2, cy = b.Y + b.SizeY / 2, z = b.Z - 0.001;
        var spread = Math.Max(b.SizeX, b.SizeY) * 0.08;
        double hx = b.SizeX * 0.75 + spread, hy = b.SizeY * 0.75 + spread;
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(new Point3D(cx - hx, cy - hy, z));
        mesh.Positions.Add(new Point3D(cx + hx, cy - hy, z));
        mesh.Positions.Add(new Point3D(cx + hx, cy + hy, z));
        mesh.Positions.Add(new Point3D(cx - hx, cy + hy, z));
        for (var i = 0; i < 4; i++)
        {
            mesh.Normals.Add(new Vector3D(0, 0, 1));
        }

        mesh.TextureCoordinates.Add(new System.Windows.Point(0, 0));
        mesh.TextureCoordinates.Add(new System.Windows.Point(1, 0));
        mesh.TextureCoordinates.Add(new System.Windows.Point(1, 1));
        mesh.TextureCoordinates.Add(new System.Windows.Point(0, 1));
        foreach (var i in new[] { 0, 1, 2, 0, 2, 3 })
        {
            mesh.TriangleIndices.Add(i);
        }

        mesh.Freeze();
        root.Children.Add(new GeometryModel3D(mesh, ShadowMaterial));
    }

    // ------------------------------------------------------------------ Géométrie

    private static GeometryModel3D Model(MeshGeometry3D mesh, Material material)
    {
        mesh.Freeze();
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static T Freeze<T>(T freezable) where T : System.Windows.Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    /// <summary>Pavé à faces plates (4 sommets par face pour un ombrage net).</summary>
    public static void AddBox(MeshGeometry3D mesh, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        AddQuad(mesh, new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0), new(x1, y0, z0));
        AddQuad(mesh, new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1));
        AddQuad(mesh, new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, y0, z1));
        AddQuad(mesh, new(x1, y1, z0), new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1));
        AddQuad(mesh, new(x0, y1, z0), new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1));
        AddQuad(mesh, new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1));
    }

    private static void AddQuad(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c, Point3D d)
    {
        var i = mesh.Positions.Count;
        var n = Vector3D.CrossProduct(b - a, d - a);
        n.Normalize();
        foreach (var p in new[] { a, b, c, d })
        {
            mesh.Positions.Add(p);
            mesh.Normals.Add(n);
        }

        mesh.TriangleIndices.Add(i);
        mesh.TriangleIndices.Add(i + 1);
        mesh.TriangleIndices.Add(i + 2);
        mesh.TriangleIndices.Add(i);
        mesh.TriangleIndices.Add(i + 2);
        mesh.TriangleIndices.Add(i + 3);
    }

    /// <summary>Cylindre d'axe <paramref name="axis"/> (0 = X, 1 = Y, 2 = Z) ; (cu, cv) = centre dans les deux axes suivants.</summary>
    /// <param name="innerRadius">Tube creux : rayon de l'alésage (paroi intérieure et extrémités en couronne) ; 0 = plein.</param>
    private static void AddCylinder(MeshGeometry3D mesh, int axis, double a0, double a1, double cu, double cv, double radius, int segments,
        double innerRadius = 0)
    {
        var hollow = innerRadius > 0 && innerRadius < radius * 0.98;
        var u = (axis + 1) % 3;
        var v = (axis + 2) % 3;

        Point3D P(double a, double uu, double vv)
        {
            var c = new double[3];
            c[axis] = a;
            c[u] = uu;
            c[v] = vv;
            return new Point3D(c[0], c[1], c[2]);
        }

        Vector3D N(double nu, double nv)
        {
            var c = new double[3];
            c[u] = nu;
            c[v] = nv;
            return new Vector3D(c[0], c[1], c[2]);
        }

        var axisDir = new double[3];
        axisDir[axis] = 1;
        var axisVec = new Vector3D(axisDir[0], axisDir[1], axisDir[2]);
        for (var i = 0; i < segments; i++)
        {
            var t0 = 2 * Math.PI * i / segments;
            var t1 = 2 * Math.PI * (i + 1) / segments;
            var (c0, s0, c1, s1) = (Math.Cos(t0), Math.Sin(t0), Math.Cos(t1), Math.Sin(t1));
            var k = mesh.Positions.Count;
            mesh.Positions.Add(P(a0, cu + radius * c0, cv + radius * s0));
            mesh.Positions.Add(P(a0, cu + radius * c1, cv + radius * s1));
            mesh.Positions.Add(P(a1, cu + radius * c1, cv + radius * s1));
            mesh.Positions.Add(P(a1, cu + radius * c0, cv + radius * s0));
            mesh.Normals.Add(N(c0, s0));
            mesh.Normals.Add(N(c1, s1));
            mesh.Normals.Add(N(c1, s1));
            mesh.Normals.Add(N(c0, s0));
            foreach (var idx in new[] { 0, 1, 2, 0, 2, 3 })
            {
                mesh.TriangleIndices.Add(k + idx);
            }

            if (hollow)
            {
                // Paroi intérieure (normales vers l'axe) et extrémités en couronne.
                var r = innerRadius;
                var m = mesh.Positions.Count;
                mesh.Positions.Add(P(a0, cu + r * c0, cv + r * s0));
                mesh.Positions.Add(P(a0, cu + r * c1, cv + r * s1));
                mesh.Positions.Add(P(a1, cu + r * c1, cv + r * s1));
                mesh.Positions.Add(P(a1, cu + r * c0, cv + r * s0));
                mesh.Normals.Add(N(-c0, -s0));
                mesh.Normals.Add(N(-c1, -s1));
                mesh.Normals.Add(N(-c1, -s1));
                mesh.Normals.Add(N(-c0, -s0));
                foreach (var idx in new[] { 0, 2, 1, 0, 3, 2 })
                {
                    mesh.TriangleIndices.Add(m + idx);
                }

                foreach (var (a, normal) in new[] { (a0, -axisVec), (a1, axisVec) })
                {
                    var j = mesh.Positions.Count;
                    mesh.Positions.Add(P(a, cu + radius * c0, cv + radius * s0));
                    mesh.Positions.Add(P(a, cu + radius * c1, cv + radius * s1));
                    mesh.Positions.Add(P(a, cu + r * c1, cv + r * s1));
                    mesh.Positions.Add(P(a, cu + r * c0, cv + r * s0));
                    for (var n = 0; n < 4; n++)
                    {
                        mesh.Normals.Add(normal);
                    }

                    foreach (var idx in new[] { 0, 1, 2, 0, 2, 3 })
                    {
                        mesh.TriangleIndices.Add(j + idx);
                    }
                }

                continue;
            }

            foreach (var (a, normal) in new[] { (a0, -axisVec), (a1, axisVec) })
            {
                var j = mesh.Positions.Count;
                mesh.Positions.Add(P(a, cu, cv));
                mesh.Positions.Add(P(a, cu + radius * c0, cv + radius * s0));
                mesh.Positions.Add(P(a, cu + radius * c1, cv + radius * s1));
                for (var n = 0; n < 3; n++)
                {
                    mesh.Normals.Add(normal);
                }

                mesh.TriangleIndices.Add(j);
                mesh.TriangleIndices.Add(j + 1);
                mesh.TriangleIndices.Add(j + 2);
            }
        }
    }
}
