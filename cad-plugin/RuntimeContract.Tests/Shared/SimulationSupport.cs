using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace PatentMarker
{
    internal static class PatentMarkerApp
    {
        internal static void RawLog(string message) { }
    }
}

namespace PatentMarker.Palette
{
    internal static class PatPaletteCommand
    {
        public static string PendingNumber;
        public static string PendingName;
    }
}

namespace PatentMarker.Styles
{
    internal static class PatStyleInitializer
    {
        internal static void EnsurePatDimStyle() { }
        internal static void EnsurePatStyle() { }
        internal static ObjectId GetPatDimStyleId(Database db, Transaction tr) { return ObjectId.Null; }
        internal static ObjectId GetOrCreateTimesRoman(Database db, Transaction tr) { return new ObjectId(500); }
        internal static ObjectId GetPatStyleId(Database db, Transaction tr) { return new ObjectId(501); }
    }
}

namespace PatentMarker.IO
{
    public sealed class PatConfig
    {
        public PatStyleConfig PatStyle { get; set; } = new PatStyleConfig();
        public AlignConfig Align { get; set; } = new AlignConfig();
    }

    public sealed class PatStyleConfig
    {
        public double TextHeight { get; set; } = 3.5;
    }

    public sealed class AlignConfig
    {
        public double MarginToFrame { get; set; } = 5.0;
    }
}

namespace PatentMarker.RuntimeContractTests
{
    internal sealed class SimulationFixture : System.IDisposable
    {
        public SimulationFixture()
        {
            Editor = new Editor();
            Database = new Database();
            Document = new Document { Editor = Editor, Database = Database, Name = "C:\\sim\\drawing-a.dwg" };
            IO.RuntimeHost.SetActiveDocumentOverride(delegate { return Document; });
            Palette.PatPaletteCommand.PendingNumber = null;
            Palette.PatPaletteCommand.PendingName = null;
            IO.PatSettingsStore.Activate(Document.Name);
            IO.PatSettingsStore.ResetConfigDefaults();
            IO.PatSettingsStore.Current.HasArrowHead = false;
            IO.PatSettingsStore.Current.ArrowSize = IO.PatSettingsStore.DefaultArrowSize;
            IO.PatSettingsStore.Current.IsSplined = true;
            IO.PatSettingsStore.Current.ThreePointMode = false;
        }

        public Document Document { get; private set; }
        public Editor Editor { get; private set; }
        public Database Database { get; private set; }

        public void Dispose()
        {
            IO.RuntimeHost.ClearActiveDocumentOverride();
            IO.PatSettingsStore.Release(Document.Name);
            Palette.PatPaletteCommand.PendingNumber = null;
            Palette.PatPaletteCommand.PendingName = null;
        }

        public void QueueThreePointAnnotation(string number, Point3d attach, Point3d dogleg, Point3d text)
        {
            Palette.PatPaletteCommand.PendingNumber = number;
            Palette.PatPaletteCommand.PendingName = "测试名称";
            Editor.EnqueuePoint(PromptStatus.OK, attach);
            Editor.EnqueuePoint(PromptStatus.OK, dogleg);
            Editor.EnqueuePoint(PromptStatus.OK, text);
            Editor.EnqueuePoint(PromptStatus.Cancel, new Point3d());
        }

        public void QueueFreeModeAnnotation(string number, Point3d attach, Point3d dogleg)
        {
            Palette.PatPaletteCommand.PendingNumber = number;
            Palette.PatPaletteCommand.PendingName = "测试名称";
            Editor.EnqueuePoint(PromptStatus.OK, attach);
            Editor.EnqueuePoint(PromptStatus.OK, dogleg);
            Editor.EnqueuePoint(PromptStatus.None, new Point3d());
            Editor.EnqueuePoint(PromptStatus.None, new Point3d());
            Editor.EnqueuePoint(PromptStatus.Cancel, new Point3d());
        }
    }
}
