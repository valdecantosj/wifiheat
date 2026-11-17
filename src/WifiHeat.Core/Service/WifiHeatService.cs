using System.Diagnostics;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Walls;
using WifiHeat.Core.Workflow;

namespace WifiHeat.Core.Service;

/// <summary>
/// Implementación común de <see cref="IWifiHeatService"/> sobre una <see cref="SimulationSession"/>.
/// El anfitrión (AutoCAD, o un dibujo falso en las pruebas) solo implementa la lectura y escritura del dibujo.
/// </summary>
public abstract class WifiHeatService(SimulationSession session) : IWifiHeatService
{
    public const double DefaultThicknessM = 0.15;

    private const double SameAxisToleranceM = 0.01;
    private const string MissingUnit = "El dibujo no tiene unidad guardada. Ejecutar WIFISETUP.";
    private const string NoFacesInWindow = "No hay líneas ni polilíneas enteras dentro de la zona de la planta.";

    private static readonly System.Globalization.CultureInfo Es = System.Globalization.CultureInfo.GetCultureInfo("es-AR");

    private DrawingContents _drawingContents = new([], []);
    private EffectDisplay? _effectShown;

    public SimulationSession Session { get; } = session;

    public SimulationSettings Settings
    {
        get
        {
            RadioSettings r = Session.Settings;
            return new SimulationSettings(r.Band, r.TxPowerDbm, r.TxAntennaGainDbi, r.RxAntennaGainDbi, r.TxCableLossDb, r.RxCableLossDb,
                Session.CellSizeM, Session.OverlayFadePercent, Session.Model, Session.MaxDiffractions);
        }
    }

    public IReadOnlyList<PropagationModel> PropagationModels => PropagationModelInfo.Available;

    public AccessPointInfo? AccessPoint => Session.AccessPoint is { } ap
        ? new AccessPointInfo(ap.Name, ap.Position, Session.Settings.Band, Session.Settings.FrequencyMHz, Session.Settings.TxPowerDbm)
        : null;

    public IReadOnlyList<Material> Materials => [.. Session.Catalog.All];

    public bool HasResult => Session.LastGrid is not null;

    public abstract string SuggestedExportPath { get; }

    public CalculationSummary? LastCalculation { get; private set; }

    public Legend? ReinforcementEffectLegend => _effectShown?.Legend;

    public DrawingSetup GetDrawingSetup() => ReadSetup();

    public OperationResult<DrawingUnit> SetDrawingUnit(DrawingUnit unit)
    {
        if (!Enum.IsDefined(unit)) return OperationResult<DrawingUnit>.Fail("Unidad desconocida.");

        WriteUnit(unit);
        LoadWalls();
        return OperationResult<DrawingUnit>.Ok(unit);
    }

    /// <summary>Si el dibujo no cambió desde la lectura anterior, el último cálculo sigue vigente.</summary>
    public IReadOnlyList<WallInfo> LoadWalls()
    {
        DrawingContents contents = ReadDrawing();
        if (Session.Validation is null || !SameContents(_drawingContents, contents))
        {
            Session.LoadWalls([.. contents.Walls.Select(w => w.Wall)], contents.Issues);
        }

        _drawingContents = contents;
        return [.. contents.Walls.Select(w => new WallInfo(w.Wall.Id, w.Wall.Segments.Sum(s => s.Length), w.Layer, w.Wall.MaterialId, w.Wall.ThicknessM, w.Wall.Reinforcement, w.Wall.IsColumn))];
    }

    public ValidationSummary Validate()
    {
        LoadWalls();
        return Summary();
    }

    public OperationResult<int> AssignMaterial(IReadOnlyCollection<string> handles, string materialId, double? thicknessM)
    {
        if (AssignmentError(handles, materialId, thicknessM, out Material material) is { } error) return OperationResult<int>.Fail(error);

        LoadWalls();
        var existing = _drawingContents.Walls.ToDictionary(w => w.Wall.Id, w => w.Wall.ThicknessM, StringComparer.OrdinalIgnoreCase);
        List<WallAssignment> assignments = [.. handles.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(h => new WallAssignment(h, material.Id, thicknessM ?? existing.GetValueOrDefault(h) ?? DefaultThicknessM))];

        IReadOnlyList<string> notFound = WriteWalls(assignments);
        LoadWalls();
        int updated = assignments.Count - notFound.Count;
        IReadOnlyList<string> warnings = notFound.Count == 0 ? [] : [$"No se encontraron en el dibujo: {string.Join(", ", notFound)}."];
        return updated == 0
            ? new OperationResult<int>(0, ["Ninguna de las entidades elegidas existe en el dibujo."], warnings)
            : OperationResult<int>.Ok(updated, warnings);
    }

    public FacePairSurvey FindFacePairs(IReadOnlyCollection<string> handles)
    {
        if (handles.Count == 0 || ReadSetup().Unit is null) return FacePairSurvey.None;

        FacePairs pairs = PairsAmongNewEntities(ReadEntityFaces(Distinct(handles)));
        return pairs.Axes.Count == 0
            ? FacePairSurvey.None
            : new FacePairSurvey(pairs.Axes.Count, pairs.Paired, pairs.Axes.Min(a => a.ThicknessM), pairs.Axes.Max(a => a.ThicknessM));
    }

