using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(WifiHeat.AutoCAD.WifiHeatApplication))]

namespace WifiHeat.AutoCAD;

/// <summary>Punto de entrada que AutoCAD llama al cargar la DLL (NETLOAD).</summary>
public sealed class WifiHeatApplication : IExtensionApplication
{
    public const string Version = "0.1.0-dev";

    public void Initialize()
    {
        Sessions.Attach();
        CoreApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
            $"\nWifiHeat {Version} cargado. Escriba WIFI para ver los comandos.\n");
    }

    public void Terminate()
    {
        Sessions.Detach();
    }
}
