using Autodesk.AutoCAD.ApplicationServices;

namespace PatentMarker.IO
{
    /// <summary>
    /// Narrow host seam for command orchestration. Production uses AutoCAD's active document;
    /// the simulation tests can replace it without loading the Autodesk runtime.
    /// </summary>
    internal static class RuntimeHost
    {
        internal delegate Document DocumentResolver();

        private static DocumentResolver _override;

        internal static Document ActiveDocument
        {
            get
            {
                if (_override != null) return _override();
                return Application.DocumentManager.MdiActiveDocument;
            }
        }

        internal static void SetActiveDocumentOverride(DocumentResolver resolver)
        {
            _override = resolver;
        }

        internal static void ClearActiveDocumentOverride()
        {
            _override = null;
        }
    }
}