    public OperationResult<FaceJoin> AssignMaterialJoiningFaces(IReadOnlyCollection<string> handles, string materialId, double? thicknessM)
    {
        if (AssignmentError(handles, materialId, thicknessM, out Material material) is { } error) return OperationResult<FaceJoin>.Fail(error);

        LoadWalls();
        string[] chosen = Distinct(handles);
        FacePairs pairs = ReadSetup().Unit is null ? new FacePairs([], [], [], []) : PairsAmongNewEntities(ReadEntityFaces(chosen));
        var faces = new HashSet<string>(pairs.Paired, StringComparer.OrdinalIgnoreCase);

        var create = new List<NewAxis>();
        var assignments = new List<WallAssignment>();
        var keptAxes = new List<string>();
        foreach (WallAxis axis in pairs.Axes)
        {
            double thickness = Math.Round(axis.ThicknessM, 3);
            if (ExistingAxis(axis.Axis, []) is { } existing)
            {
                keptAxes.Add(existing);
                assignments.Add(new WallAssignment(existing, material.Id, thickness));
            }
            else
            {
                create.Add(new NewAxis(axis.Axis, thickness, material.Id));
            }
        }

        var existingThickness = _drawingContents.Walls.ToDictionary(w => w.Wall.Id, w => w.Wall.ThicknessM, StringComparer.OrdinalIgnoreCase);
        List<WallAssignment> singles = [.. chosen
            .Where(h => !faces.Contains(h) && !keptAxes.Contains(h, StringComparer.OrdinalIgnoreCase))
            .Select(h => new WallAssignment(h, material.Id, thicknessM ?? existingThickness.GetValueOrDefault(h) ?? DefaultThicknessM))];
        assignments.AddRange(singles);

        IReadOnlyList<string> created = create.Count == 0 ? [] : CreateAxes(create);
        IReadOnlyList<string> notFound = assignments.Count == 0 ? [] : WriteWalls(assignments);
        LoadWalls();

        int singleWalls = singles.Count(s => !notFound.Contains(s.Handle, StringComparer.OrdinalIgnoreCase));
        var warnings = new List<string>();
        if (notFound.Count > 0) warnings.Add($"No se encontraron en el dibujo: {string.Join(", ", notFound)}.");
        if (pairs.WithLeftover.Count > 0)
        {
            warnings.Add($"Con tramos sin par de más de 0,5 m, que no quedan como muro: {string.Join(", ", pairs.WithLeftover)}.");
        }

        var result = new FaceJoin(
            created.Count, keptAxes.Count, faces.Count, singleWalls,
            pairs.Axes.Count == 0 ? 0 : pairs.Axes.Min(a => a.ThicknessM),
            pairs.Axes.Count == 0 ? 0 : pairs.Axes.Max(a => a.ThicknessM),
            [.. created, .. keptAxes], pairs.WithLeftover);
        return result.WallCount == 0
            ? new OperationResult<FaceJoin>(result, ["Ninguna de las entidades elegidas existe en el dibujo."], warnings)
            : OperationResult<FaceJoin>.Ok(result, warnings);
    }

    public ColumnReinforcement ColumnReinforcement => Session.Catalog.Columns;

    public OperationResult<ColumnGeneration> AssignColumns(IReadOnlyCollection<string> handles, Reinforcement reinforcement)
    {
        const string unchanged = "No se generó ninguna columna.";
        if (handles.Count == 0) return OperationResult<ColumnGeneration>.Fail("No se eligió ninguna figura.");
        if (!Enum.IsDefined(reinforcement)) return OperationResult<ColumnGeneration>.Fail($"Nivel de armadura desconocido. {unchanged}");
        if (ReadSetup().Unit is null) return OperationResult<ColumnGeneration>.Fail(MissingUnit);

        ColumnReinforcement columns = Session.Catalog.Columns;
        if (!Session.Catalog.TryGet(columns.MaterialId, out Material material))
        {
            return OperationResult<ColumnGeneration>.Fail($"El catálogo no tiene el material de las columnas ('{columns.MaterialId}'). {unchanged}");
        }

        LoadWalls();
        string[] chosen = Distinct(handles);
        IReadOnlyList<ClosedFigure> figures = ReadClosedFigures(chosen);
        List<(string Handle, Polygon2 Footprint)> existing = ExistingColumns();

        var create = new List<NewColumn>();
        var update = new List<ColumnUpdate>();
        var accepted = new List<Polygon2>();
        var tooLarge = new List<string>();
        var invalid = new List<string>();
        int kept = 0, duplicates = 0;
        foreach (ClosedFigure figure in figures)
        {
            if (figure.IsColumn)
            {
                if (Touch(figure.Handle)) kept++;
                continue;
            }

            if (figure.Polygon is not { } polygon)
            {
                invalid.Add($"{figure.Handle} ({figure.Kind}: {figure.Error ?? "no es una figura cerrada"})");
                continue;
            }

            if (!ColumnRules.IsSmall(polygon))
            {
                tooLarge.Add(figure.Handle);
                continue;
            }

            if (accepted.Any(p => ColumnRules.SameFootprint(p, polygon)))
            {
                duplicates++;
                continue;
            }

            accepted.Add(polygon);
            string? match = existing.FirstOrDefault(c => ColumnRules.SameFootprint(c.Footprint, polygon)).Handle;
            if (match is null) create.Add(new NewColumn(polygon, material.Id, reinforcement));
            else if (Touch(match)) kept++;
            else duplicates++;
        }

        var warnings = new List<string>();
        string[] notFound = [.. chosen.Where(h => !figures.Any(f => string.Equals(f.Handle, h, StringComparison.OrdinalIgnoreCase)))];
        if (notFound.Length > 0) warnings.Add($"No se encontraron en el dibujo: {string.Join(", ", notFound)}.");
        if (tooLarge.Count > 0)
        {
            warnings.Add(string.Create(Es, $"No parece una columna (más de {ColumnRules.MaxSideM:0.#} m de lado), no se generó: {string.Join(", ", tooLarge.Distinct())}."));
        }

        if (invalid.Count > 0) warnings.Add($"{ValidationIssue.InvalidGeometry}: {string.Join("; ", invalid.Distinct())}.");

        if (create.Count == 0 && update.Count == 0)
        {
            return new OperationResult<ColumnGeneration>(null, [.. warnings, unchanged], []);
        }

        IReadOnlyList<string> created = WriteColumns(create, update);
        LoadWalls();
        return OperationResult<ColumnGeneration>.Ok(new ColumnGeneration(
            created.Count, kept, duplicates, reinforcement, columns.AdditionalLossDb(reinforcement),
            [.. created, .. update.Select(u => u.Handle)], tooLarge, invalid), warnings);

        bool Touch(string handle)
        {
            if (update.Any(u => string.Equals(u.Handle, handle, StringComparison.OrdinalIgnoreCase))) return false;
            update.Add(new ColumnUpdate(handle, material.Id, reinforcement));
            return true;
        }
    }

