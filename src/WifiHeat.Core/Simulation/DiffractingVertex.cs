using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;

namespace WifiHeat.Core.Simulation;

/// <summary>
/// Vértice alrededor del cual puede doblar un trayecto: extremo libre de muro, esquina o vértice convexo de columna.
/// El sector libre empieza en la dirección SectorStartRad y abarca SectorRad en sentido antihorario.
/// LossDb: la mayor pérdida de penetración de los obstáculos que llegan al vértice.
/// </summary>
public sealed record DiffractingVertex(Vec2 Position, double SectorStartRad, double SectorRad, double LossDb)
{
    internal const double AngleEpsilonRad = 1e-6;
    internal const double TwoPi = 2 * Math.PI;

    /// <summary>Transmisión en potencia del obstáculo: T = 10^(−L/10).</summary>
    public double Transmission { get; } = Math.Pow(10, -LossDb / 10);

    public double FreeSectorDeg => SectorRad * 180 / Math.PI;

    /// <summary>
    /// Ángulo de la dirección vértice → p dentro del sector libre, medido desde su inicio; null si cae fuera.
    /// En esquinas y columnas la dirección rasante a una cara vale (es el borde del sector).
    /// En un extremo libre (sector de 360°) la dirección del propio muro no vale.
    /// </summary>
    internal double? SectorOffset(Vec2 p)
    {
        double offset = Mod(Math.Atan2(p.Y - Position.Y, p.X - Position.X) - SectorStartRad, TwoPi);
        if (SectorRad >= TwoPi - 1e-9)
        {
            return offset < AngleEpsilonRad || offset > TwoPi - AngleEpsilonRad ? null : offset;
        }

        if (offset >= TwoPi - AngleEpsilonRad) offset = 0.0;
        if (offset > SectorRad + AngleEpsilonRad) return null;
        return Math.Min(offset, SectorRad);
    }

    /// <summary>La dirección vértice → p cae estrictamente dentro del sector libre.</summary>
    internal bool SeesStrictly(Vec2 p)
    {
        double offset = Mod(Math.Atan2(p.Y - Position.Y, p.X - Position.X) - SectorStartRad, TwoPi);
        return offset > AngleEpsilonRad && offset < SectorRad - AngleEpsilonRad;
    }

    /// <summary>
    /// Ángulo de difracción θ > 0 si el trayecto previous → vértice → next rodea el obstáculo; null si no.
    /// Rodea cuando las dos direcciones están en el sector libre y el ángulo entre ellas, medido por el sector, supera 180°.
    /// θ es el exceso sobre 180°. Si la recta previous–next pasa a menos de <see cref="LegLoss.GrazeM"/> del vértice,
    /// el trayecto es rasante y no lo rodea.
    /// </summary>
    internal double? WrapAngle(Vec2 previous, Vec2 next)
    {
        if (SectorOffset(previous) is not { } a || SectorOffset(next) is not { } b) return null;

        double theta = Math.Abs(a - b) - Math.PI;
        if (theta <= 0) return null;

        Vec2 chord = next - previous;
        double length = chord.Length;
        if (length > 0 && Math.Abs(Vec2.Cross(chord, Position - previous)) / length <= LegLoss.GrazeM) return null;
        return theta;
    }

    /// <summary>Resto con el signo del divisor: el resultado queda en [0, m) para m positivo.</summary>
    internal static double Mod(double a, double m)
    {
        double r = a % m;
        if (r != 0 && (r < 0) != (m < 0)) r += m;
        return r;
    }
}

public static class DiffractingVertices
{
    /// <summary>Sector libre mínimo para que un nodo sea vértice difractante: 181°.</summary>
    public const double MinSectorRad = Math.PI + Math.PI / 180;

