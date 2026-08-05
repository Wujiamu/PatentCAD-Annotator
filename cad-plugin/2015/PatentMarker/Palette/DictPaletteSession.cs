using System;
using System.Collections.Generic;
using PatentMarker.IO;

namespace PatentMarker.Palette
{
    public class DictPaletteSession
    {
        private readonly List<PaletteEntry> _allEntries = new List<PaletteEntry>();
        private DictModel _currentDict;
        private List<DictDiffEntry> _currentDiff;
        public DictModel CurrentDict { get { return _currentDict; } }
        public List<PaletteEntry> AllEntries { get { return _allEntries; } }
        public List<DictDiffEntry> CurrentDiff { get { return _currentDiff; } }
        public void Load(DictModel dict, DictModel previous)
        {
            if (dict == null) { Clear(); return; }
            _currentDict = dict;
            _currentDiff = previous != null ? DictDiff.Compute(previous, dict) : null;
            _allEntries.Clear();
            foreach (DictEntry entry in dict.Entries)
            {
                PaletteEntry item = new PaletteEntry(); item.Number = entry.Number; item.Name = entry.Name; item.Occurrences = entry.Occurrences; _allEntries.Add(item);
            }
        }
        public void Clear() { _allEntries.Clear(); _currentDict = null; _currentDiff = null; }
        public List<PaletteEntry> Filter(string keyword)
        {
            List<PaletteEntry> result = new List<PaletteEntry>(); string term = keyword == null ? "" : keyword.Trim();
            foreach (PaletteEntry entry in _allEntries) if (term.Length == 0 || entry.Number.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 || entry.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) result.Add(entry);
            return result;
        }
        public int WarningCount { get { return _currentDict == null || _currentDict.Warnings == null ? 0 : _currentDict.Warnings.Count; } }
        public int ConflictCount { get { if (_currentDict == null || _currentDict.Entries == null) return 0; int count = 0; foreach (DictEntry entry in _currentDict.Entries) count += entry.Conflicts == null ? 0 : entry.Conflicts.Count; return count; } }
    }
    public class PaletteEntry { public string Number = ""; public string Name = ""; public int Occurrences; }
}
