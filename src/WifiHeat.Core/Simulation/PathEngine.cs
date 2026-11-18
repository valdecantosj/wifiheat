using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;

namespace WifiHeat.Core.Simulation;

/// <summary>
/// Motor de trayectos del modelo con difracción, para una escena, un AP y unos parámetros.
/// En cada punto suma las potencias (sin fases) del trayecto directo y de las polilíneas AP → v1 → … → vk → punto
/// que rodean vértices difractantes. La pérdida por difracción usa el filo de cuchilla (<see cref="KnifeEdge"/>),
/// combina varios vértices con la construcción de Deygout en forma angular y corrige las aristas próximas.
/// Con reflexiones suma además los trayectos AP → R → punto con un rebote en un muro o cara de columna (método de las
/// imágenes) y pérdida de Fresnel TE (<see cref="Fresnel"/>).
/// El constructor precalcula los vértices, las llegadas a cada vértice, sus tramos y las imágenes del AP; después la
/// instancia no cambia y sus consultas se pueden hacer desde varios hilos.
/// </summary>
public sealed class PathEngine
{
    /// <summary>Distancia mínima (m) del AP o del punto a la recta de rebote.</summary>
    public const double MinReflectionDistanceM = 1e-6;

    /// <summary>ν mínimo, respecto del tramo, para considerar un vértice de transición.</summary>
    public const double LitPrefilterNu = -3.0;

    private const int ApNode = -1;
    private const int RxNode = -2;
    private const double OpaqueLimit = 1 - 1e-12;

    [ThreadStatic] private static double[]? t_losses;
    [ThreadStatic] private static List<LitCandidate>? t_lit;

    private readonly SimulationScene _scene;
    private readonly RadioSettings _settings;
    private readonly Band _band;
    private readonly Vec2 _ap;
    private readonly double _frequencyMHz;
    private readonly double _lambda;
    private readonly PropagationOptions _options;
    private readonly DiffractingVertex[] _vertices;
    private readonly Vec2[] _positions;
    private readonly Dictionary<(int From, int To), Leg> _legs = [];
    private readonly Arrival[][][] _arrivals;
    private readonly PropagationModel _model;
    private readonly ReflectingSurface[] _surfaces;
    private readonly Mirror[] _mirrors;

    public PathEngine(SimulationScene scene, AccessPoint accessPoint, RadioSettings settings, PropagationOptions? options = null)
        : this(scene, accessPoint, settings, PropagationModel.Diffraction, options)
    {
    }

    public PathEngine(SimulationScene scene, AccessPoint accessPoint, RadioSettings settings, PropagationModel model, PropagationOptions? options = null)
    {
        if (model is not (PropagationModel.Diffraction or PropagationModel.DiffractionAndReflection))
        {
            throw new ArgumentOutOfRangeException(nameof(model), $"El modelo «{PropagationModelInfo.Label(model)}» no usa este motor.");
        }

        _model = model;
        _options = options ?? PropagationOptions.Default;
        if (!PropagationOptions.IsValidMaxDiffractions(_options.MaxDiffractions))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Las difracciones por trayecto van de 1 a 4.");
        }

