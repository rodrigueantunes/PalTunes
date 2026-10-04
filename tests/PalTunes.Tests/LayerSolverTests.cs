using PalTunes.Core.Engine;

namespace PalTunes.Tests;

/// <summary>Propriétés du générateur de plans de couche sur des centaines d'instances aléatoires.</summary>
public class LayerSolverTests
{
    [Fact]
    public void RandomInstances_AreValid_AndBelowBound()
    {
        var rng = new Random(42);
        for (var n = 0; n < 400; n++)
        {
            double X = rng.Next(600, 1400), Y = rng.Next(400, 1200);
            double a = rng.Next(80, 500), b = rng.Next(60, 400);
            var r = RectLayerSolver.Solve(X, Y, a, b);
            var items = r.Best.Items;
            Assert.True(items.Count <= r.UpperBound || r.UpperBound == 0, $"{X}x{Y} / {a}x{b}");
            Assert.True(items.Count >= r.Grid.Count);
            foreach (var it in items)
            {
                Assert.True(it.X >= -1e-6 && it.Y >= -1e-6 && it.Right <= X + 1e-6 && it.Top <= Y + 1e-6);
                Assert.True((Math.Abs(it.W - a) < 1e-6 && Math.Abs(it.H - b) < 1e-6) || (Math.Abs(it.W - b) < 1e-6 && Math.Abs(it.H - a) < 1e-6));
            }

            for (var i = 0; i < items.Count; i++)
            {
                for (var j = i + 1; j < items.Count; j++)
                {
                    var ox = Math.Min(items[i].Right, items[j].Right) - Math.Max(items[i].X, items[j].X);
                    var oy = Math.Min(items[i].Top, items[j].Top) - Math.Max(items[i].Y, items[j].Y);
                    Assert.False(ox > 1e-6 && oy > 1e-6, $"Chevauchement {X}x{Y} / {a}x{b}");
                }
            }
        }
    }

    [Fact]
    public void Gap_IsRespectedBetweenItems()
    {
        var r = RectLayerSolver.Solve(1200, 800, 400, 300, gap: 10);
        // 4 × 300 + 3 × 10 = 1230 > 1200 : la grille tournée n'en loge plus que 3 × 2 en long, etc.
        foreach (var i in r.Best.Items)
        {
            foreach (var j in r.Best.Items.Where(j => !j.Equals(i)))
            {
                var gx = Math.Max(i.X, j.X) - Math.Min(i.Right, j.Right);
                var gy = Math.Max(i.Y, j.Y) - Math.Min(i.Top, j.Top);
                Assert.True(gx >= 10 - 1e-6 || gy >= 10 - 1e-6);
            }
        }
    }

    [Fact]
    public void Pinwheel_BeatsGuillotine_OnClassicInstance()
    {
        // Instance où un moulinet (non-guillotine) dépasse la meilleure guillotine.
        var found = false;
        for (var X = 20; X <= 60 && !found; X++)
        {
            for (var Y = 15; Y <= X && !found; Y++)
            {
                var r = RectLayerSolver.Solve(X, Y, 7, 3);
                found = r.PinwheelCount > r.GuillotineCount;
            }
        }

        Assert.True(found);
    }

    [Fact]
    public void Interlock_VariantExists_ForNonSymmetricLayer()
    {
        var r = RectLayerSolver.Solve(1200, 800, 300, 250);
        var (variant, interlock) = HomogeneousEngine.BestInterlock(r.Best, 0.5);
        if (r.Best.Kind != "Grille")
        {
            Assert.NotNull(variant);
            Assert.True(interlock > 0);
        }
    }
}
