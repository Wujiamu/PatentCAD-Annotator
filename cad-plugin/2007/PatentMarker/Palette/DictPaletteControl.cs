using Autodesk.AutoCAD.ApplicationServices;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using PatentMarker.IO;
using PatentMarker.I18n;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AppAcad = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PatentMarker.Palette
{
    /// <summary>
    /// WinForms 字典面板 — AutoCAD 2007 (.NET 2.0) 版本。
    ///
    /// v2.3：中英双语支持（运行时切换，持久化到 config.json）+ 字体加大。
    /// </summary>
    public class DictPaletteControl : UserControl
    {
        private Label _lblTitle;
        private Label _lblDictInfo;
        private TextBox _txtSearch;
        private Button _btnReload;
        private Button _btnOpen;
        private Button _btnConflicts;
        private Button _btnDelete;
        private Button _btnArrow;
        private NumericUpDown _numArrowSize;
        private Button _btnSpline;
        private Button _btnSelectAll;
        private Button _btnCompare;
        private Button _btnLanguage;  // v2.3：语言切换
        private Label _lblStatus;
        private NumericUpDown _numTextHeight;
        private ListView _lstEntries;
        private ColumnHeader _colNumber;
        private ColumnHeader _colName;
        private ColumnHeader _colOcc;
        private ColumnHeader _colOldNumber;
        private ColumnHeader _colOldName;

        private List<PaletteEntry> _allEntries = new List<PaletteEntry>();
        private DictModel _currentDict;
        private System.Windows.Forms.Timer _autoRefreshTimer;

        private List<DictDiffEntry> _currentDiff;
        private bool _compareMode;

        public DictPaletteControl()
        {
            InitializeComponent();
            ApplyLanguage();
            _autoRefreshTimer = new System.Windows.Forms.Timer();
            _autoRefreshTimer.Interval = 2000;
            _autoRefreshTimer.Tick += new EventHandler(AutoRefreshTimer_Tick);
            _autoRefreshTimer.Start();
        }

        private void AutoRefreshTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (!DictLoader.IsFileChanged()) return;
                var dict = DictLoader.LoadForCurrentDrawing();
                if (dict != null)
                {
                    LoadDict(dict);
                    _lblStatus.Text = Strings.Status_DictAutoUpdated;
                }
                else if (_currentDict != null)
                {
                    ShowNoDict();
                }
            }
            catch (System.Exception ex)
            {
                PatentMarkerApp.RawLog("AutoRefresh error: " + ex.Message);
            }
        }

        private void InitializeComponent()
        {
            _lblTitle = new Label();
            _lblTitle.Text = Strings.Palette_Title;
            _lblTitle.Font = new Font("Microsoft Sans Serif", 12F, FontStyle.Bold);
            _lblTitle.Dock = DockStyle.Top;
            _lblTitle.Height = 26;
            _lblTitle.Padding = new Padding(2);

            _lblDictInfo = new Label();
            _lblDictInfo.Text = Strings.Palette_DictNotLoaded;
            _lblDictInfo.ForeColor = SystemColors.GrayText;
            _lblDictInfo.Dock = DockStyle.Top;
            _lblDictInfo.Height = 20;
            _lblDictInfo.Font = new Font("Microsoft Sans Serif", 9F);
            _lblDictInfo.Padding = new Padding(2);

            // 搜索栏 | Search bar
            Panel searchPanel = new Panel();
            searchPanel.Dock = DockStyle.Top;
            searchPanel.Height = 30;
            searchPanel.Padding = new Padding(0, 2, 0, 2);

            _txtSearch = new TextBox();
            _txtSearch.Dock = DockStyle.Fill;
            _txtSearch.Margin = new Padding(2);
            Label searchLbl = new Label();
            searchLbl.Text = Strings.Palette_Search;
            searchLbl.Dock = DockStyle.Left;
            searchLbl.Width = 45;
            searchLbl.TextAlign = ContentAlignment.MiddleCenter;
            searchPanel.Controls.AddRange(new Control[] { _txtSearch, searchLbl });
            _txtSearch.TextChanged += new EventHandler(TxtSearch_TextChanged);

            // 文字高度栏 | Text height bar
            FlowLayoutPanel heightPanel = new FlowLayoutPanel();
            heightPanel.Dock = DockStyle.Top;
            heightPanel.Height = 30;
            heightPanel.WrapContents = true;
            heightPanel.FlowDirection = FlowDirection.LeftToRight;
            heightPanel.Padding = new Padding(0, 2, 0, 2);

            Label heightLbl = new Label();
            heightLbl.Text = Strings.Palette_TextHeight;
            heightLbl.AutoSize = true;
            heightLbl.TextAlign = ContentAlignment.MiddleLeft;

            _numTextHeight = new NumericUpDown();
            _numTextHeight.Width = 55;
            _numTextHeight.Minimum = 1.0m;
            _numTextHeight.Maximum = 20.0m;
            _numTextHeight.Value = 3.5m;
            _numTextHeight.DecimalPlaces = 1;
            _numTextHeight.Increment = 0.5m;
            _numTextHeight.ValueChanged += new EventHandler(NumTextHeight_ValueChanged);

            Button heightReset = new Button();
            heightReset.Text = Strings.Palette_Reset;
            heightReset.AutoSize = true;
            heightReset.Click += delegate(object s, EventArgs ev) { _numTextHeight.Value = 3.5m; };

            heightPanel.Controls.AddRange(new Control[] { heightLbl, _numTextHeight, heightReset });

            // 样式栏 | Style bar
            FlowLayoutPanel stylePanel = new FlowLayoutPanel();
            stylePanel.Dock = DockStyle.Top;
            stylePanel.Height = 30;
            stylePanel.WrapContents = true;
            stylePanel.FlowDirection = FlowDirection.LeftToRight;
            stylePanel.Padding = new Padding(0, 2, 0, 2);

            _btnArrow = new Button();
            _btnArrow.AutoSize = true;
            _btnArrow.Click += new EventHandler(BtnArrow_Click);
            UpdateArrowButtonText();

            Label arrowSizeLbl = new Label();
            arrowSizeLbl.Text = Strings.Palette_ArrowSize;
            arrowSizeLbl.AutoSize = true;
            arrowSizeLbl.TextAlign = ContentAlignment.MiddleLeft;

            _numArrowSize = new NumericUpDown();
            _numArrowSize.Width = 50;
            _numArrowSize.Minimum = 0.5m;
            _numArrowSize.Maximum = 20.0m;
            _numArrowSize.Value = 2.5m;
            _numArrowSize.DecimalPlaces = 1;
            _numArrowSize.Increment = 0.5m;
            _numArrowSize.ValueChanged += new EventHandler(NumArrowSize_ValueChanged);

            _btnSpline = new Button();
            _btnSpline.AutoSize = true;
            _btnSpline.Click += new EventHandler(BtnSpline_Click);
            UpdateSplineButtonText();

            stylePanel.Controls.AddRange(new Control[] { _btnArrow, arrowSizeLbl, _numArrowSize, _btnSpline });

            // 按钮栏 | Button bar
            FlowLayoutPanel btnPanel = new FlowLayoutPanel();
            btnPanel.Dock = DockStyle.Top;
            btnPanel.Height = 60;
            btnPanel.WrapContents = true;
            btnPanel.FlowDirection = FlowDirection.LeftToRight;
            btnPanel.Padding = new Padding(0, 2, 0, 2);

            _btnReload = new Button();
            _btnReload.Text = Strings.Palette_Reload;
            _btnReload.AutoSize = true;
            _btnReload.Margin = new Padding(0, 0, 4, 2);

            _btnOpen = new Button();
            _btnOpen.Text = Strings.Palette_Open;
            _btnOpen.AutoSize = true;
            _btnOpen.Margin = new Padding(0, 0, 4, 2);

            _btnConflicts = new Button();
            _btnConflicts.Text = Strings.Palette_Conflicts;
            _btnConflicts.AutoSize = true;
            _btnConflicts.Margin = new Padding(0, 0, 4, 2);

            _btnDelete = new Button();
            _btnDelete.Text = Strings.Palette_DeleteLeader;
            _btnDelete.AutoSize = true;
            _btnDelete.Margin = new Padding(0, 0, 4, 2);

            _btnSelectAll = new Button();
            _btnSelectAll.Text = Strings.Palette_SelectAll;
            _btnSelectAll.AutoSize = true;
            _btnSelectAll.Margin = new Padding(0, 0, 4, 2);

            _btnCompare = new Button();
            _btnCompare.Text = Strings.Palette_Compare;
            _btnCompare.AutoSize = true;
            _btnCompare.Margin = new Padding(0, 0, 4, 2);
            _btnCompare.Enabled = false;

            // v2.3：语言切换按钮 | Language toggle button
            _btnLanguage = new Button();
            _btnLanguage.Text = Strings.Palette_Language;
            _btnLanguage.AutoSize = true;
            _btnLanguage.Margin = new Padding(0, 0, 4, 2);
            _btnLanguage.Click += new EventHandler(BtnLanguage_Click);

            btnPanel.Controls.AddRange(new Control[] {
                _btnReload, _btnOpen, _btnConflicts, _btnDelete,
                _btnSelectAll, _btnCompare, _btnLanguage
            });
            _btnReload.Click += new EventHandler(BtnReload_Click);
            _btnOpen.Click += new EventHandler(BtnOpen_Click);
            _btnConflicts.Click += new EventHandler(BtnConflicts_Click);
            _btnDelete.Click += new EventHandler(BtnDelete_Click);
            _btnSelectAll.Click += new EventHandler(BtnSelectAll_Click);
            _btnCompare.Click += new EventHandler(BtnCompare_Click);

            _lblStatus = new Label();
            _lblStatus.Text = Strings.Status_Ready;
            _lblStatus.ForeColor = SystemColors.ControlText;
            _lblStatus.Dock = DockStyle.Top;
            _lblStatus.Height = 20;
            _lblStatus.Font = new Font("Microsoft Sans Serif", 9F);
            _lblStatus.Padding = new Padding(2);

            _lstEntries = new ListView();
            _lstEntries.Dock = DockStyle.Fill;
            _lstEntries.View = View.Details;
            _lstEntries.FullRowSelect = true;
            _lstEntries.MultiSelect = false;
            _lstEntries.HideSelection = false;

            _colNumber = new ColumnHeader();
            _colNumber.Text = Strings.Col_Number;
            _colNumber.Width = 55;
            _colName = new ColumnHeader();
            _colName.Text = Strings.Col_Name;
            _colName.Width = 170;
            _colOcc = new ColumnHeader();
            _colOcc.Text = Strings.Col_Occ;
            _colOcc.Width = 45;
            _colOldNumber = new ColumnHeader();
            _colOldNumber.Text = Strings.Col_OldNumber;
            _colOldNumber.Width = 0;
            _colOldName = new ColumnHeader();
            _colOldName.Text = Strings.Col_OldName;
            _colOldName.Width = 0;
            _lstEntries.Columns.AddRange(new ColumnHeader[] { _colNumber, _colName, _colOcc, _colOldNumber, _colOldName });
            _lstEntries.SelectedIndexChanged += new EventHandler(LstEntries_SelectedIndexChanged);
            _lstEntries.DoubleClick += new EventHandler(LstEntries_DoubleClick);

            this.Controls.Add(_lstEntries);
            this.Controls.Add(_lblStatus);
            this.Controls.Add(btnPanel);
            this.Controls.Add(heightPanel);
            this.Controls.Add(stylePanel);
            this.Controls.Add(searchPanel);
            this.Controls.Add(_lblDictInfo);
            this.Controls.Add(_lblTitle);

            this.Dock = DockStyle.Fill;
            this.Font = new Font("Microsoft Sans Serif", 10F);
        }

        /// <summary>
        /// v2.3：刷新所有 UI 文本为当前语言 | Refresh all UI text to current language
        /// </summary>
        private void ApplyLanguage()
        {
            if (_lblTitle != null) _lblTitle.Text = Strings.Palette_Title;
            if (_lblDictInfo != null && _currentDict == null)
                _lblDictInfo.Text = Strings.Palette_DictNotLoaded;
            // 语言切换后状态栏重置为"就绪" | Reset status to Ready on language toggle
            if (_lblStatus != null)
                _lblStatus.Text = Strings.Status_Ready;
            if (_btnReload != null) _btnReload.Text = Strings.Palette_Reload;
            if (_btnOpen != null) _btnOpen.Text = Strings.Palette_Open;
            if (_btnConflicts != null) _btnConflicts.Text = Strings.Palette_Conflicts;
            if (_btnDelete != null) _btnDelete.Text = Strings.Palette_DeleteLeader;
            if (_btnSelectAll != null) _btnSelectAll.Text = Strings.Palette_SelectAll;
            if (_btnCompare != null) _btnCompare.Text = Strings.Palette_Compare;
            if (_btnLanguage != null) _btnLanguage.Text = Strings.Palette_Language;
            if (_colNumber != null) _colNumber.Text = Strings.Col_Number;
            if (_colName != null) _colName.Text = Strings.Col_Name;
            if (_colOcc != null) _colOcc.Text = Strings.Col_Occ;
            if (_colOldNumber != null) _colOldNumber.Text = Strings.Col_OldNumber;
            if (_colOldName != null) _colOldName.Text = Strings.Col_OldName;
            UpdateArrowButtonText();
            UpdateSplineButtonText();
        }

        /// <summary>v2.3：切换语言 | Toggle language</summary>
        private void BtnLanguage_Click(object sender, EventArgs e)
        {
            Strings.Lang = (Strings.Lang == Language.Chinese) ? Language.English : Language.Chinese;
            ApplyLanguage();
            // 刷新字典信息标签
            if (_currentDict != null)
            {
                LoadDict(_currentDict);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _autoRefreshTimer != null)
            {
                _autoRefreshTimer.Stop();
                _autoRefreshTimer.Dispose();
                _autoRefreshTimer = null;
            }
            base.Dispose(disposing);
        }

        public void LoadDict(DictModel dict)
        {
            if (dict == null) { ShowNoDict(); return; }
            _currentDict = dict;

            DictModel prevDict = DictLoader.PreviousModel;
            if (prevDict != null)
            {
                _currentDiff = DictDiff.Compute(prevDict, dict);
                _btnCompare.Enabled = true;
            }
            else
            {
                _currentDiff = null;
                _btnCompare.Enabled = false;
                _compareMode = false;
                UpdateCompareColumns();
            }

            _allEntries.Clear();
            _lstEntries.BeginUpdate();
            _lstEntries.Items.Clear();

            List<DictEntry> sorted = new List<DictEntry>(dict.Entries);
            sorted.Sort(delegate(DictEntry a, DictEntry b) { return NaturalCompare(a.Number, b.Number); });

            Dictionary<DictEntry, DictDiffEntry> diffMap = new Dictionary<DictEntry, DictDiffEntry>();
            if (_currentDiff != null)
            {
                foreach (DictDiffEntry d in _currentDiff)
                {
                    if (d.NewEntry != null) diffMap[d.NewEntry] = d;
                }
            }

            foreach (DictEntry e in sorted)
            {
                PaletteEntry pe = new PaletteEntry();
                pe.Number = e.Number;
                pe.Name = e.Name;
                pe.Occurrences = e.Occurrences;
                _allEntries.Add(pe);

                ListViewItem item = new ListViewItem(pe.Number);
                item.SubItems.Add(pe.Name != null ? pe.Name : "");
                item.SubItems.Add(pe.Occurrences.ToString());
                item.Tag = pe;

                DictDiffEntry diff = null;
                if (diffMap.TryGetValue(e, out diff))
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

                _lstEntries.Items.Add(item);
            }
            _lstEntries.EndUpdate();

            int warnCount = dict.Warnings != null ? dict.Warnings.Count : 0;
            int conflictCount = 0;
            foreach (DictEntry e in dict.Entries)
                conflictCount += (e.Conflicts != null ? e.Conflicts.Count : 0);

            _lblDictInfo.Text = string.Format(Strings.Palette_DictInfo,
                dict.Entries.Count, warnCount, conflictCount);

            if (_currentDiff != null)
            {
                string summary = DictDiff.Summarize(_currentDiff);
                _lblStatus.Text = string.Format(Strings.Status_DictUpdated, summary);
                _lblStatus.ForeColor = SystemColors.Highlight;
            }
            else
            {
                _lblStatus.Text = Strings.Status_DictLoaded;
                _lblStatus.ForeColor = SystemColors.ControlText;
            }
        }

        private void ApplyDiffHighlight(ListViewItem item, DiffStatus status)
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

        private void UpdateCompareColumns()
        {
            if (_colOldNumber == null || _colOldName == null) return;
            if (_compareMode)
            {
                _colOldNumber.Width = 55;
                _colOldName.Width = 120;
            }
            else
            {
                _colOldNumber.Width = 0;
                _colOldName.Width = 0;
            }
        }

        public void ShowNoDict()
        {
            _allEntries.Clear();
            _lstEntries.Items.Clear();
            _currentDict = null;
            _lblDictInfo.Text = Strings.Palette_DictNotLoaded;
            _lblStatus.Text = Strings.Status_PlaceDictHint;
        }

        private static int NaturalCompare(string a, string b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                char ca = a[i];
                char cb = b[j];

                if (char.IsDigit(ca) && char.IsDigit(cb))
                {
                    int startA = i;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    int numA = 0;
                    int.TryParse(a.Substring(startA, i - startA), out numA);

                    int startB = j;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    int numB = 0;
                    int.TryParse(b.Substring(startB, j - startB), out numB);

                    if (numA != numB) return numA.CompareTo(numB);
                }
                else
                {
                    if (ca != cb) return ca.CompareTo(cb);
                    i++;
                    j++;
                }
            }

            if (i < a.Length) return 1;
            if (j < b.Length) return -1;
            return 0;
        }

        // ===== 事件 =====

        private void TxtSearch_TextChanged(object sender, EventArgs e)
        {
            string keyword = _txtSearch.Text.Trim().ToLowerInvariant();
            _lstEntries.BeginUpdate();
            _lstEntries.Items.Clear();

            foreach (PaletteEntry entry in _allEntries)
            {
                bool match = false;
                if (keyword.Length == 0)
                    match = true;
                else
                {
                    string numLower = entry.Number.ToLowerInvariant();
                    string nameLower = entry.Name != null ? entry.Name.ToLowerInvariant() : "";
                    if (numLower.IndexOf(keyword) >= 0 || nameLower.IndexOf(keyword) >= 0)
                        match = true;
                }

                if (match)
                {
                    ListViewItem item = new ListViewItem(entry.Number);
                    item.SubItems.Add(entry.Name != null ? entry.Name : "");
                    item.SubItems.Add(entry.Occurrences.ToString());
                    item.Tag = entry;
                    _lstEntries.Items.Add(item);
                }
            }
            _lstEntries.EndUpdate();
        }

        private void LstEntries_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_lstEntries.SelectedItems.Count > 0)
            {
                PaletteEntry entry = _lstEntries.SelectedItems[0].Tag as PaletteEntry;
                if (entry != null)
                    _lblStatus.Text = string.Format(Strings.Status_Selected, entry.Number, entry.Name);
            }
        }

        private void NumTextHeight_ValueChanged(object sender, EventArgs e)
        {
            PatPaletteCommand.TextHeight = (double)_numTextHeight.Value;
        }

        private void BtnArrow_Click(object sender, EventArgs e)
        {
            PatPaletteCommand.HasArrowHead = !PatPaletteCommand.HasArrowHead;
            UpdateArrowButtonText();
            string state = PatPaletteCommand.HasArrowHead ? Strings.Palette_On : Strings.Palette_Off;
            _lblStatus.Text = string.Format(Strings.Status_ArrowToggled, state);
        }

        private void UpdateArrowButtonText()
        {
            if (_btnArrow != null)
            {
                string state = PatPaletteCommand.HasArrowHead ? Strings.Palette_On : Strings.Palette_Off;
                _btnArrow.Text = string.Format(Strings.Palette_ArrowOnOff, state);
            }
        }

        private void NumArrowSize_ValueChanged(object sender, EventArgs e)
        {
            PatPaletteCommand.ArrowSize = (double)_numArrowSize.Value;
        }

        private void BtnSpline_Click(object sender, EventArgs e)
        {
            PatPaletteCommand.IsSplined = !PatPaletteCommand.IsSplined;
            UpdateSplineButtonText();
            string desc = PatPaletteCommand.IsSplined ? Strings.Status_SplineDesc : Strings.Status_StraightDesc;
            _lblStatus.Text = string.Format(Strings.Status_SplineToggled, desc);
        }

        private void UpdateSplineButtonText()
        {
            if (_btnSpline != null)
                _btnSpline.Text = PatPaletteCommand.IsSplined ? Strings.Palette_LineTypeSpline : Strings.Palette_LineTypeStraight;
        }

        private void BtnSelectAll_Click(object sender, EventArgs e)
        {
            try
            {
                var doc = AppAcad.DocumentManager.MdiActiveDocument;
                if (doc != null)
                    doc.SendStringToExecute("PATSELECTALL\n", true, false, false);
            }
            catch (System.Exception ex)
            {
                PatentMarkerApp.RawLog("BtnSelectAll error: " + ex.Message);
            }
        }

        private void BtnCompare_Click(object sender, EventArgs e)
        {
            _compareMode = !_compareMode;
            UpdateCompareColumns();
            _lblStatus.Text = _compareMode ? Strings.Status_CompareShown : Strings.Status_CompareHidden;
        }

        private void LstEntries_DoubleClick(object sender, EventArgs e)
        {
            if (_lstEntries.SelectedItems.Count == 0) return;
            PaletteEntry entry = _lstEntries.SelectedItems[0].Tag as PaletteEntry;
            if (entry == null) return;

            PatPaletteCommand.PendingNumber = entry.Number;
            PatPaletteCommand.PendingName = entry.Name != null ? entry.Name : "";
            _lblStatus.Text = string.Format(Strings.Status_Loaded, entry.Number);

            var doc = AppAcad.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                doc.Editor.WriteMessage(string.Format(Strings.Status_LoadedCmd,
                    PatPaletteCommand.PendingNumber, PatPaletteCommand.PendingName));
                doc.SendStringToExecute("PATMARK ", false, false, false);
            }
        }

        private void BtnReload_Click(object sender, EventArgs e)
        {
            try
            {
                DictLoader.InvalidateCache();
                var dict = DictLoader.LoadForCurrentDrawing();
                if (dict != null)
                {
                    LoadDict(dict);
                    _lblStatus.Text = Strings.Status_Reloaded;
                }
                else
                    ShowNoDict();
            }
            catch (System.Exception ex)
            {
                _lblStatus.Text = string.Format(Strings.Status_LoadFailed, ex.Message);
            }
        }

        private void BtnOpen_Click(object sender, EventArgs e)
        {
            try
            {
                var doc = AppAcad.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                string dwgDir = System.IO.Path.GetDirectoryName(doc.Name);
                if (dwgDir == null) dwgDir = "";
                string dwgBase = System.IO.Path.GetFileNameWithoutExtension(doc.Name);
                string dictPath = System.IO.Path.Combine(dwgDir, dwgBase + ".dict.json");
                if (!System.IO.File.Exists(dictPath))
                {
                    _lblStatus.Text = Strings.Status_NoDictFile;
                    return;
                }
                System.Diagnostics.Process.Start(dictPath);
            }
            catch (System.Exception ex)
            {
                _lblStatus.Text = string.Format(Strings.Status_OpenFailed, ex.Message);
            }
        }

        private void BtnConflicts_Click(object sender, EventArgs e)
        {
            if (_currentDict == null) { _lblStatus.Text = Strings.Palette_DictNotLoaded; return; }

            int warnCount = _currentDict.Warnings != null ? _currentDict.Warnings.Count : 0;
            int conflictCount = 0;
            foreach (DictEntry ent in _currentDict.Entries)
                conflictCount += (ent.Conflicts != null ? ent.Conflicts.Count : 0);

            if (warnCount == 0 && conflictCount == 0)
            {
                MessageBox.Show(Strings.Msg_NoConflicts, Strings.Msg_ConflictTitle);
                return;
            }

            string msg = "";
            if (warnCount > 0)
            {
                msg += Strings.Msg_WarningsSection;
                foreach (string w in _currentDict.Warnings)
                    msg += "  * " + w + "\r\n";
            }
            if (conflictCount > 0)
            {
                msg += Strings.Msg_ConflictsSection;
                foreach (DictEntry ent in _currentDict.Entries)
                {
                    if (ent.Conflicts == null || ent.Conflicts.Count == 0) continue;
                    foreach (ConflictInfo c in ent.Conflicts)
                    {
                        string cands = string.Join(" vs ", c.Candidates.ToArray());
                        msg += "  #" + c.Number + ": " + cands + "\r\n";
                    }
                }
            }
            MessageBox.Show(msg, Strings.Msg_ConflictTitle);
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            var doc = AppAcad.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

            PatentMarkerApp.RawLog("BtnDelete: starting...");

            DialogResult confirm = MessageBox.Show(
                Strings.Msg_DeleteConfirm,
                Strings.Msg_DeleteTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            using (Autodesk.AutoCAD.ApplicationServices.DocumentLock docLock = doc.LockDocument())
            {
                using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
                {
                    AcDb.BlockTable bt = (AcDb.BlockTable)tr.GetObject(db.BlockTableId, AcDb.OpenMode.ForRead);
                    AcDb.BlockTableRecord btr = (AcDb.BlockTableRecord)tr.GetObject(
                        bt[AcDb.BlockTableRecord.ModelSpace], AcDb.OpenMode.ForWrite);

                    int deleted = 0;
                    int skipped = 0;
                    List<AcDb.ObjectId> toDelete = new List<AcDb.ObjectId>();
                    List<AcDb.ObjectId> mtextToDelete = new List<AcDb.ObjectId>();

                    foreach (AcDb.ObjectId entId in btr)
                    {
                        AcDb.Entity ent = (AcDb.Entity)tr.GetObject(entId, AcDb.OpenMode.ForRead);
                        AcDb.Leader leader = ent as AcDb.Leader;
                        if (leader == null) continue;

                        if (!PatEntityHelper.IsPatEntity(leader, tr)) { skipped++; continue; }

                        toDelete.Add(entId);
                        if (!leader.Annotation.IsNull)
                            mtextToDelete.Add(leader.Annotation);
                    }

                    foreach (AcDb.ObjectId id in toDelete)
                    {
                        try
                        {
                            AcDb.Leader leader = (AcDb.Leader)tr.GetObject(id, AcDb.OpenMode.ForWrite);
                            leader.Erase(true);
                            deleted++;
                        }
                        catch (System.Exception ex)
                        {
                            PatentMarkerApp.RawLog("BtnDelete leader error: " + ex.Message);
                        }
                    }

                    foreach (AcDb.ObjectId id in mtextToDelete)
                    {
                        try
                        {
                            AcDb.MText mt = (AcDb.MText)tr.GetObject(id, AcDb.OpenMode.ForWrite);
                            mt.Erase(true);
                        }
                        catch (System.Exception ex)
                        {
                            PatentMarkerApp.RawLog("BtnDelete mtext error: " + ex.Message);
                        }
                    }

                    tr.Commit();
                    _lblStatus.Text = string.Format(Strings.Status_Deleted, deleted, skipped);
                    doc.Editor.WriteMessage(string.Format(Strings.Status_DeletedCmd, deleted, skipped));
                    PatentMarkerApp.RawLog("BtnDelete: done, deleted=" + deleted + ", skipped=" + skipped);
                }
            }
        }
    }

    /// <summary>面板列表项数据模型</summary>
    public class PaletteEntry
    {
        public string Number = "";
        public string Name = "";
        public int Occurrences;
    }
}