        if (_options.BeamWidth < 1 || _options.MaxLitPerLeg < 0 || !(_options.PruneDb > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        _scene = scene;
        _settings = settings;
        _band = settings.Band;
        AccessPoint = accessPoint;
        _ap = accessPoint.Position;
        _frequencyMHz = settings.FrequencyMHz;
        _lambda = KnifeEdge.WavelengthM(_frequencyMHz);
        _vertices = [.. DiffractingVertices.Find(scene, _band)];
        _positions = [.. _vertices.Select(v => v.Position)];
        _arrivals = BuildArrivals();
        _surfaces = model == PropagationModel.DiffractionAndReflection ? [.. ReflectingSurfaces.Find(scene)] : [];
        _mirrors = BuildMirrors();
    }

    /// <summary>Motor para un modelo distinto de línea recta.</summary>
    public static PathEngine For(SimulationScene scene, AccessPoint accessPoint, RadioSettings settings, PropagationModel model, PropagationOptions? options = null) =>
        new(scene, accessPoint, settings, model, options);

    public SimulationScene Scene => _scene;

    public AccessPoint AccessPoint { get; }

    public RadioSettings Settings => _settings;

    public PropagationModel Model => _model;

    public IReadOnlyList<DiffractingVertex> Vertices => _vertices;

    /// <summary>Superficies donde puede rebotar un trayecto; vacío sin reflexiones.</summary>
    public IReadOnlyList<ReflectingSurface> Surfaces => _surfaces;

    public PropagationOptions Options => _options;

    /// <summary>Potencia recibida: suma de los trayectos que no pierden más que el mejor en el margen de poda.</summary>
    public double ReceivedPowerDbm(Vec2 point) => LinkBudget.ReceivedPowerDbm(_settings, CombinedLossDb(point), 0);

    /// <summary>Pérdida equivalente (dB) de la suma en potencia de los trayectos.</summary>
    public double CombinedLossDb(Vec2 point) => Evaluate(point, null);

    /// <summary>Trayectos sumados en el punto, del que más aporta al que menos.</summary>
    public IReadOnlyList<PathContribution> Paths(Vec2 point)
    {
        var raw = new List<RawPath>();
        Evaluate(point, raw);

        double best = double.PositiveInfinity;
        foreach (RawPath path in raw) best = Math.Min(best, path.LossDb);

        double total = 0;
        foreach (RawPath path in raw)
        {
            if (path.LossDb <= best + _options.PruneDb) total += Math.Pow(10, -path.LossDb / 10);
        }

        var result = new List<PathContribution>();
        foreach (RawPath path in raw)
        {
            if (path.LossDb > best + _options.PruneDb) continue;

            double share = Math.Pow(10, -path.LossDb / 10) / total;
            if (path.Surface >= 0)
            {
                ReflectingSurface s = _surfaces[path.Surface];
                double incidence = Math.Acos(Math.Clamp(path.CosIncidence, 0.0, 1.0)) * 180 / Math.PI;
                result.Add(new PathContribution(PathKind.Reflected, [path.Bounce], path.LengthM, path.LossDb, share,
                    new PathReflection(s.WallId, s.IsColumn, s.MaterialName, path.ReflectionDb, incidence)));
                continue;
            }

            Vec2[] vertices = [.. path.Chain.Select(i => _positions[i])];
            result.Add(new PathContribution(vertices.Length == 0 ? PathKind.Direct : PathKind.Diffracted, vertices, path.LengthM, path.LossDb, share));
        }

        return [.. result.OrderBy(p => p.LossDb)];
    }

    // ---- precálculo: llegadas a cada vértice

    /// <summary>
    /// arrivals[k][j]: hasta BeamWidth llegadas al vértice j después de k difracciones previas.
    /// El orden usa una estimación (suma de J con los vecinos inmediatos) solo para elegir cuáles conservar.
    /// </summary>
    private Arrival[][][] BuildArrivals()
    {
        int count = _vertices.Length;
        var arrivals = new Arrival[_options.MaxDiffractions][][];

        var level = new Arrival[count][];
        for (int j = 0; j < count; j++)
        {
            Leg leg = CachedLeg(ApNode, j);
            double d = Vec2.Distance(_ap, _positions[j]);
            level[j] = [new Arrival(leg.MaterialsDb + 20 * Math.Log10(Math.Max(d, 1.0)), leg.MaterialsDb, d, [], [leg])];
        }

        arrivals[0] = level;

        var kept = new List<Candidate>(_options.BeamWidth + 1);
        for (int k = 1; k < _options.MaxDiffractions; k++)
        {
            var next = new Arrival[count][];
            for (int j = 0; j < count; j++)
            {
                kept.Clear();
                Vec2 target = _positions[j];
                for (int i = 0; i < count; i++)
                {
                    DiffractingVertex u = _vertices[i];
                    if (i == j || u.Transmission >= OpaqueLimit) continue;

                    foreach (Arrival arrival in arrivals[k - 1][i])
                    {
                        if (Array.IndexOf(arrival.Chain, j) >= 0) continue;

                        Vec2 previous = arrival.Chain.Length == 0 ? _ap : _positions[arrival.Chain[^1]];
                        if (u.WrapAngle(previous, target) is not { } theta) continue;

                        double d1 = Vec2.Distance(previous, u.Position);
                        double d2 = Vec2.Distance(u.Position, target);
                        double estimate = arrival.Order - 20 * Math.Log10(Math.Max(arrival.LengthM, 1.0)) - arrival.MaterialsDb;
                        estimate += KnifeEdge.LossDb(KnifeEdge.NuFromAngle(theta, d1, d2, _lambda)) - 10 * Math.Log10(1 - u.Transmission);
                        double materials = arrival.MaterialsDb + CachedLeg(i, j).MaterialsDb;
                        double length = arrival.LengthM + d2;
                        Keep(kept, new Candidate(materials + estimate + 20 * Math.Log10(Math.Max(length, 1.0)), materials, length, arrival, i));
                    }
                }

                var chosen = new Arrival[kept.Count];
                for (int n = 0; n < kept.Count; n++)
                {
                    Candidate c = kept[n];
                    chosen[n] = new Arrival(c.Order, c.MaterialsDb, c.LengthM, [.. c.Parent.Chain, c.Via], [.. c.Parent.Legs, CachedLeg(c.Via, j)]);
                }

                next[j] = chosen;
            }

            arrivals[k] = next;
        }

        return arrivals;
    }

    /// <summary>Inserta el candidato en la lista ordenada y la recorta a BeamWidth. Entre iguales queda primero el que llegó antes.</summary>
    private void Keep(List<Candidate> kept, Candidate candidate)
    {
        int at = kept.Count;
        while (at > 0 && CompareCandidates(candidate, kept[at - 1]) < 0) at--;
        if (at >= _options.BeamWidth) return;

        kept.Insert(at, candidate);
        if (kept.Count > _options.BeamWidth) kept.RemoveAt(kept.Count - 1);
    }

    /// <summary>
    /// Orden de las llegadas: estimación redondeada a 1e-6 dB y, ante empate, las coordenadas (x, y) de los vértices de la cadena.
    /// Así el resultado no depende del orden en que se numeran los vértices.
    /// </summary>
    private int CompareCandidates(Candidate a, Candidate b)
    {
        int byOrder = Math.Round(a.Order, 6).CompareTo(Math.Round(b.Order, 6));
        if (byOrder != 0) return byOrder;

        int lengthA = a.Parent.Chain.Length + 1, lengthB = b.Parent.Chain.Length + 1;
        for (int n = 0; n < Math.Min(lengthA, lengthB); n++)
        {
            Vec2 pa = _positions[n < lengthA - 1 ? a.Parent.Chain[n] : a.Via];
            Vec2 pb = _positions[n < lengthB - 1 ? b.Parent.Chain[n] : b.Via];
            int byPosition = ComparePositions(pa, pb);
            if (byPosition != 0) return byPosition;
        }

        return lengthA.CompareTo(lengthB);
    }

    private static int ComparePositions(Vec2 a, Vec2 b)
    {
        int byX = a.X.CompareTo(b.X);
        return byX != 0 ? byX : a.Y.CompareTo(b.Y);
    }

    // ---- tramos

    private Vec2 Position(int node, Vec2 rx) => node == ApNode ? _ap : node == RxNode ? rx : _positions[node];

    private Leg CachedLeg(int from, int to)
    {
        if (_legs.TryGetValue((from, to), out Leg leg)) return leg;

        leg = ComputeLeg(from, to, default);
        _legs[(from, to)] = leg;
        return leg;
    }

    /// <summary>Pérdida por materiales y vértices de transición del tramo entre dos nodos.</summary>
    private Leg ComputeLeg(int from, int to, Vec2 rx)
    {
        Vec2 a = Position(from, rx);
        Vec2 b = Position(to, rx);
        return new Leg(LegLoss.LossDb(_scene, _band, a, b, from >= 0, to >= 0, _positions), LitVertices(a, b, from, to));
    }

    /// <summary>
    /// Vértices de transición del tramo a–b: los que el tramo deja cerca, del lado libre y sin rodearlos.
    /// Devuelve los MaxLitPerLeg de mayor ν respecto del tramo (ν > <see cref="LitPrefilterNu"/>), ordenados por su posición a lo largo del tramo.
    /// </summary>
    private int[] LitVertices(Vec2 a, Vec2 b, int skipA, int skipB)
    {
        Vec2 ab = b - a;
        double squared = Vec2.Dot(ab, ab);
        if (squared <= 0) return [];

        double length = Math.Sqrt(squared);
        List<LitCandidate> found = t_lit ??= [];
        found.Clear();
        for (int w = 0; w < _vertices.Length; w++)
        {
            if (w == skipA || w == skipB) continue;

            DiffractingVertex v = _vertices[w];
            double t = Vec2.Dot(v.Position - a, ab) / squared;
            if (!(t > 0 && t < 1)) continue;

            var foot = new Vec2(a.X + t * ab.X, a.Y + t * ab.Y);
            double h = Vec2.Distance(v.Position, foot);
            double d1 = t * length;
            double d2 = (1 - t) * length;
            double nu = -h * Math.Sqrt(2 * (d1 + d2) / (_lambda * d1 * d2));
            if (nu <= LitPrefilterNu) continue;
            if (h > 1e-9 && !v.SeesStrictly(foot)) continue;
            if (v.WrapAngle(a, b) is not null) continue;

            found.Add(new LitCandidate(nu, w, t));
        }

        if (found.Count == 0) return [];

        found.Sort((x, y) =>
        {
            int byNu = Math.Round(y.Nu, 9).CompareTo(Math.Round(x.Nu, 9));
            return byNu != 0 ? byNu : ComparePositions(_positions[x.Vertex], _positions[y.Vertex]);
        });
        if (found.Count > _options.MaxLitPerLeg) found.RemoveRange(_options.MaxLitPerLeg, found.Count - _options.MaxLitPerLeg);

        found.Sort((x, y) =>
        {
            int byT = Math.Round(x.T, 9).CompareTo(Math.Round(y.T, 9));
            return byT != 0 ? byT : ComparePositions(_positions[x.Vertex], _positions[y.Vertex]);
        });

        var result = new int[found.Count];
        for (int n = 0; n < result.Length; n++) result[n] = found[n].Vertex;
        return result;
    }

    // ---- difracción de un trayecto: Deygout angular con corrección por aristas próximas

    /// <summary>Giro con signo (rad) del trayecto a → u → b. Positivo: giro a la izquierda.</summary>
    private static double SignedTurn(Vec2 a, Vec2 u, Vec2 b)
    {
        Vec2 d1 = u - a;
        Vec2 d2 = b - u;
        return Math.Atan2(Vec2.Cross(d1, d2), Vec2.Dot(d1, d2));
    }

    /// <summary>ν del elemento respecto de la cuerda a–b; null si coincide con un extremo.</summary>
    private double? ChordNu(Vec2 a, in Item item, Vec2 b)
    {
        double d1 = Vec2.Distance(a, item.Position);
        double d2 = Vec2.Distance(item.Position, b);
        if (d1 <= 0 || d2 <= 0) return null;
        return item.Orientation * SignedTurn(a, item.Position, b) * Math.Sqrt(2 * d1 * d2 / (_lambda * (d1 + d2)));
    }

    /// <summary>
    /// Escribe en effective la pérdida efectiva (dB) de los elementos items[from..to), ordenados a lo largo del trayecto.
    /// 1. Arista principal: el elemento con mayor ν respecto de la cuerda a–b (ante igual ν, el primero). Su pérdida es J(ν).
    ///    Un vértice rodeado nunca toma ν menor que 0. Si ν ≤ −0,78 ningún elemento de la lista aporta.
    /// 2. Los anteriores se resuelven respecto de la cuerda a–principal y los posteriores respecto de principal–b, de forma recursiva.
    /// 3. Una arista secundaria aporta escala · (J − G · min(1, J / J(0))), con G = 12 − 20·log10(2 / (1 − α/π)) y
    ///    tan α = √(b·(a + b + c) / (a·c)); a, b y c son las distancias extremo–arista, arista–arista y arista–extremo en el
    ///    orden del trayecto. La escala vale 1 en un vértice rodeado; en uno de transición es la de la principal por min(1, J principal / J(0)).
    /// </summary>
    private void Deygout(Vec2 a, ReadOnlySpan<Item> items, int from, int to, Vec2 b, Span<double> effective, in Principal parent)
    {
        if (from >= to) return;

        int best = -1;
        double bestNu = 0;
        for (int n = from; n < to; n++)
        {
            if (ChordNu(a, items[n], b) is { } candidate && (best < 0 || candidate > bestNu))
            {
                best = n;
                bestNu = candidate;
            }
        }

        if (best < 0) return;

        Item item = items[best];
        double nu = item.Wrapped ? Math.Max(bestNu, 0.0) : bestNu;
        if (nu <= KnifeEdge.CutoffNu) return;

        double j = KnifeEdge.LossDb(nu);
        double value = j;
        double scale = 1.0;
        if (parent.Exists)
        {
            double aa, bb, cc;
            if (parent.After)
            {
                aa = Vec2.Distance(parent.Far, parent.Position);
                bb = Vec2.Distance(parent.Position, item.Position);
                cc = Vec2.Distance(item.Position, b);
            }
            else
            {
                aa = Vec2.Distance(a, item.Position);
                bb = Vec2.Distance(item.Position, parent.Position);
                cc = Vec2.Distance(parent.Position, parent.Far);
            }

            double g = 0.0;
            if (aa > 0 && bb > 0 && cc > 0)
            {
                double alpha = Math.Atan(Math.Sqrt(bb * (aa + bb + cc) / (aa * cc)));
                g = 12 - 20 * Math.Log10(2 / (1 - alpha / Math.PI));
            }

            scale = item.Wrapped ? 1.0 : parent.Scale * Math.Min(1.0, parent.LossDb / KnifeEdge.J0);
            value = scale * (j - g * Math.Min(1.0, j / KnifeEdge.J0));
        }

        effective[best] = value;
        Deygout(a, items, from, best, item.Position, effective, new Principal(true, item.Position, j, scale, b, false));
        Deygout(item.Position, items, best + 1, to, b, effective, new Principal(true, item.Position, j, scale, a, true));
    }

    /// <summary>
    /// Pérdida total (dB) del trayecto AP → vértices de la llegada → vertex → rx (arrival null: trayecto directo).
    /// Elementos de difracción, en el orden del trayecto: por cada tramo, sus vértices de transición (orientación opuesta
    /// al giro a → w → b del tramo) y después el vértice rodeado en que termina (orientación = sentido de su giro real).
    /// Vértice rodeado: suma su pérdida efectiva y −10·log10(1 − T).
    /// Vértice de transición: suma −10·log10(T + (1 − T)·10^(−efectiva/10)).
    /// </summary>
    private double ChainLoss(Arrival? arrival, int vertex, in Leg lastLeg, Vec2 rx, out double lengthM)
    {
        int wraps = arrival is null ? 0 : arrival.Chain.Length + 1;
        Span<Vec2> points = stackalloc Vec2[wraps + 2];
        Span<int> nodes = stackalloc int[wraps + 2];
        points[0] = _ap;
        for (int n = 1; n < wraps; n++)
        {
            nodes[n] = arrival!.Chain[n - 1];
            points[n] = _positions[nodes[n]];
        }

        if (wraps > 0)
        {
            nodes[wraps] = vertex;
            points[wraps] = _positions[vertex];
        }

        points[wraps + 1] = rx;

        double length = 0;
        for (int n = 0; n <= wraps; n++) length += Vec2.Distance(points[n], points[n + 1]);
        lengthM = length;

        int itemCount = wraps;
        double materials = 0;
        for (int n = 0; n <= wraps; n++)
        {
            Leg leg = n < wraps ? arrival!.Legs[n] : lastLeg;
            itemCount += leg.Lit.Length;
            materials += leg.MaterialsDb;
        }

        double loss = PathLoss.FreeSpaceDbFromMetres(_frequencyMHz, length) + materials;
        if (itemCount == 0) return loss;

        Span<Item> items = itemCount <= 64 ? stackalloc Item[itemCount] : new Item[itemCount];
        int at = 0;
        for (int n = 0; n <= wraps; n++)
        {
            Leg leg = n < wraps ? arrival!.Legs[n] : lastLeg;
            foreach (int w in leg.Lit)
            {
                double turn = SignedTurn(points[n], _positions[w], points[n + 1]);
                items[at++] = new Item(_positions[w], turn > 0 ? -1.0 : 1.0, false, w);
            }

            if (n < wraps)
            {
                double turn = SignedTurn(points[n], points[n + 1], points[n + 2]);
                items[at++] = new Item(points[n + 1], turn > 0 ? 1.0 : -1.0, true, nodes[n + 1]);
            }
        }

        Span<double> effective = itemCount <= 64 ? stackalloc double[itemCount] : new double[itemCount];
        effective.Fill(double.NaN);
        Deygout(points[0], items, 0, itemCount, points[wraps + 1], effective, default);

        for (int n = 0; n < itemCount; n++)
        {
            DiffractingVertex v = _vertices[items[n].Vertex];
            bool resolved = !double.IsNaN(effective[n]);
            if (items[n].Wrapped)
            {
                loss += (resolved ? effective[n] : KnifeEdge.J0) - 10 * Math.Log10(1 - v.Transmission);
            }
            else
            {
                loss += -10 * Math.Log10(v.Transmission + (1 - v.Transmission) * Math.Pow(10, -(resolved ? effective[n] : 0.0) / 10));
            }
        }

        return loss;
    }

    // ---- resultado en un punto

    /// <summary>Recorre los trayectos del punto y devuelve la pérdida equivalente de su suma. Con detail, además los lista.</summary>
    private double Evaluate(Vec2 rx, List<RawPath>? detail)
    {
        double[] losses = t_losses ??= new double[256];
        int count = 0;

        Leg direct = ComputeLeg(ApNode, RxNode, rx);
        losses[count++] = ChainLoss(null, -1, direct, rx, out double directLength);
        detail?.Add(new RawPath([], directLength, losses[0]));

        for (int i = 0; i < _vertices.Length; i++)
        {
            DiffractingVertex v = _vertices[i];
            if (v.Transmission >= OpaqueLimit) continue;

            Leg last = default;
            bool hasLast = false;
            for (int k = 0; k < _arrivals.Length; k++)
            {
                foreach (Arrival arrival in _arrivals[k][i])
                {
                    Vec2 previous = arrival.Chain.Length == 0 ? _ap : _positions[arrival.Chain[^1]];
                    if (v.WrapAngle(previous, rx) is null) continue;

                    if (!hasLast)
                    {
                        last = ComputeLeg(i, RxNode, rx);
                        hasLast = true;
                    }

                    double loss = ChainLoss(arrival, i, last, rx, out double length);
                    if (count == losses.Length)
                    {
                        Array.Resize(ref losses, losses.Length * 2);
                        t_losses = losses;
                    }

                    losses[count++] = loss;
                    detail?.Add(new RawPath([.. arrival.Chain, i], length, loss));
                }
            }
        }

        double best = double.PositiveInfinity;
        for (int n = 0; n < count; n++) best = Math.Min(best, losses[n]);

        for (int s = 0; s < _surfaces.Length; s++)
        {
            if (!Reflect(s, rx, out Vec2 bounce, out double length, out double cos)) continue;

            double reflection = Fresnel.ReflectionLossDb(_mirrors[s].Permittivity, cos);
            double loss = PathLoss.FreeSpaceDbFromMetres(_frequencyMHz, length) + reflection;
            if (loss > best + _options.PruneDb) continue;

            loss += LegLoss.LossDb(_scene, _band, _ap, bounce, false, true, _positions)
                  + LegLoss.LossDb(_scene, _band, bounce, rx, true, false, _positions);
            if (count == losses.Length)
            {
                Array.Resize(ref losses, losses.Length * 2);
                t_losses = losses;
            }

            losses[count++] = loss;
            best = Math.Min(best, loss);
            detail?.Add(new RawPath([], length, loss, s, bounce, cos, reflection));
        }

        double total = 0;
        for (int n = 0; n < count; n++)
        {
            if (losses[n] <= best + _options.PruneDb) total += Math.Pow(10, -losses[n] / 10);
        }

        return -10 * Math.Log10(total);
    }

    // ---- reflexiones de primer orden: método de las imágenes

    private Mirror[] BuildMirrors()
    {
        var mirrors = new Mirror[_surfaces.Length];
        double frequencyGHz = _frequencyMHz / 1000;
        for (int s = 0; s < _surfaces.Length; s++)
        {
            ReflectingSurface surface = _surfaces[s];
            mirrors[s] = new Mirror(surface.Mirror(_ap), surface.SignedDistance(_ap), surface.Material.ComplexPermittivity(frequencyGHz));
        }

        return mirrors;
    }

    /// <summary>
    /// Rebote en la superficie s hacia rx. Vale si el AP y rx están del mismo lado de la recta (del lado exterior en una
    /// columna), a más de <see cref="MinReflectionDistanceM"/> de ella, y si la recta imagen → rx corta el segmento en su interior.
    /// length = |imagen − rx| = d1 + d2. cos: coseno del ángulo de incidencia, medido desde la normal.
    /// </summary>
    private bool Reflect(int s, Vec2 rx, out Vec2 bounce, out double length, out double cos)
    {
        bounce = default;
        length = 0;
        cos = 0;

        ReflectingSurface surface = _surfaces[s];
        Mirror mirror = _mirrors[s];
        double hAp = mirror.ApDistance;
        double hRx = surface.SignedDistance(rx);
        if (Math.Abs(hAp) <= MinReflectionDistanceM || Math.Abs(hRx) <= MinReflectionDistanceM) return false;
        if ((hAp > 0) != (hRx > 0)) return false;
        if (surface.OutwardOnly && hAp < 0) return false;

        double k = Math.Abs(hAp) / (Math.Abs(hAp) + Math.Abs(hRx));
        Vec2 toRx = rx - mirror.Image;
        bounce = mirror.Image + toRx * k;

        Vec2 ab = surface.Segment.B - surface.Segment.A;
        double t = Vec2.Dot(bounce - surface.Segment.A, ab) / Vec2.Dot(ab, ab);
        if (!(t > 0 && t < 1)) return false;

        length = toRx.Length;
        cos = (Math.Abs(hAp) + Math.Abs(hRx)) / length;
        return true;
    }

    /// <summary>Imagen del AP respecto de una superficie, su distancia con signo a la recta y la permitividad compleja del material.</summary>
    private readonly record struct Mirror(Vec2 Image, double ApDistance, System.Numerics.Complex Permittivity);

    /// <summary>Tramo entre dos nodos: pérdida por materiales y vértices de transición, en el orden del tramo.</summary>
    private readonly record struct Leg(double MaterialsDb, int[] Lit);

    /// <summary>
    /// Trayecto que llega a un vértice. Order: estimación para ordenar. Chain: vértices ya rodeados.
    /// Legs: tramos AP → … → vértice de llegada.
    /// </summary>
    private sealed record Arrival(double Order, double MaterialsDb, double LengthM, int[] Chain, Leg[] Legs);

    private readonly record struct Candidate(double Order, double MaterialsDb, double LengthM, Arrival Parent, int Via);

    private readonly record struct LitCandidate(double Nu, int Vertex, double T);

    /// <summary>Elemento de difracción de un trayecto. Orientation: +1 o −1, sentido en que el trayecto lo rodea o lo deja.</summary>
    private readonly record struct Item(Vec2 Position, double Orientation, bool Wrapped, int Vertex);

    /// <summary>Arista principal bajo la cual se resuelve una secundaria. Far: extremo lejano de su cuerda. After: la secundaria va después.</summary>
    private readonly record struct Principal(bool Exists, Vec2 Position, double LossDb, double Scale, Vec2 Far, bool After);

    /// <summary>Surface: índice de la superficie del rebote (−1 si no es reflejado); Bounce, CosIncidence y ReflectionDb solo en el reflejado.</summary>
    private readonly record struct RawPath(int[] Chain, double LengthM, double LossDb, int Surface = -1, Vec2 Bounce = default,
        double CosIncidence = 0, double ReflectionDb = 0);
}
