using System.Reflection;
using Xunit;

namespace WifiHeat.Core.Tests.Architecture;

public class CoreDependencyTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "acmgd", "acdbmgd", "accoremgd", "Autodesk.",
        "PresentationFramework", "PresentationCore", "WindowsBase",
        "System.Windows.Forms",
    ];

    [Fact]
    public void Core_does_not_reference_AutoCAD_or_UI_assemblies()
    {
        Assembly core = Assembly.Load("WifiHeat.Core");

        string[] violations = core.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.Empty(violations);
    }
}
