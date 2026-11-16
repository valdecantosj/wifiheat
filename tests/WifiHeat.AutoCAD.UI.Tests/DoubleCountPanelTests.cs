using WifiHeat.AutoCAD.UI.ViewModels;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Tests.Service;
using Xunit;

namespace WifiHeat.AutoCAD.UI.Tests;

public class DoubleCountPanelTests
{
    private static async Task<Sample> PickedFacesAsync(params string[] extra)
    {
        Sample s = Sample.Custom(d =>
        {
            d.AddLine("muros", "F1", 5, 0, 5, 8);
            d.AddLine("muros", "F2", 5.2, 0, 5.2, 8);
            d.AddLine("muros", "L", 0, 0, 0, 8);
        });
        s.Interaction.Entities = ["F1", "F2", .. extra];
        await s.Panel.Walls.PickInDrawingAsync();
        s.Panel.Walls.BulkMaterial = s.Panel.Materials.Single(m => m.Id == "brick");
        return s;
    }

    private static Sample AssignedFaces() => Sample.Custom(d =>
    {
        d.Add("F1", 5, 0, 5, 8, "brick", 0.15);
        d.Add("F2", 5.2, 0, 5.2, 8, "brick", 0.15);
    });

    private static void Run(Sample s, string label) =>
        s.Panel.Messages.Current!.Actions.Single(a => a.Label == label).Command.Execute(null);

    [Fact]
    public async Task Assigning_a_pair_of_faces_asks_before_writing()
    {
        Sample s = await PickedFacesAsync();

        s.Panel.Walls.Assign();

        PanelMessage message = s.Panel.Messages.Current!;
        Assert.Equal(StatusKind.Warning, message.Kind);
        Assert.Equal("La selección tiene 1 par de caras de un mismo muro", message.Title);
        Assert.Equal(["Unir en ejes", "Ignorar"], message.Actions.Select(a => a.Label));
        Assert.Empty(s.Drawing.Walls);
        Assert.Equal(2, s.Panel.Walls.ExtraSelectionCount);
    }

    [Fact]
    public async Task Joining_creates_one_axis_and_leaves_the_faces_in_their_layer()
    {
        Sample s = await PickedFacesAsync("L");
        s.Panel.Walls.Assign();

        Run(s, "Unir en ejes");

        Assert.Equal(2, s.Drawing.Walls.Count);
        Wall axis = s.Drawing.Walls.Values.Single(w => w.Id != "L");
        Assert.Equal("brick", axis.MaterialId);
        Assert.Equal(0.2, axis.ThicknessM!.Value, 9);
        Assert.Equal("muros", s.Drawing.Lines["F1"].Layer);
        Assert.Equal(0, s.Panel.Walls.ExtraSelectionCount);
        Assert.True(s.Row(axis.Id).IsSelected);
        Assert.True(s.Row("L").IsSelected);
        Assert.Equal(StatusKind.Success, s.Panel.Messages.Kind);
        Assert.Equal("1 eje(s) y 1 muro(s) sin par con Ladrillo", s.Panel.Messages.Current!.Title);
        Assert.Contains("Espesor medido: 0,2 m", s.Panel.Messages.Current.Detail);
        Assert.False(s.Panel.Validation.HasDoubleCount);
    }

    [Fact]
    public async Task Ignoring_assigns_each_face_and_validation_warns()
    {
        Sample s = await PickedFacesAsync();
        s.Panel.Walls.Assign();

        Run(s, "Ignorar");

        Assert.Equal(["F1", "F2"], s.Drawing.Walls.Keys.Order());
        Assert.Equal("2 muro(s) con Ladrillo", s.Panel.Messages.Current!.Title);
        Assert.True(s.Panel.Validation.HasDoubleCount);
        Assert.Equal(1, s.Panel.Validation.DoubleCountGroups);
    }

