using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PatentMarker.I18n;
using PatentMarker.IO;

namespace PatentMarker.Palette
{
    /// <summary>
    /// WinForms-only rendering boundary for the dictionary palette.
    /// It translates session data into ListView rows and keeps list/diff
    /// presentation out of the command and workflow orchestration class.
    /// </summary>
    public sealed class DictPaletteViewRenderer
    {
        private readonly ListView _entries;
        private readonly Label _dictInfo;
        private readonly Label _status;
        private readonly ColumnHeader _oldNumber;
        private readonly ColumnHeader _oldName;
        private object _checkDocumentKey = Commands.PatCheckResult.DefaultContextKey;

        public DictPaletteViewRenderer(
            ListView entries,
            Label dictInfo,
            Label status,
            ColumnHeader oldNumber,
            ColumnHeader oldName)
        {
            _entries = entries;
            _dictInfo = dictInfo;
            _status = status;
            _oldNumber = oldNumber;
            _oldName = oldName;
        }

        /// <summary>
        /// Selects the document whose PATCHECK result should color rows. The
        /// key is deliberately object-typed so the renderer remains usable
        /// by the 2007 target without referencing AutoCAD assemblies.
        /// </summary>
        public void SetCheckDocumentKey(object documentKey)
        {
            _checkDocumentKey = documentKey;
        }

        public void RenderDictionary(DictModel dict, DictPaletteSession session, bool compareMode)
        {
            List<DictDiffEntry> currentDiff = session.CurrentDiff;
            SetCompareMode(compareMode);

            Dictionary<DictEntry, DictDiffEntry> diffMap = new Dictionary<DictEntry, DictDiffEntry>();
            if (currentDiff != null)
            {
                foreach (DictDiffEntry diff in currentDiff)
                {
                    if (diff.NewEntry != null) diffMap[diff.NewEntry] = diff;
                    else if (diff.OldEntry != null) diffMap[diff.OldEntry] = diff;
                }
            }

            _entries.BeginUpdate();
            try
            {
                _entries.Items.Clear();
                foreach (PaletteEntry entry in session.AllEntries)
                {
                    DictDiffEntry diff;
                    diff = null;
                    if (entry.Source != null) diffMap.TryGetValue(entry.Source, out diff);
                    ListViewItem item = CreateItem(entry, diff);
                    _entries.Items.Add(item);
                }
            }
            finally
            {
                _entries.EndUpdate();
            }

            int warningCount = session.WarningCount;
            int conflictCount = session.ConflictCount;
            _dictInfo.Text = string.Format(Strings.Palette_DictInfo,
                dict.Entries.Count, warningCount, conflictCount);

            if (currentDiff != null)
            {
                string summary = DictDiff.Summarize(currentDiff);
                _status.Text = string.Format(Strings.Status_DictUpdated, summary);
                _status.ForeColor = SystemColors.Highlight;
            }
            else
            {
                _status.Text = Strings.Status_DictLoaded;
                _status.ForeColor = SystemColors.ControlText;
            }
        }

        public void RenderFiltered(List<PaletteEntry> entries,
            List<DictDiffEntry> currentDiff, bool compareMode)
        {
            SetCompareMode(compareMode);
            Dictionary<DictEntry, DictDiffEntry> diffMap = new Dictionary<DictEntry, DictDiffEntry>();
            if (currentDiff != null)
            {
                foreach (DictDiffEntry diff in currentDiff)
                {
                    if (diff.NewEntry != null) diffMap[diff.NewEntry] = diff;
                    else if (diff.OldEntry != null) diffMap[diff.OldEntry] = diff;
                }
            }

            _entries.BeginUpdate();
            try
            {
                _entries.Items.Clear();
                foreach (PaletteEntry entry in entries)
                {
                    DictDiffEntry diff;
                    diff = null;
                    if (entry.Source != null) diffMap.TryGetValue(entry.Source, out diff);
                    _entries.Items.Add(CreateItem(entry, diff));
                }
            }
            finally
            {
                _entries.EndUpdate();
            }
        }

        // Kept for callers compiled against the pre-compare renderer API.
        public void RenderFiltered(List<PaletteEntry> entries)
        {
            RenderFiltered(entries, null, false);
        }

        public void ShowNoDictionary()
        {
            _entries.Items.Clear();
            _dictInfo.Text = Strings.Palette_DictNotLoaded;
            _status.Text = Strings.Status_PlaceDictHint;
        }

        public void SetCompareMode(bool compareMode)
        {
            if (_oldNumber == null || _oldName == null) return;
            if (compareMode)
            {
                _oldNumber.Width = 55;
                _oldName.Width = 120;
            }
            else
            {
                _oldNumber.Width = 0;
                _oldName.Width = 0;
            }
        }

        private ListViewItem CreateItem(PaletteEntry entry, DictDiffEntry diff)
        {
            // v5.1：PATCHECK 之后未标注的条目以橙色 + △ 前缀标示
            // （前景色，不与对照模式的 diff 背景色冲突）
            bool unmarked = !entry.IsRemoved &&
                Commands.PatCheckResult.HasResultFor(_checkDocumentKey) &&
                Commands.PatCheckResult.IsUnmarked(
                    _checkDocumentKey,
                    IO.NumberIdentity.Normalize(entry.Number));
            string number = entry.Number ?? "";
            ListViewItem item = new ListViewItem(
                unmarked ? "△ " + number : number);
            if (unmarked) item.ForeColor = Color.DarkOrange;
            item.SubItems.Add(entry.Name != null ? entry.Name : "");
            item.SubItems.Add(entry.Occurrences.ToString());
            item.SubItems.Add(diff != null && diff.Status != DiffStatus.Removed
                ? diff.OldNumber : "");
            item.SubItems.Add(diff != null && diff.Status != DiffStatus.Removed
                ? diff.OldName : "");
            if (diff != null) ApplyDiffHighlight(item, diff.Status);
            item.Tag = entry;
            return item;
        }

        private static void ApplyDiffHighlight(ListViewItem item, DiffStatus status)
        {
            switch (status)
            {
                case DiffStatus.Added:
                    item.BackColor = Color.LightGreen;
                    break;
                case DiffStatus.Removed:
                    item.BackColor = Color.LightPink;
                    break;
                case DiffStatus.NumberChanged:
                    item.BackColor = Color.LightYellow;
                    break;
                case DiffStatus.NameChanged:
                    item.BackColor = Color.LightBlue;
                    break;
                case DiffStatus.BothChanged:
                    item.BackColor = Color.LightCoral;
                    break;
                case DiffStatus.Unchanged:
                default:
                    break;
            }
        }
    }
}
