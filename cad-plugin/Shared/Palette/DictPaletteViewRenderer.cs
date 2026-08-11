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
                }
            }

            _entries.BeginUpdate();
            try
            {
                _entries.Items.Clear();
                foreach (DictEntry entry in dict.Entries)
                {
                    PaletteEntry paletteEntry = new PaletteEntry();
                    paletteEntry.Number = entry.Number;
                    paletteEntry.Name = entry.Name;
                    paletteEntry.Occurrences = entry.Occurrences;

                    ListViewItem item = CreateItem(paletteEntry);
                    DictDiffEntry diff;
                    if (diffMap.TryGetValue(entry, out diff))
                    {
                        item.SubItems.Add(diff.OldNumber);
                        item.SubItems.Add(diff.OldName);
                        ApplyDiffHighlight(item, diff.Status);
                    }
                    else
                    {
                        item.SubItems.Add("");
                        item.SubItems.Add("");
                    }
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

        public void RenderFiltered(List<PaletteEntry> entries)
        {
            _entries.BeginUpdate();
            try
            {
                _entries.Items.Clear();
                foreach (PaletteEntry entry in entries)
                    _entries.Items.Add(CreateItem(entry));
            }
            finally
            {
                _entries.EndUpdate();
            }
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

        private static ListViewItem CreateItem(PaletteEntry entry)
        {
            ListViewItem item = new ListViewItem(entry.Number);
            item.SubItems.Add(entry.Name != null ? entry.Name : "");
            item.SubItems.Add(entry.Occurrences.ToString());
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
