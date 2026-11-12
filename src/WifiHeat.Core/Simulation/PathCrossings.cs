using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;

namespace WifiHeat.Core.Simulation;

/// <summary>Muros que el trayecto toca en un mismo lugar. Solo cuenta la pérdida de <see cref="Counted"/>, la mayor.</summary>
public sealed record CrossingGroup(ResolvedWall Counted, double LossDb, IReadOnlyList<ResolvedWall> Others);

/// <summary>
/// Muros cortados por el trayecto AP–punto.
/// Cada muro cuenta a lo sumo una vez, aunque el trayecto lo toque en un vértice o en varios segmentos.
/// Los muros que el trayecto toca en el mismo punto (uniones en T, cruces, vértices compartidos o muros
/// superpuestos, con tolerancia <see cref="JunctionToleranceM"/>) forman un grupo que aporta una sola vez la pérdida mayor.
/// </summary>
public static class PathCrossings
{
    public const double JunctionToleranceM = 0.001;

    public static IReadOnlyList<CrossingGroup> Find(SimulationScene scene, Segment2 path, Band band)
    {
        List<Contact>? contacts = Contacts(scene, path);
        if (contacts is null) return [];

        int[] group = Group(contacts);
        var result = new List<CrossingGroup>();
        for (int root = 0; root < contacts.Count; root++)
        {
            if (group[root] != root) continue;

            int best = -1;
            for (int i = 0; i < contacts.Count; i++)
            {
                if (group[i] == root && (best < 0 || contacts[i].LossDb(band) > contacts[best].LossDb(band))) best = i;
            }

            var others = new List<ResolvedWall>();
            for (int i = 0; i < contacts.Count; i++)
            {
                if (group[i] == root && i != best) others.Add(contacts[i].Wall);
            }

            result.Add(new CrossingGroup(contacts[best].Wall, contacts[best].LossDb(band), others));
        }

        return result;
    }

    /// <summary>Suma de pérdidas con la misma regla que <see cref="Find"/>, sin armar la lista de grupos.</summary>
    public static double TotalLossDb(SimulationScene scene, Segment2 path, Band band)
    {
        List<Contact>? contacts = Contacts(scene, path);
        if (contacts is null) return 0;
        if (contacts.Count == 1) return contacts[0].LossDb(band);

        int[] group = Group(contacts);
        var max = new double[contacts.Count];
        Array.Fill(max, double.NegativeInfinity);
        for (int i = 0; i < contacts.Count; i++) max[group[i]] = Math.Max(max[group[i]], contacts[i].LossDb(band));

        double total = 0;
        foreach (double loss in max)
        {
            if (!double.IsNegativeInfinity(loss)) total += loss;
        }

        return total;
    }

    private static List<Contact>? Contacts(SimulationScene scene, Segment2 path)
    {
        List<Contact>? contacts = null;
        foreach (ResolvedWall rw in scene.Walls)
        {
            List<(double From, double To)>? spans = null;
            foreach (Segment2 segment in rw.Wall.Segments)
            {
                if (!SegmentIntersection.Intersects(path, segment)) continue;
                (spans ??= []).Add(Span(path, segment));
            }

            if (spans is not null) (contacts ??= []).Add(new Contact(rw, spans));
        }

        return contacts;
    }

    /// <summary>Une los muros que comparten un punto de contacto; devuelve la raíz de cada uno.</summary>
    private static int[] Group(List<Contact> contacts)
    {
        var parent = new int[contacts.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        for (int i = 0; i < contacts.Count; i++)
        {
            for (int j = i + 1; j < contacts.Count; j++)
            {
                if (contacts[i].Touches(contacts[j])) parent[Find(j)] = Find(i);
            }
        }

        for (int i = 0; i < parent.Length; i++) parent[i] = Find(i);
        return parent;

        int Find(int i)
        {
            while (parent[i] != i) i = parent[i] = parent[parent[i]];
            return i;
        }
    }

    /// <summary>Tramo del trayecto (distancias desde el AP, en m) que toca el segmento: un punto o, si son colineales, un intervalo.</summary>
    private static (double From, double To) Span(Segment2 path, Segment2 segment)
    {
        Vec2 r = path.B - path.A;
        double length = r.Length;
        if (length <= 0) return (0, 0);

        Vec2 s = segment.B - segment.A;
        double denominator = Vec2.Cross(r, s);
        if (Math.Abs(denominator) > 1e-12 * length * Math.Max(s.Length, 1e-12))
        {
            double t = Math.Clamp(Vec2.Cross(segment.A - path.A, s) / denominator, 0, 1);
            return (t * length, t * length);
        }

        double ta = Dot(segment.A - path.A, r) / length;
        double tb = Dot(segment.B - path.A, r) / length;
        return (Math.Clamp(Math.Min(ta, tb), 0, length), Math.Clamp(Math.Max(ta, tb), 0, length));
    }

    private static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    private sealed record Contact(ResolvedWall Wall, List<(double From, double To)> Spans)
    {
        public double LossDb(Band band) => Wall.Material.LossDb(band);

        public bool Touches(Contact other)
        {
            foreach (var a in Spans)
            {
                foreach (var b in other.Spans)
                {
                    if (a.From <= b.To + JunctionToleranceM && b.From <= a.To + JunctionToleranceM) return true;
                }
            }

            return false;
        }
    }
}
