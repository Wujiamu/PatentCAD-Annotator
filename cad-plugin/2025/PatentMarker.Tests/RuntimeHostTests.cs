using Autodesk.AutoCAD.ApplicationServices;
using PatentMarker.IO;
using Xunit;

namespace PatentMarker.Tests;

public sealed class RuntimeHostTests
{
    [Fact]
    public void ActiveDocument_UsesInjectedHostDocument()
    {
        var expected = new Document { Name = "C:\\drawings\\host-test.dwg" };
        RuntimeHost.SetActiveDocumentOverride(() => expected);

        try
        {
            Assert.Same(expected, RuntimeHost.ActiveDocument);
        }
        finally
        {
            RuntimeHost.ClearActiveDocumentOverride();
        }
    }

    [Fact]
    public void ClearOverride_ReturnsToApplicationHost()
    {
        RuntimeHost.SetActiveDocumentOverride(() => new Document());
        RuntimeHost.ClearActiveDocumentOverride();

        Assert.Null(RuntimeHost.ActiveDocument);
    }
}