    /// <summary>
    /// Nodos = extremos de todos los segmentos, unidos con la tolerancia de <see cref="PathCrossings.JunctionToleranceM"/>.
    /// En cada nodo se juntan las direcciones de los segmentos que salen de él; si cae sobre el interior de otro segmento
    /// (unión en T) se agregan las dos direcciones de ese segmento. El nodo es vértice difractante si el mayor hueco angular
    /// entre direcciones consecutivas supera <see cref="MinSectorRad"/>. Se descartan los nodos que caen dentro de una columna.
    /// </summary>
    public static IReadOnlyList<DiffractingVertex> Find(SimulationScene scene, Band band)
    {
        const double tolerance = PathCrossings.JunctionToleranceM;

        var nodes = new List<Vec2>();
        foreach (ResolvedWall rw in scene.Walls)
        {
            foreach (Segment2 s in rw.Wall.Segments)
            {
                AddNode(nodes, s.A, tolerance);
                AddNode(nodes, s.B, tolerance);
            }
        }

        var vertices = new List<DiffractingVertex>();
        var directions = new List<double>();
        var unique = new List<double>();
        foreach (Vec2 node in nodes)
        {
            if (InsideAColumn(scene, node)) continue;

            directions.Clear();
            double loss = 0.0;
            foreach (ResolvedWall rw in scene.Walls)
            {
                double wallLoss = rw.LossDb(band);
                foreach (Segment2 s in rw.Wall.Segments)
                {
                    bool atA = Vec2.Distance(s.A, node) <= tolerance;
                    bool atB = Vec2.Distance(s.B, node) <= tolerance;
                    if (atA) directions.Add(Math.Atan2(s.B.Y - node.Y, s.B.X - node.X));
                    if (atB) directions.Add(Math.Atan2(s.A.Y - node.Y, s.A.X - node.X));
                    if (atA || atB)
                    {
                        loss = Math.Max(loss, wallLoss);
                        continue;
                    }

                    Vec2 ab = s.B - s.A;
                    double t = Vec2.Dot(node - s.A, ab) / Vec2.Dot(ab, ab);
                    if (t > 0 && t < 1 && Vec2.Distance(node, s.A + ab * t) <= tolerance)
                    {
                        directions.Add(Math.Atan2(ab.Y, ab.X));
                        directions.Add(Math.Atan2(-ab.Y, -ab.X));
                        loss = Math.Max(loss, wallLoss);
                    }
                }
            }

            for (int i = 0; i < directions.Count; i++) directions[i] = DiffractingVertex.Mod(directions[i], DiffractingVertex.TwoPi);
            directions.Sort();

            unique.Clear();
            foreach (double d in directions)
            {
                if (unique.Count == 0 || d - unique[^1] > DiffractingVertex.AngleEpsilonRad) unique.Add(d);
            }

            if (unique.Count > 1 && unique[0] + DiffractingVertex.TwoPi - unique[^1] <= DiffractingVertex.AngleEpsilonRad)
            {
                unique.RemoveAt(unique.Count - 1);
            }

            double start = 0.0, sector = -1.0;
            if (unique.Count == 1)
            {
                start = unique[0];
                sector = DiffractingVertex.TwoPi;
            }
            else
            {
                for (int i = 0; i < unique.Count; i++)
                {
                    double width = DiffractingVertex.Mod(unique[(i + 1) % unique.Count] - unique[i], DiffractingVertex.TwoPi);
                    if (width > sector)
                    {
                        start = unique[i];
                        sector = width;
                    }
                }
            }

            if (sector > MinSectorRad) vertices.Add(new DiffractingVertex(node, start, sector, loss));
        }

        return vertices;
    }

    private static void AddNode(List<Vec2> nodes, Vec2 p, double tolerance)
    {
        foreach (Vec2 q in nodes)
        {
            if (Vec2.Distance(p, q) <= tolerance) return;
        }

        nodes.Add(p);
    }

    private static bool InsideAColumn(SimulationScene scene, Vec2 node)
    {
        foreach (ResolvedWall rw in scene.Walls)
        {
            if (rw.Footprint is not null && rw.Footprint.ContainsStrictly(node)) return true;
        }

        return false;
    }
}
