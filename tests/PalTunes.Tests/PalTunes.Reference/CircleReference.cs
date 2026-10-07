namespace PalTunes.Reference;

/// <summary>
/// Référence des cercles identiques dans un rectangle : meilleure suite de rangées, chacune alignée (n₀ = ⌊L/d⌋ cercles)
/// ou décalée d'un demi-diamètre (n₁ = ⌊(L − d/2)/d⌋), deux rangées consécutives distantes de d (même décalage) ou de
/// d·√3/2 (décalages différents, cercles imbriqués). Couvre la maille carrée, la quinconce et tous leurs mélanges, dans les
/// deux sens. Indépendante du moteur.
/// </summary>
public static class CircleReference
{
    private static readonly double Pitch = Math.Sqrt(3) / 2;

    public static (int Count, string Pattern) Best(double X, double Y, double d)
    {
        var a = Rows(X, Y, d);
        var b = Rows(Y, X, d);
        return a.Count >= b.Count ? a : b;
    }

    /// <summary>Rangées le long de <paramref name="L"/>, empilées sur <paramref name="W"/>.</summary>
    private static (int Count, string Pattern) Rows(double L, double W, double d)
    {
        if (d > L + 1e-9 || d > W + 1e-9)
        {
            return (0, "aucun");
        }

        var n0 = (int)Math.Floor(L / d + 1e-9);
        var n1 = (int)Math.Floor((L - d / 2) / d + 1e-9);
        var best = (Count: 0, Pattern: "");
        // kd pas « carrés » (distance d), kp pas « imbriqués » (distance d·√3/2) : hauteur d + kd·d + kp·p ≤ W.
        for (var kp = 0; d + kp * d * Pitch <= W + 1e-9; kp++)
        {
            var kd = (int)Math.Floor((W - d - kp * d * Pitch) / d + 1e-9);
            var rows = 1 + kd + kp;
            // Rangées décalées : au moins ⌈kp / 2⌉ (chaque passage en décalé et retour coûte deux pas imbriqués).
            var shifted = (kp + 1) / 2;
            var count = (rows - shifted) * n0 + shifted * n1;
            if (count > best.Count)
            {
                best = (count, kp == 0 ? "carrée" : kd == 0 ? "quinconce" : $"mixte ({kp} pas quinconce, {kd} pas carrés)");
            }
        }

        return best;
    }
}
