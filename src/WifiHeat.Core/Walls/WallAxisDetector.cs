using WifiHeat.Core.Geometry;

namespace WifiHeat.Core.Walls;

/// <summary>Segmento recto de una cara de muro tomado del plano. Id debe ser único (por ejemplo, handle o handle/índice).</summary>
public sealed record FaceSegment(string Id, Segment2 Segment);

/// <summary>Eje propuesto entre dos o más caras paralelas. ThicknessM es la distancia entre caras.</summary>
public sealed record WallAxis(Segment2 Axis, double ThicknessM, IReadOnlyList<string> FaceIds);

/// <summary>
/// Resultado de la detección. Unpaired: segmentos sin ninguna cara opuesta.
/// PartlyUnpaired: segmentos con un tramo continuo sin par más largo que <see cref="AxisDetectionOptions.ReportGapM"/>.
/// </summary>
public sealed record AxisDetection(IReadOnlyList<WallAxis> Axes, IReadOnlyList<string> Unpaired, IReadOnlyList<string> PartlyUnpaired);

public sealed record AxisDetectionOptions
{
    public double MaxSeparationM { get; init; } = 0.5;
    public double MinSeparationM { get; init; } = 0.05;
    public double MinOverlapM { get; init; } = 0.1;
    public double AngleToleranceDeg { get; init; } = 1;

    public const double DefaultMergeGapM = 0.2;
    public const double LargestMergeGapM = 2;

    /// <summary>Ejes colineales separados por no más que esto se unen; las aberturas más angostas se cierran.</summary>
    public double MaxMergeGapM { get; init; } = DefaultMergeGapM;

    public static string? MergeGapError(double gapM) =>
        double.IsFinite(gapM) && gapM >= 0 && gapM <= LargestMergeGapM
            ? null
            : string.Create(System.Globalization.CultureInfo.GetCultureInfo("es-AR"), $"El umbral de unión debe estar entre 0 y {LargestMergeGapM:0} m.");

    /// <summary>Holgura sobre medio espesor al prolongar un eje hasta el eje de otro muro.</summary>
    public double ExtendToleranceM { get; init; } = 0.05;

    public double ReportGapM { get; init; } = 0.5;

    /// <summary>False: dos tramos de la misma entidad (mismo id antes de «/») no forman par.</summary>
    public bool PairWithinSameEntity { get; init; } = true;
}

/// <summary>
/// Convierte muros dibujados con sus dos caras en ejes: empareja segmentos paralelos cercanos que se solapan,
/// propone el eje a mitad de distancia, une tramos colineales y prolonga los extremos hasta el eje vecino
/// (esquinas en L, uniones en T). No modifica las caras.
/// </summary>
public static class WallAxisDetector
{
    private const double CollinearOffsetM = 0.01;
    private const double SameThicknessM = 0.02;
    private const double MinJoinAngleDeg = 10;
    private const double Tiny = 1e-9;

    /// <summary>Id de la entidad de un tramo: lo que precede a «/» (handle o handle/índice).</summary>
    public static string EntityId(string faceId)
    {
        int slash = faceId.IndexOf('/');
        return slash < 0 ? faceId : faceId[..slash];
    }

    /// <summary>Segmentos con los dos extremos dentro de la zona.</summary>
    public static IReadOnlyList<FaceSegment> InsideWindow(IEnumerable<FaceSegment> faces, BoundingBox window) =>
        [.. faces.Where(f => Inside(f.Segment.A, window) && Inside(f.Segment.B, window))];

