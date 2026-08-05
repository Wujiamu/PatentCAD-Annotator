using System;
using System.Collections.Generic;
using PatentMarker.IO;

namespace PatentMarker.Palette
{
    public class DictPaletteSession
    {
        private readonly List<PaletteEntry> _allEntries = new();
        private DictModel? _currentDict;
        private List<DictDiffEntry>? _currentDiff;
        public DictModel? CurrentDict => _currentDict;
        public List<PaletteEntry> AllEntries => _allEntries;
        public List<DictDiffEntry>? CurrentDiff => _currentDiff;
        public void Load(DictModel? dict, DictModel? previous)
        {
            if (dict is null) { Clear(); return; }
            _currentDict = dict;
            _currentDiff = previous is null ? null : DictDiff.Compute(previous, dict);
            _allEntries.Clear();
            foreach (DictEntry entry in dict.Entries)
                _allEntries.Add(new PaletteEntry { Number = entry.Number, Name = entry.Name, Occurrences = entry.Occurrences });
        }
        public void Clear() { _allEntries.Clear(); _currentDict = null; _currentDiff = null; }
        public List<PaletteEntry> Filter(string? keyword)
        {
            List<PaletteEntry> result = new(); string term = keyword?.Trim() ?? "";
            foreach (PaletteEntry entry in _allEntries)
                if (term.Length == 0 || entry.Number.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 || entry.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) result.Add(entry);
            return result;
        }
        public int WarningCount => _currentDict?.Warnings?.Count ?? 0;
        public int ConflictCount
        {
            get { int count = 0; if (_currentDict?.Entries is null) return count; foreach (DictEntry entry in _currentDict.Entries) count += entry.Conflicts?.Count ?? 0; return count; }
        }
    }
    public class PaletteEntry { public string Number = ""; public string Name = ""; public int Occurrences; }
}
