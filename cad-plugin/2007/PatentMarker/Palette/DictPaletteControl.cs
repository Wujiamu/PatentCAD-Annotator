using Autodesk.AutoCAD.ApplicationServices;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using PatentMarker.IO;
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
    /// 改动：
    ///   - 去 LINQ、$""、?.、using var（C# 3.0 兼容）
    ///   - Explode 按钮 → "删除引线"（2007 的 Leader 已是基本实体，无需 explode）
    ///   - 修复 D2：面板事件中修改数据库需 LockDocument
    ///   - 修复 S2：真正的自然排序
    ///   - 修复 S8：中文 UI
    /// </summary>
    public class DictPaletteControl : UserControl
    {
        private Label _lblTitle;
        private Label _lblDictInfo;
        private TextBox _txtSearch;
        private Button _btnReload;
        private Button _btnOpen;
        private Button _btnConflicts;
        private Button _btnDelete;  // 原 Explode → 删除引线
        private Button _btnArrow;   // v2：箭头开关
        private NumericUpDown _numArrowSize;  // v2.1：箭头大小
        private Button _btnSpline;  // v2.1：样条/直线开关
        private Button _btnSelectAll;  // v2.2：全选 PAT 文字
        private Button _btnCompare;  // v2.2：对照开关
        private Label _lblStatus;
        private NumericUpDown _numTextHeight;
        private ListView _lstEntries;
        private ColumnHeader _colNumber;
        private ColumnHeader _colName;
        private ColumnHeader _colOcc;
        private ColumnHeader _colOldNumber;  // v2.2：旧编号（对照模式）
        private ColumnHeader _colOldName;    // v2.2：旧名称（对照模式）

        private List<PaletteEntry> _allEntries = new List<PaletteEntry>();
        private DictModel _currentDict;
        private System.Windows.Forms.Timer _autoRefreshTimer;

        // v2.2：字典对比状态
        private List<DictDiffEntry> _currentDiff;  // null 表示无对比基线
        private bool _compareMode;  // 是否显示对照列

        public DictPaletteControl()
        {
            InitializeComponent();
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
                    _lblStatus.Text = "字典已自动更新。";
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
            _lblTitle.Text = "PatentMarker 字典";
            _lblTitle.Font = new Font("Microsoft Sans Serif", 11F, FontStyle.Bold);
            _lblTitle.Dock = DockStyle.Top;
            _lblTitle.Height = 24;
            _lblTitle.Padding = new Padding(2);

            _lblDictInfo = new Label();
            _lblDictInfo.Text = "未加载字典";
            _lblDictInfo.ForeColor = SystemColors.GrayText;
            _lblDictInfo.Dock = DockStyle.Top;
            _lblDictInfo.Height = 18;
            _lblDictInfo.Font = new Font("Microsoft Sans Serif", 8F);
            _lblDictInfo.Padding = new Padding(2);

            // 搜索栏
            Panel searchPanel = new Panel();
            searchPanel.Dock = DockStyle.Top;
            searchPanel.Height = 28;
            searchPanel.Padding = new Padding(0, 2, 0, 2);

            _txtSearch = new TextBox();
            _txtSearch.Dock = DockStyle.Fill;
            _txtSearch.Margin = new Padding(2);
            Label searchLbl = new Label();
            searchLbl.Text = "搜索";
            searchLbl.Dock = DockStyle.Left;
            searchLbl.Width = 35;
            searchLbl.TextAlign = ContentAlignment.MiddleCenter;
            searchPanel.Controls.AddRange(new Control[] { _txtSearch, searchLbl });
            _txtSearch.TextChanged += new EventHandler(TxtSearch_TextChanged);

            // 文字高度栏（FlowLayoutPanel 自动换行）
            FlowLayoutPanel heightPanel = new FlowLayoutPanel();
            heightPanel.Dock = DockStyle.Top;
            heightPanel.Height = 30;
            heightPanel.WrapContents = true;
            heightPanel.FlowDirection = FlowDirection.LeftToRight;
            heightPanel.Padding = new Padding(0, 2, 0, 2);

            Label heightLbl = new Label();
            heightLbl.Text = "字高:";
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
            heightReset.Text = "重置";
            heightReset.AutoSize = true;
            heightReset.Click += delegate(object s, EventArgs ev) { _numTextHeight.Value = 3.5m; };

            heightPanel.Controls.AddRange(new Control[] { heightLbl, _numTextHeight, heightReset });

            // v2.1：样式栏 — 箭头开关 + 箭头大小 + 线型开关（FlowLayoutPanel 自动换行）
            FlowLayoutPanel stylePanel = new FlowLayoutPanel();
            stylePanel.Dock = DockStyle.Top;
            stylePanel.Height = 30;
            stylePanel.WrapContents = true;
            stylePanel.FlowDirection = FlowDirection.LeftToRight;
            stylePanel.Padding = new Padding(0, 2, 0, 2);

            // 箭头开关按钮
            _btnArrow = new Button();
            _btnArrow.AutoSize = true;
            _btnArrow.Click += new EventHandler(BtnArrow_Click);
            UpdateArrowButtonText();

            // 箭头大小
            Label arrowSizeLbl = new Label();
            arrowSizeLbl.Text = "大小:";
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

            // 线型开关按钮（样条/直线）
            _btnSpline = new Button();
            _btnSpline.AutoSize = true;
            _btnSpline.Click += new EventHandler(BtnSpline_Click);
            UpdateSplineButtonText();

            stylePanel.Controls.AddRange(new Control[] { _btnArrow, arrowSizeLbl, _numArrowSize, _btnSpline });

            // 按钮栏（FlowLayoutPanel 自动换行）
            FlowLayoutPanel btnPanel = new FlowLayoutPanel();
            btnPanel.Dock = DockStyle.Top;
            btnPanel.Height = 60;  // 预留两行高度，防换行时被截断
            btnPanel.WrapContents = true;
            btnPanel.FlowDirection = FlowDirection.LeftToRight;
            btnPanel.Padding = new Padding(0, 2, 0, 2);

            _btnReload = new Button();
            _btnReload.Text = "重载";
            _btnReload.AutoSize = true;
            _btnReload.Margin = new Padding(0, 0, 4, 2);

            _btnOpen = new Button();
            _btnOpen.Text = "打开";
            _btnOpen.AutoSize = true;
            _btnOpen.Margin = new Padding(0, 0, 4, 2);

            _btnConflicts = new Button();
            _btnConflicts.Text = "冲突";
            _btnConflicts.AutoSize = true;
            _btnConflicts.Margin = new Padding(0, 0, 4, 2);

            _btnDelete = new Button();
            _btnDelete.Text = "删除引线";
            _btnDelete.AutoSize = true;
            _btnDelete.Margin = new Padding(0, 0, 4, 2);

            // v2.2：全选 PAT 文字
            _btnSelectAll = new Button();
            _btnSelectAll.Text = "全选";
            _btnSelectAll.AutoSize = true;
            _btnSelectAll.Margin = new Padding(0, 0, 4, 2);

            // v2.2：对照开关（切换显示旧版字典列）
            _btnCompare = new Button();
            _btnCompare.Text = "对照";
            _btnCompare.AutoSize = true;
            _btnCompare.Margin = new Padding(0, 0, 4, 2);
            _btnCompare.Enabled = false;  // 无对比基线时禁用

            btnPanel.Controls.AddRange(new Control[] { _btnReload, _btnOpen, _btnConflicts, _btnDelete, _btnSelectAll, _btnCompare });
            _btnReload.Click += new EventHandler(BtnReload_Click);
            _btnOpen.Click += new EventHandler(BtnOpen_Click);
            _btnConflicts.Click += new EventHandler(BtnConflicts_Click);
            _btnDelete.Click += new EventHandler(BtnDelete_Click);
            _btnSelectAll.Click += new EventHandler(BtnSelectAll_Click);
            _btnCompare.Click += new EventHandler(BtnCompare_Click);

            _lblStatus = new Label();
            _lblStatus.Text = "就绪。";
            _lblStatus.ForeColor = SystemColors.ControlText;
            _lblStatus.Dock = DockStyle.Top;
            _lblStatus.Height = 18;
            _lblStatus.Font = new Font("Microsoft Sans Serif", 8F);
            _lblStatus.Padding = new Padding(2);

            _lstEntries = new ListView();
            _lstEntries.Dock = DockStyle.Fill;
            _lstEntries.View = View.Details;
            _lstEntries.FullRowSelect = true;
            _lstEntries.MultiSelect = false;
            _lstEntries.HideSelection = false;

            _colNumber = new ColumnHeader();
            _colNumber.Text = "编号";
            _colNumber.Width = 55;
            _colName = new ColumnHeader();
            _colName.Text = "名称";
            _colName.Width = 170;
            _colOcc = new ColumnHeader();
            _colOcc.Text = "次数";
            _colOcc.Width = 45;
            // v2.2：对照列（默认宽度 0 隐藏，对照模式时展开）
            _colOldNumber = new ColumnHeader();
            _colOldNumber.Text = "旧编号";
            _colOldNumber.Width = 0;
            _colOldName = new ColumnHeader();
            _colOldName.Text = "旧名称";
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
            this.Font = new Font("Microsoft Sans Serif", 9F);
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

            // v2.2：检测是否有对比基线
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

            // 修复 S2：真正的自然排序（替代 LINQ OrderBy + 假 NaturalSort）
            List<DictEntry> sorted = new List<DictEntry>(dict.Entries);
            sorted.Sort(delegate(DictEntry a, DictEntry b) { return NaturalCompare(a.Number, b.Number); });

            // v2.2：构建 diff 查找表（按新条目引用查 DiffEntry）
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

                // v2.2：对照列填充 + 高亮
                DictDiffEntry diff = null;
                if (diffMap.TryGetValue(e, out diff))
                {
                    item.SubItems.Add(diff.OldNumber);   // 旧编号
                    item.SubItems.Add(diff.OldName);     // 旧名称
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

            _lblDictInfo.Text = dict.Entries.Count + " 条 | " + warnCount + " 警告 | " + conflictCount + " 冲突";

            // v2.2：状态栏显示差异概要
            if (_currentDiff != null)
            {
                string summary = DictDiff.Summarize(_currentDiff);
                _lblStatus.Text = "字典已更新 — " + summary;
                _lblStatus.ForeColor = SystemColors.Highlight;  // 跟随系统高亮色
            }
            else
            {
                _lblStatus.Text = "字典已加载。";
                _lblStatus.ForeColor = SystemColors.ControlText;
            }
        }

        // v2.2：按差异状态高亮列表项
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

        // v2.2：切换对照列显示
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
            _lblDictInfo.Text = "未加载字典";
            _lblStatus.Text = "请将 <dwg名>.dict.json 放在 DWG 同目录。";
        }

        // ===== 修复 S2：真正的自然排序 =====
        // 逐字符比较，数字段按数值比较（"10" > "9"，"10a" > "9b"）
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
                    // 提取 a 中的数字
                    int startA = i;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    int numA = 0;
                    int.TryParse(a.Substring(startA, i - startA), out numA);

                    // 提取 b 中的数字
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
                    _lblStatus.Text = "已选: [" + entry.Number + "] " + entry.Name;
            }
        }

        private void NumTextHeight_ValueChanged(object sender, EventArgs e)
        {
            PatPaletteCommand.TextHeight = (double)_numTextHeight.Value;
        }

        // v2：箭头开关按钮 — 切换后续新建引线是否带箭头
        private void BtnArrow_Click(object sender, EventArgs e)
        {
            PatPaletteCommand.HasArrowHead = !PatPaletteCommand.HasArrowHead;
            UpdateArrowButtonText();
            _lblStatus.Text = "箭头: " + (PatPaletteCommand.HasArrowHead ? "开" : "关") + "（影响后续新建引线）";
        }

        private void UpdateArrowButtonText()
        {
            if (_btnArrow != null)
                _btnArrow.Text = "箭头:" + (PatPaletteCommand.HasArrowHead ? "开" : "关");
        }

        // v2.1：箭头大小调节 — 创建引线时同步到 PAT_DIM 样式
        private void NumArrowSize_ValueChanged(object sender, EventArgs e)
        {
            PatPaletteCommand.ArrowSize = (double)_numArrowSize.Value;
        }

        // v2.1：样条/直线开关 — 切换后续新建引线的线型
        private void BtnSpline_Click(object sender, EventArgs e)
        {
            PatPaletteCommand.IsSplined = !PatPaletteCommand.IsSplined;
            UpdateSplineButtonText();
            _lblStatus.Text = "线型: " + (PatPaletteCommand.IsSplined ? "样条曲线" : "直线段") + "（影响后续新建引线）";
        }

        private void UpdateSplineButtonText()
        {
            if (_btnSpline != null)
                _btnSpline.Text = PatPaletteCommand.IsSplined ? "线型:样条" : "线型:直线";
        }

        // v2.2：全选 PAT 文字 — 触发 PatSelectAll 命令
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

        // v2.2：对照开关 — 切换旧版字典列显示
        private void BtnCompare_Click(object sender, EventArgs e)
        {
            _compareMode = !_compareMode;
            UpdateCompareColumns();
            _lblStatus.Text = _compareMode ? "已显示旧版对照列。" : "已隐藏旧版对照列。";
        }

        private void LstEntries_DoubleClick(object sender, EventArgs e)
        {
            if (_lstEntries.SelectedItems.Count == 0) return;
            PaletteEntry entry = _lstEntries.SelectedItems[0].Tag as PaletteEntry;
            if (entry == null) return;

            PatPaletteCommand.PendingNumber = entry.Number;
            PatPaletteCommand.PendingName = entry.Name != null ? entry.Name : "";
            _lblStatus.Text = "已装填: [" + entry.Number + "] — 请在图纸中点击";

            var doc = AppAcad.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                doc.Editor.WriteMessage("\n已切换到 #" + PatPaletteCommand.PendingNumber + " " + PatPaletteCommand.PendingName + "\n");
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
                    _lblStatus.Text = "已重载。";
                }
                else
                    ShowNoDict();
            }
            catch (System.Exception ex)
            {
                _lblStatus.Text = "加载失败: " + ex.Message;
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
                    _lblStatus.Text = "未找到 .dict.json";
                    return;
                }
                // .NET 2.0: Process.Start(string) 默认使用 shell execute
                System.Diagnostics.Process.Start(dictPath);
            }
            catch (System.Exception ex)
            {
                _lblStatus.Text = "打开失败: " + ex.Message;
            }
        }

        private void BtnConflicts_Click(object sender, EventArgs e)
        {
            if (_currentDict == null) { _lblStatus.Text = "未加载字典"; return; }

            int warnCount = _currentDict.Warnings != null ? _currentDict.Warnings.Count : 0;
            int conflictCount = 0;
            foreach (DictEntry ent in _currentDict.Entries)
                conflictCount += (ent.Conflicts != null ? ent.Conflicts.Count : 0);

            if (warnCount == 0 && conflictCount == 0)
            {
                MessageBox.Show("无冲突。", "冲突检查");
                return;
            }

            string msg = "";
            if (warnCount > 0)
            {
                msg += "=== 警告 ===\r\n";
                foreach (string w in _currentDict.Warnings)
                    msg += "  * " + w + "\r\n";
            }
            if (conflictCount > 0)
            {
                msg += "\r\n=== 冲突 ===\r\n";
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
            MessageBox.Show(msg, "冲突检查");
        }

        /// <summary>
        /// 删除所有 PAT_DIM 引线及其关联的 MText（原 Explode 按钮，修复 S6）。
        /// 2007 的 Leader 已是基本实体，无需 explode。
        /// 修复 D2：面板事件中修改数据库需要 LockDocument。
        /// </summary>
        private void BtnDelete_Click(object sender, EventArgs e)
        {
            var doc = AppAcad.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

            PatentMarkerApp.RawLog("BtnDelete: starting...");

            // 确认对话框
            DialogResult confirm = MessageBox.Show(
                "确定删除所有 PAT_DIM 引线？\n此操作不可撤销（请在命令行输入 UNDO 恢复）。",
                "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            // 修复 D2：锁定文档
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

                        // 统一使用 PatEntityHelper 识别（修复 S3）
                        if (!PatEntityHelper.IsPatEntity(leader, tr)) { skipped++; continue; }

                        toDelete.Add(entId);
                        // 收集关联的 MText
                        if (!leader.Annotation.IsNull)
                            mtextToDelete.Add(leader.Annotation);
                    }

                    // 删除 Leader
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

                    // 删除关联的 MText
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
                    _lblStatus.Text = "已删除 " + deleted + " 条引线（跳过 " + skipped + "）。";
                    doc.Editor.WriteMessage("\n已删除 " + deleted + " 条 PAT_DIM 引线（跳过 " + skipped + "）。\n");
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