    public static AxisDetection Detect(IReadOnlyList<FaceSegment> faces, AxisDetectionOptions? options = null)
    {
        options ??= new AxisDetectionOptions();
        var lines = faces.Where(f => f.Segment.Length > Tiny).Select(f => new Face(f)).ToList();
        double sinTol = Math.Sin(options.AngleToleranceDeg * Math.PI / 180);

        var candidates = new List<(int I, int J, double Sep, double Overlap)>();
        for (int i = 0; i < lines.Count; i++)
        {
            for (int j = i + 1; j < lines.Count; j++)
            {
                Face a = lines[i], b = lines[j];
                if (Math.Abs(Vec2.Cross(a.U, b.U)) > sinTol) continue;
                if (!options.PairWithinSameEntity && a.Entity == b.Entity) continue;

                (double from, double to) = a.Project(b);
                double overlap = to - from;
                if (overlap < options.MinOverlapM) continue;

                double sep = Math.Abs(Vec2.Cross(a.U, b.Closest(a.At((from + to) / 2)) - a.Start));
                if (sep < options.MinSeparationM || sep > options.MaxSeparationM) continue;
                candidates.Add((i, j, sep, overlap));
            }
        }

        var pieces = new List<Piece>();
        foreach (var c in candidates.OrderBy(c => c.Sep).ThenByDescending(c => c.Overlap))
        {
            Face a = lines[c.I], b = lines[c.J];
            (double from, double to) = a.Project(b);
            List<(double From, double To)> free = Subtract([(from, to)], a.Used);
            free = Subtract(free, [.. b.Used.Select(u => a.FromOther(b, u))]);

            foreach ((double f, double t) in free)
            {
                if (t - f < options.MinOverlapM) continue;

                Vec2 p0 = a.At(f), p1 = a.At(t);
                Vec2 q0 = b.Closest(p0), q1 = b.Closest(p1);
                pieces.Add(new Piece(new Segment2((p0 + q0) * 0.5, (p1 + q1) * 0.5), c.Sep, [a.Source.Id, b.Source.Id]));
                a.Used.Add((f, t));
                b.Used.Add(Sorted(b.Param(p0), b.Param(p1)));
            }
        }

        List<WallAxis> axes = Extend(Merge(pieces, options, sinTol), options);

        var unpaired = new List<string>();
        var partly = new List<string>();
        foreach (Face f in lines)
        {
            if (f.Used.Count == 0) unpaired.Add(f.Source.Id);
            else if (Subtract([(0, f.Length)], f.Used).Any(g => g.To - g.From > options.ReportGapM)) partly.Add(f.Source.Id);
        }

        return new AxisDetection(axes, unpaired, partly);
    }

    private static List<Piece> Merge(List<Piece> pieces, AxisDetectionOptions options, double sinTol)
    {
        var result = new List<Piece>();
        var done = new bool[pieces.Count];
        for (int i = 0; i < pieces.Count; i++)
        {
            if (done[i]) continue;
            done[i] = true;
            Piece current = pieces[i];

            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int j = 0; j < pieces.Count; j++)
                {
                    if (done[j] || !CanMerge(current, pieces[j], options, sinTol)) continue;
                    current = Join(current, pieces[j]);
                    done[j] = true;
                    changed = true;
                }
            }