    [Fact]
    public async Task Assigning_without_pairs_does_not_ask()
    {
        Sample s = Sample.Custom(d => d.AddLine("0", "L", 0, 0, 0, 8));
        s.Interaction.Entities = ["L"];
        await s.Panel.Walls.PickInDrawingAsync();
        s.Panel.Walls.BulkMaterial = s.Panel.Materials.Single(m => m.Id == "brick");

        s.Panel.Walls.Assign();

        Assert.Empty(s.Panel.Messages.Current!.Actions);
        Assert.Equal("brick", s.Drawing.Walls["L"].MaterialId);
    }

    [Fact]
    public async Task The_question_goes_away_when_another_action_starts()
    {
        Sample s = await PickedFacesAsync();
        s.Panel.Walls.Assign();

        s.Panel.Walls.ClearSelection();
        s.Panel.Validation.Validate();

        Assert.Empty(s.Panel.Messages.Current!.Actions);
        Assert.Empty(s.Drawing.Walls);
    }

    [Fact]
    public void Validation_counts_the_groups_of_parallel_walls()
    {
        Sample s = AssignedFaces();

        Assert.True(s.Panel.Validation.HasDoubleCount);
        Assert.StartsWith("Hay 1 grupo de muros paralelos a menos de 0,50 m", s.Panel.Validation.DoubleCountText);
        Assert.True(s.Panel.Validation.IsValid);
        Assert.Equal(1, s.Panel.Validation.WarningCount);
    }

    [Fact]
    public void Validate_offers_to_convert_parallel_walls()
    {
        Sample s = AssignedFaces();

        s.Panel.Validation.Validate();

        Assert.Equal(StatusKind.Warning, s.Panel.Messages.Kind);
        Assert.Equal(["Convertir en ejes"], s.Panel.Messages.Current!.Actions.Select(a => a.Label));
        Assert.Equal(2, s.Drawing.Walls.Count);
    }

    [Fact]
    public void Converting_from_the_message_leaves_one_axis()
    {
        Sample s = AssignedFaces();
        s.Panel.Validation.Validate();

        Run(s, "Convertir en ejes");

        Wall axis = Assert.Single(s.Drawing.Walls.Values);
        Assert.Equal("brick", axis.MaterialId);
        Assert.Equal(FakeDrawing.FaceLayer, s.Drawing.Lines["F1"].Layer);
        Assert.False(s.Panel.Validation.HasDoubleCount);
        Assert.Equal(1, s.Panel.Walls.Rows.Count);
        Assert.Equal(StatusKind.Success, s.Panel.Messages.Kind);
        Assert.Equal("1 eje creado en lugar de las dos caras", s.Panel.Messages.Current!.Title);
    }

    [Fact]
    public void Converting_walls_with_unpaired_stretches_explains_why_nothing_changed()
    {
        Sample s = Sample.Custom(d =>
        {
            d.Add("LONG", 0, 0, 10, 0, "brick", 0.15);
            d.Add("SHORT", 0, 0.2, 3, 0.2, "brick", 0.15);
        });

        s.Panel.Validation.ConvertParallel();

        Assert.Equal(StatusKind.Warning, s.Panel.Messages.Kind);
        Assert.Equal("No se convirtió ningún muro", s.Panel.Messages.Current!.Title);
        Assert.Contains("LONG", s.Panel.Messages.Current.Detail);
        Assert.Equal(2, s.Drawing.Walls.Count);
    }

    [Fact]
    public void A_message_keeps_its_actions_only_while_it_is_current()
    {
        var center = new MessageCenter();
        int runs = 0;

        center.Post(StatusKind.Warning, "Pregunta", "", [new MessageAction("Hacer", new RelayCommand(() => runs++))]);
        center.Current!.Actions[0].Command.Execute(null);

        Assert.True(center.Current.HasActions);
        Assert.Equal(1, runs);
        Assert.Equal("Pregunta", center.History[0].Title);

        center.BeginAction();
        Assert.Null(center.Current);
        center.Post(StatusKind.Info, "Otro");
        Assert.False(center.Current!.HasActions);
    }
}
