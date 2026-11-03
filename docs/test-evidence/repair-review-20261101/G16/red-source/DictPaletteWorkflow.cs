using PatentMarker.IO;
using PatentMarker.I18n;
using System;
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

        public static bool IsCurrentContext(object document, string path)
        {
            if (document == null || !object.ReferenceEquals(document, RuntimeHost.ActiveDocument)) return false;
            string current = DictLoader.ResolveDictPath();
            if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(path)) return false;
            try { return string.Equals(Path.GetFullPath(path), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        public static string DescribeWriteError(string error)
        {
            if (error == DictFileVisibility.BusyError) return Strings.Dict_Busy;
            if (error == DictFileVisibility.ChangedError) return Strings.Dict_Changed;
            if (error == DictFileVisibility.PendingError) return Strings.Dict_Pending;
            if (error == DictFileVisibility.BaselineError) return Strings.Dict_BaselineFailed;
            return error;
        }

        public bool IsDictVisible(string path)
        {
            return DictFileVisibility.IsVisible(path);
        }

        public bool TrySetDictVisible(string path, bool visible, out string error)
        {
            return DictFileVisibility.TrySetVisible(path, visible, out error);
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