            result.Add(current);
        }

        return result;
    }

    private static bool CanMerge(Piece a, Piece b, AxisDetectionOptions options, double sinTol)
    {
        Vec2 u = Unit(a.Axis);
        if (Math.Abs(Vec2.Cross(u, Unit(b.Axis))) > sinTol) return false;
        if (Math.Abs(a.ThicknessM - b.ThicknessM) > SameThicknessM) return false;
        if (Math.Abs(Vec2.Cross(u, b.Axis.A - a.Axis.A)) > CollinearOffsetM) return false;
        if (Math.Abs(Vec2.Cross(u, b.Axis.B - a.Axis.A)) > CollinearOffsetM) return false;

        (double b0, double b1) = Sorted(Dot(b.Axis.A - a.Axis.A, u), Dot(b.Axis.B - a.Axis.A, u));
        double gap = Math.Max(b0 - a.Axis.Length, -b1);
        return gap <= options.MaxMergeGapM;
    }

    private static Piece Join(Piece a, Piece b)
    {
        Vec2 u = Unit(a.Axis);
        Vec2 origin = a.Axis.A;
        double[] t = [0, a.Axis.Length, Dot(b.Axis.A - origin, u), Dot(b.Axis.B - origin, u)];
        double thickness = (a.ThicknessM * a.Axis.Length + b.ThicknessM * b.Axis.Length) / (a.Axis.Length + b.Axis.Length);
        return new Piece(new Segment2(origin + u * t.Min(), origin + u * t.Max()), thickness, [.. a.FaceIds.Union(b.FaceIds)]);
    }

    /// <summary>Prolonga cada extremo hasta el eje no paralelo más cercano, si está a no más de medio espesor de ese muro.</summary>
    private static List<WallAxis> Extend(List<Piece> pieces, AxisDetectionOptions options)
    {
        double sinJoin = Math.Sin(MinJoinAngleDeg * Math.PI / 180);
        var result = new List<WallAxis>(pieces.Count);
        foreach (Piece p in pieces)
        {
            Vec2 u = Unit(p.Axis);
            double length = p.Axis.Length;
            double start = 0, end = length;
            double bestStart = double.MaxValue, bestEnd = double.MaxValue;

            foreach (Piece other in pieces)
            {
                if (ReferenceEquals(other, p)) continue;
                Vec2 v = Unit(other.Axis);
                double cross = Vec2.Cross(u, v);
                if (Math.Abs(cross) < sinJoin) continue;

                Vec2 w = other.Axis.A - p.Axis.A;
                double t = Vec2.Cross(w, v) / cross;
                double s = Vec2.Cross(w, u) / cross;
                double reach = other.ThicknessM / 2 + options.ExtendToleranceM;
                double slack = p.ThicknessM / 2 + options.ExtendToleranceM;
                if (s < -slack || s > other.Axis.Length + slack) continue;

                if (t < 0 && -t <= reach && -t < bestStart) { bestStart = -t; start = t; }
                if (t > length && t - length <= reach && t - length < bestEnd) { bestEnd = t - length; end = t; }
            }

            result.Add(new WallAxis(new Segment2(p.Axis.A + u * start, p.Axis.A + u * end), p.ThicknessM, p.FaceIds));
        }

        return result;
    }

    private static List<(double From, double To)> Subtract(List<(double From, double To)> intervals, List<(double From, double To)> remove)
    {
        foreach ((double rf, double rt) in remove)
        {
            var next = new List<(double From, double To)>();
            foreach ((double f, double t) in intervals)
            {
                if (rt <= f || rf >= t) { next.Add((f, t)); continue; }
                if (rf > f) next.Add((f, rf));
                if (rt < t) next.Add((rt, t));
            }

            intervals = next;
        }

        return intervals;
    }

    private static bool Inside(Vec2 p, BoundingBox w) => p.X >= w.Min.X && p.X <= w.Max.X && p.Y >= w.Min.Y && p.Y <= w.Max.Y;

    private static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    private static Vec2 Unit(Segment2 s) => (s.B - s.A) * (1 / s.Length);

    private static (double, double) Sorted(double a, double b) => a <= b ? (a, b) : (b, a);

    private sealed record Piece(Segment2 Axis, double ThicknessM, IReadOnlyList<string> FaceIds);

    /// <summary>Cara con parámetro t en metros desde Start a lo largo de U, y los tramos ya emparejados.</summary>
    private sealed class Face(FaceSegment source)
    {
        public FaceSegment Source { get; } = source;
        public string Entity { get; } = EntityId(source.Id);
        public Vec2 Start { get; } = source.Segment.A;
        public double Length { get; } = source.Segment.Length;
        public Vec2 U { get; } = Unit(source.Segment);
        public List<(double From, double To)> Used { get; } = [];

        public Vec2 At(double t) => Start + U * t;

        public double Param(Vec2 p) => Dot(p - Start, U);

        public Vec2 Closest(Vec2 p) => At(Math.Clamp(Param(p), 0, Length));

        /// <summary>Solape de la proyección de otra cara sobre esta, en el parámetro de esta.</summary>
        public (double From, double To) Project(Face other)
        {
            (double a, double b) = Sorted(Param(other.Start), Param(other.At(other.Length)));
            return (Math.Max(0, a), Math.Min(Length, b));
        }

        public (double From, double To) FromOther(Face other, (double From, double To) interval) =>
            Sorted(Param(other.At(interval.From)), Param(other.At(interval.To)));
    }
}
