using PatentMarker.IO;
using System.IO;

namespace PatentMarker.Palette
{
    /// <summary>
    /// Dictionary and conflict lifecycle used by the palette.  It keeps the
    /// cache/path facade in one place so the WinForms control does not mix
    /// global loader state with view event handling.
    /// </summary>
    public sealed class DictPaletteWorkflow
    {
        public DictModel PreviousModel
        {
            get { return DictLoader.PreviousModel; }
        }

        public bool IsFileChanged()
        {
            return DictLoader.IsFileChanged();
        }

        public DictModel LoadCurrent()
        {
            return DictLoader.LoadForCurrentDrawing();
        }

        public DictModel ReloadCurrent()
        {
            DictLoader.InvalidateCache();
            return DictLoader.LoadForCurrentDrawing();
        }

        public string ResolveDictPath()
        {
            string path = DictLoader.CurrentPath;
            // A cached path can outlive a deleted/renamed dictionary.  Fall
            // back to the loader's canonical resolver so every palette action
            // sees the same current-drawing/configured path.
            return path != null && File.Exists(path) ? path : DictLoader.ResolveDictPath();
        }

        public bool IsPendingConflict(DictModel current, string dictPath)
        {
            return DictConflict.IsPendingConflict(current, dictPath);
        }

        public DictEntry FindEntry(DictModel dict, string number)
        {
            if (dict == null || dict.Entries == null) return null;
            foreach (DictEntry entry in dict.Entries)
            {
                if (NumberIdentity.AreEqual(entry.Number, number)) return entry;
            }
            return null;
        }
    }
}