    public IReadOnlyList<ParallelWallsInfo> FindParallelWalls()
    {
        LoadWalls();
        return [.. ParallelWalls.Find(Session.Walls).Select(g => new ParallelWallsInfo(g.WallIds, g.SeparationM, g.Convertible))];
    }

    public OperationResult<ParallelConversion> ConvertParallelWalls()
    {
        LoadWalls();
        IReadOnlyList<ParallelWallGroup> groups = ParallelWalls.Find(Session.Walls);
        if (groups.Count == 0)
        {
            return OperationResult<ParallelConversion>.Ok(new ParallelConversion(0, 0, [], []), ["No hay muros paralelos a menos de 0,50 m; el dibujo no se modificó."]);
        }

        var byId = Session.Walls.ToDictionary(w => w.Id, StringComparer.OrdinalIgnoreCase);
        var retire = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var create = new List<NewAxis>();
        foreach (ParallelWallGroup group in groups.Where(g => g.Convertible))
        {
            retire.UnionWith(group.WallIds);
            if (ExistingAxis(group.Axis, group.WallIds) is null)
            {
                create.Add(new NewAxis(group.Axis, Math.Round(group.SeparationM, 3), StrongestMaterial(group.WallIds.Select(id => byId[id].MaterialId))));
            }
        }

        string[] skipped = [.. groups.Where(g => !g.Convertible).SelectMany(g => g.WallIds).Where(id => !retire.Contains(id)).Distinct(StringComparer.OrdinalIgnoreCase)];
        IReadOnlyList<string> warnings = skipped.Length == 0
            ? []
            : [$"Sin convertir, porque tienen tramos sin par de más de 0,5 m: {string.Join(", ", skipped)}. Corregirlos a mano."];
        if (retire.Count == 0) return OperationResult<ParallelConversion>.Ok(new ParallelConversion(0, 0, [], skipped), warnings);

        IReadOnlyList<string> created = create.Count == 0 ? [] : CreateAxes(create);
        RetireFaces([.. retire]);
        LoadWalls();
        return OperationResult<ParallelConversion>.Ok(new ParallelConversion(created.Count, retire.Count, created, skipped), warnings);
    }

