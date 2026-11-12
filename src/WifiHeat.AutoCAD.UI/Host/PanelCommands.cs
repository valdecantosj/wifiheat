using Autodesk.AutoCAD.Runtime;
using WifiHeat.AutoCAD.Commands;

namespace WifiHeat.AutoCAD.UI.Host;

// Sin atributos de ensamblado de Autodesk: la vista y los ViewModels de esta DLL también se cargan fuera de AutoCAD.
public sealed class PanelCommands
{
    /// <summary>Abre el panel acoplable de WifiHeat.</summary>
    [CommandMethod("WIFIPANEL")]
    public void ShowPanel() => CommandGuard.Run("WIFIPANEL", _ => PanelHost.Show());
}
