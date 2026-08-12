using PatentMarker.IO;
using PatentMarker.I18n;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PatentMarker.Palette
{
    /// <summary>
    /// WinForms 字典面板 — AutoCAD 2013/2014 (.NET 4.0) 版本。
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
        private Button _btnPaste;      // v4.0：粘贴识别
        private Button _btnAddEntry;   // v4.0：新增条目
        private Button _btnArrow;
        private Button _btnLeader;
        private Button _btnUnderline;
        private NumericUpDown _numArrowSize;
        private Button _btnSpline;
        private Button _btnPoints;   // v3.1：点数模式切换（无限/三点）
        private Button _btnBrace;    // v4.1：参数化矢量大括号
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

        private readonly DictPaletteSession _session = new DictPaletteSession();
        private readonly DictPaletteWorkflow _workflow = new DictPaletteWorkflow();
        private readonly DictPaletteViewRenderer _view;
        private DictModel _currentDict { get { return _session.CurrentDict; } }
        private System.Windows.Forms.Timer _autoRefreshTimer;

        private List<DictDiffEntry> _currentDiff { get { return _session.CurrentDiff; } }
        private bool _compareMode;

        public DictPaletteControl()
        {
            InitializeComponent();
            _view = new DictPaletteViewRenderer(_lstEntries, _lblDictInfo, _lblStatus, _colOldNumber, _colOldName);
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
                if (!_workflow.IsFileChanged()) return;
                var dict = _workflow.LoadCurrent();
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
            _numTextHeight.Minimum = 0m;
            _numTextHeight.Maximum = decimal.MaxValue;
            _numTextHeight.Value = (decimal)PatSettingsStore.Current.TextHeight;
            _numTextHeight.DecimalPlaces = 2;
            _numTextHeight.Increment = 0.5m;
            _numTextHeight.ReadOnly = false;
            _numTextHeight.ValueChanged += new EventHandler(NumTextHeight_ValueChanged);

            Button heightReset = new Button();
            heightReset.Text = Strings.Palette_Reset;
            heightReset.AutoSize = true;
            heightReset.Click += delegate(object s, EventArgs ev) { _numTextHeight.Value = (decimal)PatSettingsStore.DefaultTextHeight; };

            heightPanel.Controls.AddRange(new Control[] { heightLbl, _numTextHeight, heightReset });

            // 样式栏 | Style bar
            FlowLayoutPanel stylePanel = new FlowLayoutPanel();
            stylePanel.Dock = DockStyle.Top;
            stylePanel.Height = 105;   // leader/underline switches plus style controls
            stylePanel.WrapContents = true;
            stylePanel.FlowDirection = FlowDirection.LeftToRight;
            stylePanel.Padding = new Padding(0, 2, 0, 2);

            _btnArrow = new Button();
            _btnArrow.AutoSize = true;
            _btnArrow.Click += new EventHandler(BtnArrow_Click);
            UpdateArrowButtonText();

            _btnLeader = new Button();
            _btnLeader.AutoSize = true;
            _btnLeader.Click += new EventHandler(BtnLeader_Click);
            UpdateLeaderButtonText();

            _btnUnderline = new Button();
            _btnUnderline.AutoSize = true;
            _btnUnderline.Click += new EventHandler(BtnUnderline_Click);
            UpdateUnderlineButtonText();

            Label arrowSizeLbl = new Label();
            arrowSizeLbl.Text = Strings.Palette_ArrowSize;
            arrowSizeLbl.AutoSize = true;
            arrowSizeLbl.TextAlign = ContentAlignment.MiddleLeft;

            _numArrowSize = new NumericUpDown();
            _numArrowSize.Width = 50;
            // v2.4：去除上下限，允许手动输入（与字体大小框一致）
            _numArrowSize.Minimum = 0m;
            _numArrowSize.Maximum = decimal.MaxValue;
            _numArrowSize.Value = (decimal)PatSettingsStore.Current.ArrowSize;
            _numArrowSize.DecimalPlaces = 1;
            _numArrowSize.Increment = 0.5m;
            _numArrowSize.ReadOnly = false;
            _numArrowSize.ValueChanged += new EventHandler(NumArrowSize_ValueChanged);

            _btnSpline = new Button();
            _btnSpline.AutoSize = true;
            _btnSpline.Click += new EventHandler(BtnSpline_Click);
            UpdateSplineButtonText();

            // v3.1：点数模式按钮（无限/三点），与线型按钮正交
            _btnPoints = new Button();
            _btnPoints.AutoSize = true;
            _btnPoints.Click += new EventHandler(BtnPoints_Click);
            UpdatePointsButtonText();

            _btnBrace = new Button();
            _btnBrace.AutoSize = true;
            _btnBrace.Click += new EventHandler(BtnBrace_Click);
            _btnBrace.Text = Strings.Palette_Brace;

            stylePanel.Controls.AddRange(new Control[] { _btnLeader, _btnUnderline, _btnArrow, arrowSizeLbl, _numArrowSize, _btnSpline, _btnPoints, _btnBrace });

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

            // v4.0：粘贴识别入口 | Paste-recognize entry
            _btnPaste = new Button();
            _btnPaste.Text = Strings.Palette_PasteRecognize;
            _btnPaste.AutoSize = true;
            _btnPaste.Margin = new Padding(0, 0, 4, 2);

            // v4.0：新增条目入口 | Add-entry entry
            _btnAddEntry = new Button();
            _btnAddEntry.Text = Strings.Palette_AddEntry;
            _btnAddEntry.AutoSize = true;
            _btnAddEntry.Margin = new Padding(0, 0, 4, 2);

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
                _btnReload, _btnOpen, _btnPaste, _btnAddEntry, _btnCompare, _btnLanguage
            });
            _btnReload.Click += new EventHandler(BtnReload_Click);
            _btnOpen.Click += new EventHandler(BtnOpen_Click);
            _btnPaste.Click += new EventHandler(BtnPaste_Click);
            _btnAddEntry.Click += new EventHandler(BtnAddEntry_Click);
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
            _lstEntries.MouseDown += new MouseEventHandler(LstEntries_MouseDown);
            _lstEntries.KeyDown += new KeyEventHandler(LstEntries_KeyDown);
            ContextMenuStrip entryContextMenu = new ContextMenuStrip();
            ToolStripMenuItem editEntryMenuItem = new ToolStripMenuItem(Strings.Edit_TitleEdit);
            editEntryMenuItem.Click += new EventHandler(LstEntries_EditRequested);
            entryContextMenu.Items.Add(editEntryMenuItem);
            _lstEntries.ContextMenuStrip = entryContextMenu;

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
            if (_btnPaste != null) _btnPaste.Text = Strings.Palette_PasteRecognize;
            if (_btnAddEntry != null) _btnAddEntry.Text = Strings.Palette_AddEntry;
            if (_btnCompare != null) _btnCompare.Text = Strings.Palette_Compare;
            if (_btnLanguage != null) _btnLanguage.Text = Strings.Palette_Language;
            if (_btnBrace != null) _btnBrace.Text = Strings.Palette_Brace;
            UpdateLeaderButtonText();
            UpdateUnderlineButtonText();
            if (_colNumber != null) _colNumber.Text = Strings.Col_Number;
            if (_colName != null) _colName.Text = Strings.Col_Name;
            if (_colOcc != null) _colOcc.Text = Strings.Col_Occ;
            if (_colOldNumber != null) _colOldNumber.Text = Strings.Col_OldNumber;
            if (_colOldName != null) _colOldName.Text = Strings.Col_OldName;
            UpdateArrowButtonText();
            UpdateSplineButtonText();
            UpdatePointsButtonText();

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
            _session.Load(dict, _workflow.PreviousModel);
            _btnCompare.Enabled = _currentDiff != null;
            if (_currentDiff == null) _compareMode = false;
            _view.RenderDictionary(dict, _session, _compareMode);
        }

        public void ApplyRuntimeSettings()
        {
            if (_numTextHeight != null)
                _numTextHeight.Value = (decimal)PatSettingsStore.Current.TextHeight;
            if (_numArrowSize != null)
                _numArrowSize.Value = (decimal)PatSettingsStore.Current.ArrowSize;
            UpdateArrowButtonText();
            UpdateLeaderButtonText();
            UpdateUnderlineButtonText();
            UpdateSplineButtonText();
            UpdatePointsButtonText();
        }

        public void ShowNoDict()
        {
            _session.Clear();
            _view.ShowNoDictionary();
        }

        // ===== 事件 =====

        private void TxtSearch_TextChanged(object sender, EventArgs e)
        {
            string keyword = _txtSearch.Text.Trim().ToLowerInvariant();
            _view.RenderFiltered(_session.Filter(keyword));
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

        private void BtnPoints_Click(object sender, EventArgs e)
        {
            PatPaletteCommand.ThreePointMode = !PatPaletteCommand.ThreePointMode;
            UpdatePointsButtonText();
            string desc = PatPaletteCommand.ThreePointMode ? Strings.Status_PointsThreeDesc : Strings.Status_PointsUnlimitedDesc;
            _lblStatus.Text = string.Format(Strings.Status_PointsToggled, desc);
        }

        private void UpdatePointsButtonText()
        {
            if (_btnPoints != null)
                _btnPoints.Text = PatPaletteCommand.ThreePointMode ? Strings.Palette_PointsThree : Strings.Palette_PointsUnlimited;
        }

        private void BtnLeader_Click(object sender, EventArgs e)
        {
            PatPaletteCommand.HasLeader = !PatPaletteCommand.HasLeader;
            UpdateLeaderButtonText();
            string state = PatPaletteCommand.HasLeader ? Strings.Palette_On : Strings.Palette_Off;
            _lblStatus.Text = string.Format(Strings.Status_LeaderToggled, state);
        }

        private void UpdateLeaderButtonText()
        {
            if (_btnLeader != null)
            {
                string state = PatPaletteCommand.HasLeader ? Strings.Palette_On : Strings.Palette_Off;
                _btnLeader.Text = string.Format(Strings.Palette_LeaderOnOff, state);
            }
        }

        private void BtnUnderline_Click(object sender, EventArgs e)
        {
            PatPaletteCommand.UnderlineText = !PatPaletteCommand.UnderlineText;
            UpdateUnderlineButtonText();
            string state = PatPaletteCommand.UnderlineText ? Strings.Palette_On : Strings.Palette_Off;
            _lblStatus.Text = string.Format(Strings.Status_UnderlineToggled, state);
        }

        private void UpdateUnderlineButtonText()
        {
            if (_btnUnderline != null)
            {
                string state = PatPaletteCommand.UnderlineText ? Strings.Palette_On : Strings.Palette_Off;
                _btnUnderline.Text = string.Format(Strings.Palette_UnderlineOnOff, state);
            }
        }

        private void BtnBrace_Click(object sender, EventArgs e)
        {
            try
            {
                var doc = IO.RuntimeHost.ActiveDocument;
                if (doc != null)
                    doc.SendStringToExecute("PATBRACE\n", false, false, false);
            }
            catch (System.Exception ex)
            {
                PatentMarkerApp.RawLog("BtnBrace error: " + ex.Message);
            }
        }

        private void BtnCompare_Click(object sender, EventArgs e)
        {
            _compareMode = !_compareMode;
            _view.SetCompareMode(_compareMode);
            _lblStatus.Text = _compareMode ? Strings.Status_CompareShown : Strings.Status_CompareHidden;
        }

        private void LstEntries_DoubleClick(object sender, EventArgs e)
        {
            MarkSelectedEntry();
        }

        private void LstEntries_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            ListViewHitTestInfo hit = _lstEntries.HitTest(e.Location);
            if (hit.Item == null) return;

            _lstEntries.SelectedItems.Clear();
            hit.Item.Selected = true;
            hit.Item.Focused = true;
            _lstEntries.Focus();
        }

        private void LstEntries_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.F2) return;
            e.Handled = true;
            EditSelectedEntry();
        }

        private void LstEntries_EditRequested(object sender, EventArgs e)
        {
            EditSelectedEntry();
        }

        /// <summary>默认交互：双击条目直接进入 PATMARK，不再打开编辑对话框。</summary>
        private void MarkSelectedEntry()
        {
            if (_lstEntries.SelectedItems.Count == 0) return;
            PaletteEntry entry = _lstEntries.SelectedItems[0].Tag as PaletteEntry;
            if (entry == null) return;

            if (_currentDict == null)
            {
                _lblStatus.Text = Strings.Palette_DictNotLoaded;
                return;
            }

            DictEntry target = _workflow.FindEntry(_currentDict, entry.Number);
            if (target == null)
            {
                _lblStatus.Text = string.Format(Strings.Status_LoadFailed, Strings.Palette_DictNotLoaded);
                return;
            }

            PatPaletteCommand.PendingNumber = target.Number;
            PatPaletteCommand.PendingName = target.Name != null ? target.Name : "";
            _lblStatus.Text = string.Format(Strings.Status_Loaded, target.Number);

            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc != null)
            {
                doc.Editor.WriteMessage(string.Format(Strings.Status_LoadedCmd,
                    PatPaletteCommand.PendingNumber, PatPaletteCommand.PendingName));
                doc.SendStringToExecute("PATMARK ", false, false, false);
            }
        }

        /// <summary>编辑入口：右键菜单或 F2 打开单条目编辑对话框。</summary>
        private void EditSelectedEntry()
        {
            if (_lstEntries.SelectedItems.Count == 0) return;
            PaletteEntry entry = _lstEntries.SelectedItems[0].Tag as PaletteEntry;
            if (entry == null) return;

            if (_currentDict == null)
            {
                _lblStatus.Text = Strings.Palette_DictNotLoaded;
                return;
            }
            string dictPath = _workflow.ResolveDictPath();
            if (dictPath == null)
            {
                _lblStatus.Text = Strings.Status_NoDictFile;
                return;
            }

            DictEntry target = _workflow.FindEntry(_currentDict, entry.Number);
            if (target == null)
            {
                _lblStatus.Text = string.Format(Strings.Status_LoadFailed, Strings.Edit_DeleteFailed);
                return;
            }

            string oldNumber = target.Number;

            using (EditEntryDialog dlg = new EditEntryDialog(_currentDict, target, dictPath))
            {
                DialogResult r = dlg.ShowDialog(this);
                if (r == DialogResult.OK || r == DialogResult.Abort)
                {
                    // 保存 / 删除：刷新面板
                    var dict = _workflow.LoadCurrent();
                    if (dict != null) LoadDict(dict);
                    else ShowNoDict();
                    _lblStatus.Text = Strings.Status_DictAutoUpdated;

                    // v4.0：编号变更 → 同步图纸标注文字（多条同号全改）+ Regen
                    if (r == DialogResult.OK && !NumberIdentity.AreEqual(target.Number, oldNumber))
                    {
                        RenameLeadersInDrawing(oldNumber, target.Number);
                    }
                }
            }
        }

        /// <summary>
        /// v4.0：编号变更后同步图纸标注文字（PAT_STYLE 引线文字 oldNumber → newNumber），
        /// 修改后 Regen 刷新显示。返回修改条数。
        /// </summary>
        private int RenameLeadersInDrawing(string oldNumber, string newNumber)
        {
            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return 0;

            int changed = 0;
            try
            {
                changed = DictPaletteCadService.RenameNumber(doc, oldNumber, newNumber);

                if (changed > 0)
                {
                    doc.Editor.Regen();
                    doc.Editor.WriteMessage(string.Format(Strings.Status_NumberSyncedCmd, changed, oldNumber, newNumber));
                }
                _lblStatus.Text = string.Format(Strings.Status_NumberSynced, changed, oldNumber, newNumber);
            }
            catch (System.Exception ex)
            {
                PatentMarkerApp.RawLog("RenameLeadersInDrawing error: " + ex.Message);
                _lblStatus.Text = string.Format(Strings.Status_NumberSyncFailed, ex.Message);
            }
            return changed;
        }

        private void BtnReload_Click(object sender, EventArgs e)
        {
            try
            {
                var dict = _workflow.ReloadCurrent();
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

        /// <summary>
        /// v4.0：打开粘贴识别对话框；写回成功后刷新面板列表。
        /// </summary>
        private void BtnPaste_Click(object sender, EventArgs e)
        {
            try
            {
                using (PasteRecognizeDialog dlg = new PasteRecognizeDialog())
                {
                    DialogResult r = dlg.ShowDialog(this);
                    if (r == DialogResult.OK)
                    {
                        var dict = _workflow.LoadCurrent();
                        if (dict != null) LoadDict(dict);
                        else ShowNoDict();
                        _lblStatus.Text = Strings.Status_DictAutoUpdated;
                    }
                }
            }
            catch (System.Exception ex)
            {
                _lblStatus.Text = string.Format(Strings.Status_LoadFailed, ex.Message);
                PatentMarkerApp.RawLog("BtnPaste error: " + ex.Message);
            }
        }

        /// <summary>
        /// v4.0：打开新增条目对话框；写回成功后刷新面板列表。
        /// </summary>
        private void BtnAddEntry_Click(object sender, EventArgs e)
        {
            try
            {
                if (_currentDict == null)
                {
                    _lblStatus.Text = Strings.Palette_DictNotLoaded;
                    return;
                }
                string dictPath = _workflow.ResolveDictPath();
                if (dictPath == null)
                {
                    _lblStatus.Text = Strings.Status_NoDictFile;
                    return;
                }

                using (EditEntryDialog dlg = new EditEntryDialog(_currentDict, null, dictPath))
                {
                    DialogResult r = dlg.ShowDialog(this);
                    if (r == DialogResult.OK)
                    {
                        var dict = _workflow.LoadCurrent();
                        if (dict != null) LoadDict(dict);
                        else ShowNoDict();
                        _lblStatus.Text = Strings.Status_DictAutoUpdated;
                    }
                }
            }
            catch (System.Exception ex)
            {
                _lblStatus.Text = string.Format(Strings.Status_LoadFailed, ex.Message);
                PatentMarkerApp.RawLog("BtnAddEntry error: " + ex.Message);
            }
        }

        private void BtnOpen_Click(object sender, EventArgs e)
        {
            try
            {
                var doc = IO.RuntimeHost.ActiveDocument;
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

    }

}