    public OperationResult<AxisSurvey> SurveyAxisLayers(BoundingBox windowM, double mergeGapM = AxisDefaults.MergeGapM)
    {
        if (AxisPrecondition(mergeGapM) is { } error) return OperationResult<AxisSurvey>.Fail(error);

        IReadOnlyList<LayerFaces> faces = ReadFaces(windowM);
        if (faces.Count == 0) return OperationResult<AxisSurvey>.Fail(NoFacesInWindow);

        var options = new AxisDetectionOptions { MaxMergeGapM = mergeGapM };
        var counted = faces
            .Select(l => (l.Layer, Segments: l.Faces.Count, Axes: WallAxisDetector.Detect(l.Faces, options).Axes.Count, ExplicitOnly: AxisDefaults.IsExplicitOnly(l.Layer)))
            .ToList();

        string? suggested = counted.FirstOrDefault(c => string.Equals(c.Layer, AxisDefaults.PreferredLayer, StringComparison.OrdinalIgnoreCase)).Layer
            ?? counted.Where(c => !c.ExplicitOnly && c.Axes > 0)
                .OrderByDescending(c => c.Layer.Contains("muro", StringComparison.OrdinalIgnoreCase) || c.Layer.Contains("wall", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(c => c.Axes)
                .FirstOrDefault().Layer;

        List<AxisLayerInfo> layers = [.. counted
            .OrderByDescending(c => c.Layer == suggested)
            .ThenBy(c => c.ExplicitOnly)
            .ThenByDescending(c => c.Axes)
            .ThenByDescending(c => c.Segments)
            .ThenBy(c => c.Layer, StringComparer.OrdinalIgnoreCase)
            .Select(c => new AxisLayerInfo(c.Layer, c.Segments, c.Axes, c.Layer == suggested, c.ExplicitOnly))];
        return OperationResult<AxisSurvey>.Ok(new AxisSurvey(windowM, layers));
    }

    public OperationResult<AxisGeneration> GenerateAxes(BoundingBox windowM, IReadOnlyCollection<string> layers, double mergeGapM = AxisDefaults.MergeGapM)
    {
        if (AxisPrecondition(mergeGapM) is { } error) return OperationResult<AxisGeneration>.Fail(error);

        string[] wanted = [.. layers.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (wanted.Length == 0) return OperationResult<AxisGeneration>.Fail("No se eligió ninguna capa. No se creó ningún eje.");

        IReadOnlyList<LayerFaces> faces = ReadFaces(windowM);
        if (faces.Count == 0) return OperationResult<AxisGeneration>.Fail(NoFacesInWindow);

        var byLayer = faces.ToDictionary(l => l.Layer, l => l.Faces, StringComparer.OrdinalIgnoreCase);
        string[] missing = [.. wanted.Where(l => !byLayer.ContainsKey(l))];
        if (missing.Length > 0)
        {
            return OperationResult<AxisGeneration>.Fail($"Sin segmentos dentro de la zona en la capa '{string.Join("', '", missing)}'. No se creó ningún eje.");
        }

        List<FaceSegment> chosen = [.. wanted.SelectMany(l => byLayer[l])];
        AxisDetection detection = WallAxisDetector.Detect(chosen, new AxisDetectionOptions { MaxMergeGapM = mergeGapM });
        if (detection.Axes.Count == 0)
        {
            return OperationResult<AxisGeneration>.Ok(
                new AxisGeneration(chosen.Count, 0, 0, 0, 0, 0, 0, mergeGapM, detection.Unpaired, detection.PartlyUnpaired)
                {
                    SmallClosedFigures = PossibleColumns(ReadClosedFigures(windowM)),
                },
                ["No se encontró ningún par de caras paralelas; el dibujo no se modificó."]);
        }

        List<GeneratedAxis> previous = [.. ReadGeneratedAxes(windowM.Expand(SameAxisToleranceM))];
        var create = new List<NewAxis>();
        int kept = 0;
        foreach (WallAxis axis in detection.Axes)
        {
            int match = previous.FindIndex(p => SameAxis(p.Axis, axis.Axis));
            if (match >= 0)
            {
                previous.RemoveAt(match);
                kept++;
            }
            else
            {
                create.Add(new NewAxis(axis.Axis, Math.Round(axis.ThicknessM, 3)));
            }
        }

        ReplaceAxes([.. previous.Select(p => p.Handle)], create);
        LoadWalls();
        return OperationResult<AxisGeneration>.Ok(new AxisGeneration(
            chosen.Count, detection.Axes.Count, create.Count, kept, previous.Count,
            detection.Axes.Min(a => a.ThicknessM), detection.Axes.Max(a => a.ThicknessM), mergeGapM,
            detection.Unpaired, detection.PartlyUnpaired) { SmallClosedFigures = PossibleColumns(ReadClosedFigures(windowM)) });
    }

    public OperationResult<AxisGeneration> GenerateAxesFromSelection(IReadOnlyCollection<string> handles, double mergeGapM = AxisDefaults.MergeGapM)
    {
        if (AxisPrecondition(mergeGapM) is { } error) return OperationResult<AxisGeneration>.Fail(error);
        if (handles.Count == 0) return OperationResult<AxisGeneration>.Fail("No se eligió ninguna línea. No se creó ningún eje.");

        LoadWalls();
        List<EntityFaces> candidates = [.. ReadEntityFaces(Distinct(handles)).Where(e => !e.IsWall)];
        List<EntityFaces> faces = [.. candidates.Where(e => ColumnRules.SmallClosedFigure(e.Segments, e.Closed) is null)];
        if (faces.Count == 0)
        {
            return OperationResult<AxisGeneration>.Fail("La selección no tiene líneas ni polilíneas que puedan ser caras de muro. No se creó ningún eje.");
        }

        FacePairs pairs = FacePairing.Detect(
            [.. faces.Select(e => new FaceEntity(e.Handle, e.Segments))],
            new AxisDetectionOptions { MaxMergeGapM = mergeGapM });
        int segments = faces.Sum(e => e.Segments.Count);
        string[] small = [.. candidates.Except(faces).Select(e => e.Handle)];
        List<string> possibleColumns = small.Length == 0 ? [] : PossibleColumns(ReadClosedFigures(small));
        if (pairs.Axes.Count == 0)
        {
            return OperationResult<AxisGeneration>.Ok(
                new AxisGeneration(segments, 0, 0, 0, 0, 0, 0, mergeGapM, pairs.Single, []) { SmallClosedFigures = possibleColumns },
                ["No se encontró ningún par de caras paralelas entre las líneas elegidas; el dibujo no se modificó."]);
        }

        var create = new List<NewAxis>();
        int kept = 0;
        foreach (WallAxis axis in pairs.Axes)
        {
            if (ExistingAxis(axis.Axis, []) is null) create.Add(new NewAxis(axis.Axis, Math.Round(axis.ThicknessM, 3)));
            else kept++;
        }

        if (create.Count > 0) CreateAxes(create);
        LoadWalls();
        return OperationResult<AxisGeneration>.Ok(new AxisGeneration(
            segments, pairs.Axes.Count, create.Count, kept, 0,
            pairs.Axes.Min(a => a.ThicknessM), pairs.Axes.Max(a => a.ThicknessM), mergeGapM,
            pairs.Single, pairs.WithLeftover) { SmallClosedFigures = possibleColumns });
    }

    public OperationResult<AccessPointInfo> SetAccessPoint(Vec2 positionM)
    {
        if (!double.IsFinite(positionM.X) || !double.IsFinite(positionM.Y)) return OperationResult<AccessPointInfo>.Fail("La ubicación del AP no es válida.");

        Session.PlaceAccessPoint(positionM);
        if (SaveAccessPoint(Session.AccessPoint!, Session.Settings) is { } error) return OperationResult<AccessPointInfo>.Fail(error);
        return OperationResult<AccessPointInfo>.Ok(AccessPoint!);
    }

    public OperationResult<SimulationSettings> Configure(SimulationSettings settings)
    {
        if (!Enum.IsDefined(settings.Band)) return OperationResult<SimulationSettings>.Fail("Banda desconocida.");

        RadioSettings radio = Session.Settings with
        {
            Band = settings.Band,
            TxPowerDbm = settings.TxPowerDbm,
            TxAntennaGainDbi = settings.TxAntennaGainDbi,
            RxAntennaGainDbi = settings.RxAntennaGainDbi,
            TxCableLossDb = settings.TxCableLossDb,
            RxCableLossDb = settings.RxCableLossDb,
        };

        var errors = new List<string>();
        if (new[] { settings.TxPowerDbm, settings.TxAntennaGainDbi, settings.RxAntennaGainDbi, settings.TxCableLossDb, settings.RxCableLossDb }.Any(v => !double.IsFinite(v)))
        {
            errors.Add("Hay un valor que no es un número.");
        }

        errors.AddRange(radio.Validate());
        if (SimulationSession.ResolutionError(settings.CellSizeM) is { } resolution) errors.Add(resolution);
        if (OverlayFade.Error(settings.OverlayFadePercent) is { } fade) errors.Add(fade);
        if (!Enum.IsDefined(settings.Model))
        {
            errors.Add("Modelo de propagación desconocido.");
        }
        else if (!PropagationModelInfo.IsAvailable(settings.Model))
        {
            errors.Add($"El modelo «{PropagationModelInfo.Label(settings.Model)}» todavía no está disponible.");
        }

        if (!PropagationOptions.IsValidMaxDiffractions(settings.MaxDiffractions))
        {
            errors.Add($"Las difracciones máximas por trayecto van de {PropagationOptions.MinDiffractions} a {PropagationOptions.MaxDiffractionsLimit}.");
        }

        if (errors.Count > 0) return new OperationResult<SimulationSettings>(null, errors, []);

        bool radioChanged = radio != Session.Settings;
        Session.Configure(radio, settings.CellSizeM);
        Session.SetModel(settings.Model, settings.MaxDiffractions);
        Session.SetOverlayFade(settings.OverlayFadePercent);

        var warnings = new List<string>();
        if (radioChanged && Session.AccessPoint is { } ap && SaveAccessPoint(ap, Session.Settings) is { } error) warnings.Add(error);
        return OperationResult<SimulationSettings>.Ok(Settings, warnings);
    }

    public CoverageAreaInfo GetCoverageArea() => AreaInfo(ReadCoverageArea());

    public OperationResult<CoverageAreaInfo> SetCoverageArea(string polylineHandle)
    {
        if (string.IsNullOrWhiteSpace(polylineHandle)) return OperationResult<CoverageAreaInfo>.Fail("No se eligió ninguna polilínea.");
        return ChangeCoverageArea(polylineHandle.Trim());
    }

    public OperationResult<CoverageAreaInfo> ClearCoverageArea() => ChangeCoverageArea(null);

    public OperationResult<int> SetOverlayFade(int percent)
    {
        OperationResult<int> set = Session.SetOverlayFade(percent);
        if (!set.Succeeded) return set;

        string? warning = _effectShown is { } effect
            ? ApplyOverlayFade(percent, effect.Grid, effect.Legend)
            : ApplyOverlayFade(percent, Session.LastGrid, Legend.For(Session.Settings, Session.Model));
        return OperationResult<int>.Ok(percent, warning is null ? [] : [warning]);
    }

    public OperationResult<CalculationSummary> Calculate()
    {
        ValidationSummary validation = Validate();
        if (!validation.IsValid)
        {
            return new OperationResult<CalculationSummary>(null,
                [.. validation.Items.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.ToString()),
                 $"No se calcula: el plano tiene {validation.ErrorCount} error(es) de validación."], []);
        }

        CoverageAreaRead area = ReadCoverageArea();
        Session.SetCoverageArea(area.Polygon);
        var warnings = new List<string>();
        if (area.Warning is not null) warnings.Add(area.Warning);

        var watch = Stopwatch.StartNew();
        OperationResult<RssiGrid> result = Session.Calculate();
        watch.Stop();
        if (!result.Succeeded) return new OperationResult<CalculationSummary>(null, result.Errors, warnings);
        warnings.AddRange(result.Warnings);

        RssiGrid grid = result.Value!;
        var imageWatch = Stopwatch.StartNew();
        HeatmapImage image = Session.Heatmap()!;
        _effectShown = null;
        OverlayOutcome overlay = DrawResult(grid, image, Legend.For(Session.Settings, Session.Model), Session.OverlayFadePercent, OverlayImageKind.Coverage);
        imageWatch.Stop();
        warnings.AddRange(overlay.Warnings);

        GridSpec spec = grid.Spec;
        LastCalculation = new CalculationSummary(
            spec.CellCount, spec.Columns, spec.Rows, spec.CellSizeM, grid.ValueCount, grid.Min, grid.Max,
            watch.Elapsed.TotalMilliseconds, imageWatch.Elapsed.TotalMilliseconds,
            image.Width, image.Height, image.PixelSizeM, Session.OverlayFadePercent, overlay.ImagePath, area.Description, warnings, Session.Model);
        return OperationResult<CalculationSummary>.Ok(LastCalculation, warnings);
    }

    public OperationResult<ReinforcementEffectSummary> ShowReinforcementEffect()
    {
        ValidationSummary validation = Validate();
        if (!validation.IsValid)
        {
            return new OperationResult<ReinforcementEffectSummary>(null,
                [.. validation.Items.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.ToString()),
                 $"No se calcula: el plano tiene {validation.ErrorCount} error(es) de validación."], []);
        }

        CoverageAreaRead area = ReadCoverageArea();
        var warnings = new List<string>();
        if (area.Warning is not null) warnings.Add(area.Warning);

        OperationResult<ReinforcementEffectResult> result = Session.CalculateReinforcementEffect(area.Polygon);
        if (!result.Succeeded) return new OperationResult<ReinforcementEffectSummary>(null, result.Errors, warnings);
        warnings.AddRange(result.Warnings);

        ReinforcementEffectResult r = result.Value!;
        ReinforcementEffectMap? map = r.Map;
        if (map is null || !map.HasEffect)
        {
            return OperationResult<ReinforcementEffectSummary>.Ok(new ReinforcementEffectSummary(
                r.ReinforcedColumns, 0, 0, r.CellSizeM, Session.CellSizeM, r.ResolutionLowered, null, warnings, Session.Model), warnings);
        }

        if (r.ResolutionLowered) warnings.Add(LoweredResolutionWarning(Session.CellSizeM, r.CellSizeM));
        var scale = new EffectScale(map.MaxDb);
        Legend legend = Legend.ForReinforcementEffect(Session.Settings, scale, Session.Model);
        HeatmapImage image = HeatmapRasterizer.RenderEffect(map.Grid, scale);
        OverlayOutcome overlay = DrawResult(map.Grid, image, legend, Session.OverlayFadePercent, OverlayImageKind.ReinforcementEffect);
        warnings.AddRange(overlay.Warnings);
        if (overlay.ImagePath is not null) _effectShown = new EffectDisplay(map.Grid, legend);

        return OperationResult<ReinforcementEffectSummary>.Ok(new ReinforcementEffectSummary(
            r.ReinforcedColumns, map.AffectedCells, map.MaxDb, r.CellSizeM, Session.CellSizeM, r.ResolutionLowered, overlay.ImagePath, warnings, Session.Model), warnings);
    }

    private static string LoweredResolutionWarning(double sessionM, double usedM) => string.Create(Es,
        $"La resolución de la sesión ({sessionM:0.##} m) se bajó a {usedM:0.##} m solo para este mapa; la configuración de la sesión no cambió.");

    private sealed record EffectDisplay(RssiGrid Grid, Legend Legend);

    public OperationResult<QueryResult> Query(Vec2 pointM)
    {
        if (!double.IsFinite(pointM.X) || !double.IsFinite(pointM.Y)) return OperationResult<QueryResult>.Fail("El punto no es válido.");
        if (Session.Validation is null) LoadWalls();

        OperationResult<PointCalculation> result = Session.Query(pointM);
        if (!result.Succeeded) return new OperationResult<QueryResult>(null, result.Errors, []);

        PointCalculation c = result.Value!;
        return OperationResult<QueryResult>.Ok(new QueryResult(pointM, c.ReceivedPowerDbm, SignalScale.Classify(c.ReceivedPowerDbm), c, Explanation.Format(c)));
    }

    public OperationResult<string> ExportPng(string path)
    {
        if (Session.LastGrid is null || Session.AccessPoint is null) return OperationResult<string>.Fail("No hay un cálculo vigente. Ejecutar WIFICALCULATE.");
        if (string.IsNullOrWhiteSpace(path)) return OperationResult<string>.Fail("Falta la ruta del archivo.");

        string full;
        try
        {
            full = Path.GetFullPath(Path.HasExtension(path) ? path : path + ".png");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return OperationResult<string>.Fail($"La ruta '{path}' no es válida.");
        }

        if (!string.Equals(Path.GetExtension(full), ".png", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<string>.Fail("El archivo debe tener extensión .png.");
        }

        try
        {
            WritePng(full, Session.Walls, Session.Heatmap()!, Session.AccessPoint, Legend.For(Session.Settings, Session.Model), Session.OverlayFadePercent);
            return OperationResult<string>.Ok(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            return OperationResult<string>.Fail($"No se pudo guardar {full}: {ex.Message}");
        }
    }

    private ValidationSummary Summary()
    {
        ValidationReport report = Session.Validation!;
        var points = Session.Walls.SelectMany(w => w.Segments).SelectMany(s => new[] { s.A, s.B }).ToList();
        BoundingBox? bounds = points.Count > 0 ? BoundingBox.FromPoints(points) : null;
        return new ValidationSummary(Session.Walls.Count, report.ErrorCount, report.WarningCount, bounds,
            [.. report.Issues.Select(i => new ValidationItem(i.Severity, i.Code, i.EntityRef, i.Message))], Session.Walls.Count(w => w.IsColumn));
    }

    private OperationResult<CoverageAreaInfo> ChangeCoverageArea(string? handle)
    {
        if (ReadSetup().Unit is null) return OperationResult<CoverageAreaInfo>.Fail(MissingUnit);

        OperationResult<CoverageAreaRead> written = WriteCoverageArea(handle);
        if (!written.Succeeded) return new OperationResult<CoverageAreaInfo>(null, written.Errors, written.Warnings);

        Polygon2? polygon = written.Value!.Polygon;
        if (polygon is not null || Session.CoverageArea is not null) Session.SetCoverageArea(polygon);
        return OperationResult<CoverageAreaInfo>.Ok(AreaInfo(written.Value), written.Warnings);
    }

    private static CoverageAreaInfo AreaInfo(CoverageAreaRead read) => new(
        read.Polygon is not null, read.Description, read.Handle,
        read.Polygon?.Vertices.Count ?? 0, read.Polygon?.AreaM2 ?? 0, read.Polygon?.Bounds, read.Warning);

    private string? AssignmentError(IReadOnlyCollection<string> handles, string materialId, double? thicknessM, out Material material)
    {
        material = null!;
        if (handles.Count == 0) return "No se eligió ningún muro.";
        if (string.IsNullOrWhiteSpace(materialId) || !Session.Catalog.TryGet(materialId.Trim(), out material))
        {
            string ids = string.Join("/", Session.Catalog.All.Select(m => m.Id));
            return $"{ValidationIssue.UnknownMaterial}: '{materialId}' no está en el catálogo. Valores posibles: {ids}. No se modificó ningún muro.";
        }

        return thicknessM is { } t && (!double.IsFinite(t) || t <= 0)
            ? "El espesor debe ser un número mayor que 0 m. No se modificó ningún muro."
            : null;
    }

    private static string[] Distinct(IReadOnlyCollection<string> handles) => [.. handles.Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Las figuras cerradas pequeñas (posibles columnas) no se usan como caras.</summary>
    private static FacePairs PairsAmongNewEntities(IReadOnlyList<EntityFaces> entities) =>
        FacePairing.Detect([.. entities
            .Where(e => !e.IsWall && ColumnRules.SmallClosedFigure(e.Segments, e.Closed) is null)
            .Select(e => new FaceEntity(e.Handle, e.Segments))]);

    private List<(string Handle, Polygon2 Footprint)> ExistingColumns() =>
        [.. _drawingContents.Walls
            .Where(w => w.Wall.IsColumn)
            .Select(w => (w.Wall.Id, Footprint: w.Wall.Footprint()))
            .Where(c => c.Footprint is not null)
            .Select(c => (c.Id, c.Footprint!))];

    /// <summary>Un handle por cada contorno pequeño distinto que todavía no es una columna.</summary>
    private List<string> PossibleColumns(IEnumerable<ClosedFigure> figures)
    {
        List<(string Handle, Polygon2 Footprint)> existing = ExistingColumns();
        var seen = new List<Polygon2>();
        var handles = new List<string>();
        foreach (ClosedFigure figure in figures)
        {
            if (figure.IsColumn || figure.Polygon is not { } polygon || !ColumnRules.IsSmall(polygon)) continue;
            if (seen.Any(p => ColumnRules.SameFootprint(p, polygon))) continue;

            seen.Add(polygon);
            if (!existing.Any(c => ColumnRules.SameFootprint(c.Footprint, polygon)) && !handles.Contains(figure.Handle)) handles.Add(figure.Handle);
        }

        return handles;
    }

    /// <summary>Muro de un solo tramo que ya ocupa ese eje, sin contar los indicados.</summary>
    private string? ExistingAxis(Segment2 axis, IReadOnlyList<string> except) =>
        _drawingContents.Walls
            .FirstOrDefault(w => w.Wall.Segments.Count == 1 && !except.Contains(w.Wall.Id, StringComparer.OrdinalIgnoreCase) && SameAxis(w.Wall.Segments[0], axis))
            ?.Wall.Id;

    /// <summary>Entre los materiales de las caras, el de mayor pérdida; null si ninguna tiene uno del catálogo.</summary>
    private string? StrongestMaterial(IEnumerable<string?> materialIds)
    {
        Material? best = null;
        foreach (string? id in materialIds)
        {
            if (id is null || !Session.Catalog.TryGet(id, out Material material)) continue;
            if (best is null || material.LossDb2_4 > best.LossDb2_4 || (material.LossDb2_4 == best.LossDb2_4 && material.LossDb5 > best.LossDb5)) best = material;
        }

        return best?.Id;
    }

    private string? AxisPrecondition(double mergeGapM) =>
        ReadSetup().Unit is null ? MissingUnit : AxisDetectionOptions.MergeGapError(mergeGapM);

    private static bool SameAxis(Segment2 a, Segment2 b) =>
        (Vec2.Distance(a.A, b.A) <= SameAxisToleranceM && Vec2.Distance(a.B, b.B) <= SameAxisToleranceM) ||
        (Vec2.Distance(a.A, b.B) <= SameAxisToleranceM && Vec2.Distance(a.B, b.A) <= SameAxisToleranceM);

    private static bool SameContents(DrawingContents a, DrawingContents b) =>
        a.Issues.SequenceEqual(b.Issues) &&
        a.Walls.Count == b.Walls.Count &&
        a.Walls.Zip(b.Walls).All(p =>
            p.First.Layer == p.Second.Layer &&
            p.First.Wall.Id == p.Second.Wall.Id &&
            p.First.Wall.MaterialId == p.Second.Wall.MaterialId &&
            p.First.Wall.ThicknessM == p.Second.Wall.ThicknessM &&
            p.First.Wall.Kind == p.Second.Wall.Kind &&
            p.First.Wall.Reinforcement == p.Second.Wall.Reinforcement &&
            p.First.Wall.Segments.SequenceEqual(p.Second.Wall.Segments));

    protected abstract DrawingSetup ReadSetup();

    /// <summary>Guarda la unidad y crea capas y aplicación registrada si faltan.</summary>
    protected abstract void WriteUnit(DrawingUnit unit);

    /// <summary>Líneas y tramos rectos de polilíneas enteros dentro de la zona, por capa, fuera de las capas propias.</summary>
    protected abstract IReadOnlyList<LayerFaces> ReadFaces(BoundingBox windowM);

    /// <summary>Ejes generados antes (con marca) que están enteros dentro de la zona.</summary>
    protected abstract IReadOnlyList<GeneratedAxis> ReadGeneratedAxes(BoundingBox windowM);

    /// <summary>Borra los ejes indicados y crea los nuevos en la capa de muros, marcados como generados y sin material.</summary>
    protected abstract void ReplaceAxes(IReadOnlyList<string> eraseHandles, IReadOnlyList<NewAxis> create);

    /// <summary>Líneas y polilíneas con esos handles, con sus tramos rectos en metros. Las demás entidades se omiten.</summary>
    protected abstract IReadOnlyList<EntityFaces> ReadEntityFaces(IReadOnlyCollection<string> handles);

    /// <summary>Crea ejes en la capa de muros, con su espesor y su material si lo traen. Devuelve los handles nuevos, en el mismo orden.</summary>
    protected abstract IReadOnlyList<string> CreateAxes(IReadOnlyList<NewAxis> axes);

    /// <summary>Quita la condición de muro a esas entidades y las pasa a la capa neutra de caras.</summary>
    protected abstract void RetireFaces(IReadOnlyList<string> handles);

    /// <summary>
    /// Hatches, polilíneas cerradas y columnas con esos handles, con su contorno en metros.
    /// Un hatch con varios contornos separados da una figura por contorno, todas con su handle.
    /// Un hatch con arcos o con islas, o una entidad de otro tipo, vuelve sin contorno y con el motivo.
    /// </summary>
    protected abstract IReadOnlyList<ClosedFigure> ReadClosedFigures(IReadOnlyCollection<string> handles);

    /// <summary>Hatches y polilíneas cerradas enteros dentro de la zona, fuera de las capas propias.</summary>
    protected abstract IReadOnlyList<ClosedFigure> ReadClosedFigures(BoundingBox windowM);

    /// <summary>
    /// Crea una polilínea cerrada por columna en la capa de muros, con material, armadura y marca de generada,
    /// y actualiza las columnas indicadas. Devuelve los handles nuevos, en el mismo orden.
    /// </summary>
    protected abstract IReadOnlyList<string> WriteColumns(IReadOnlyList<NewColumn> create, IReadOnlyList<ColumnUpdate> update);

    /// <summary>Guarda la polilínea como área de cobertura (null la quita) y devuelve lo guardado, o el motivo del rechazo.</summary>
    protected abstract OperationResult<CoverageAreaRead> WriteCoverageArea(string? polylineHandle);

    /// <summary>Aplica la atenuación al mapa dibujado y, si hay cálculo vigente, rehace la leyenda. Devuelve un aviso si no se pudo.</summary>
    protected abstract string? ApplyOverlayFade(int percent, RssiGrid? grid, Legend legend);

    /// <summary>Muros de la capa de simulación (en metros) y problemas de lectura, como falta de unidad.</summary>
    protected abstract DrawingContents ReadDrawing();

    /// <summary>Área de cobertura guardada; Polygon null = rectángulo de los muros.</summary>
    protected abstract CoverageAreaRead ReadCoverageArea();

    /// <summary>Escribe material y espesor y pasa las entidades a la capa de muros. Devuelve los handles que no existen.</summary>
    protected abstract IReadOnlyList<string> WriteWalls(IReadOnlyList<WallAssignment> assignments);

    /// <summary>Dibuja el marcador del AP con su banda y potencia. Devuelve un mensaje si no se pudo.</summary>
    protected abstract string? SaveAccessPoint(AccessPoint ap, RadioSettings settings);

    /// <summary>Reemplaza mapa y leyenda del dibujo. Kind define el nombre del archivo de la imagen.</summary>
    protected abstract OverlayOutcome DrawResult(RssiGrid grid, HeatmapImage image, Legend legend, int fadePercent, OverlayImageKind kind);

    protected abstract void WritePng(string path, IReadOnlyList<Wall> walls, HeatmapImage image, AccessPoint ap, Legend legend, int fadePercent);
}
