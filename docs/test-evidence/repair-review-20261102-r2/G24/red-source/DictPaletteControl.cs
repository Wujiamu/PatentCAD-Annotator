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
        private Button _btnVisibility;
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
        private Button _btnCheck;
        private Button _btnAlign;
        private Button _btnArbitrate;
private Button _btnRenameRetry;   // v4.0：改号同步失败的显式重试
        // R-004（G21）：欠账的显式放弃入口，与重试按钮同生命周期。
        private Button _btnRenameDiscard;
        // R-004：待同步操作按图纸隔离，保存原 Document／完整字典路径／成功写出的
        // 内容身份／旧号新号与失败信息。只活在本面板会话内，不是持久任务队列。
        private readonly PendingRenameRegistry _pendingRenames = new PendingRenameRegistry();
        // 字典在失败后变化时，重试需要用户对当前字典再做一次显式确认。
        private bool _pendingRenameAwaitingReconfirm;
        private int _lastCheckVersion = -1;
        private object _lastCheckDocumentKey;
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
                // 面板点击可能发生在另一个 CAD 命令期间；命令结束后由这里补发一次请求。
                PatPaletteCommand.TryDispatchPendingForCurrentDocument();

                // v5.1：PATCHECK 完成后重渲染列表（未标注条目高亮）
                object checkDocumentKey = GetCurrentCheckDocumentKey();
                int checkVersion = Commands.PatCheckResult.GetVersion(checkDocumentKey);
                if (!object.ReferenceEquals(checkDocumentKey, _lastCheckDocumentKey) ||
                    checkVersion != _lastCheckVersion)
                {
                    _lastCheckDocumentKey = checkDocumentKey;
                    _lastCheckVersion = checkVersion;
                    RefreshListRendering();
                    UpdateConflictState();
                    return;
                }
                UpdateConflictState();
                UpdateVisibilityButtonText();
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

        /// <summary>按当前搜索关键字重渲染列表（保留未标注高亮）。</summary>
        private void RefreshListRendering()
        {
            if (_currentDict == null) return;
            _view.SetCheckDocumentKey(GetCurrentCheckDocumentKey());
            string keyword = _txtSearch.Text.Trim().ToLowerInvariant();
            _view.RenderFiltered(_session.Filter(keyword), _currentDiff, _compareMode);
        }

        private static object GetCurrentCheckDocumentKey()
        {
            return IO.RuntimeHost.ActiveDocument;
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

            _btnVisibility = new Button();
            _btnVisibility.AutoSize = true;
            _btnVisibility.Margin = new Padding(0, 0, 4, 2);
            UpdateVisibilityButtonText();

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

            // v5.1：检测（PATCHECK 漏标）与对齐（PATALIGN）入口
            _btnCheck = new Button();
            _btnCheck.Text = Strings.Palette_Check;
            _btnCheck.AutoSize = true;
            _btnCheck.Margin = new Padding(0, 0, 4, 2);

            _btnAlign = new Button();
            _btnAlign.Text = Strings.Palette_Align;
            _btnAlign.AutoSize = true;
            _btnAlign.Margin = new Padding(0, 0, 4, 2);

            _btnArbitrate = new Button();
            _btnArbitrate.Text = Strings.Palette_Arbitrate;
            _btnArbitrate.AutoSize = true;
            _btnArbitrate.Margin = new Padding(0, 0, 4, 2);
            _btnArbitrate.Enabled = false;

            // v4.0：改号同步失败后的显式重试入口 | Explicit retry after a failed renumber sync
            _btnRenameRetry = new Button();
            _btnRenameRetry.Text = Strings.Palette_RenameRetry;
            _btnRenameRetry.AutoSize = true;
            _btnRenameRetry.Margin = new Padding(0, 0, 4, 2);
            _btnRenameRetry.Visible = false;

            // R-004（G21）：待同步操作只有「完成」或「用户显式放弃」两条出路。
            // 收紧守卫后每次改号都会被拦，放弃入口必须真实可达，否则该图纸在
            // 本次会话内再也改不了任何编号。
            _btnRenameDiscard = new Button();
            _btnRenameDiscard.Text = RenameSyncText.DiscardPendingButton();
            _btnRenameDiscard.AutoSize = true;
            _btnRenameDiscard.Margin = new Padding(0, 0, 4, 2);
            _btnRenameDiscard.Visible = false;

            // v2.3：语言切换按钮 | Language toggle button
            _btnLanguage = new Button();
            _btnLanguage.Text = Strings.Palette_Language;
            _btnLanguage.AutoSize = true;
            _btnLanguage.Margin = new Padding(0, 0, 4, 2);
            _btnLanguage.Click += new EventHandler(BtnLanguage_Click);

            btnPanel.Controls.AddRange(new Control[] {
                _btnReload, _btnOpen, _btnVisibility, _btnPaste, _btnAddEntry, _btnCompare,
                _btnCheck, _btnAlign, _btnArbitrate, _btnRenameRetry, _btnRenameDiscard, _btnLanguage
            });
            _btnReload.Click += new EventHandler(BtnReload_Click);
            _btnOpen.Click += new EventHandler(BtnOpen_Click);
            _btnVisibility.Click += new EventHandler(BtnVisibility_Click);
            _btnPaste.Click += new EventHandler(BtnPaste_Click);
            _btnAddEntry.Click += new EventHandler(BtnAddEntry_Click);
            _btnRenameRetry.Click += new EventHandler(BtnRenameRetry_Click);
            _btnRenameDiscard.Click += new EventHandler(BtnRenameDiscard_Click);
            _btnCompare.Click += new EventHandler(BtnCompare_Click);
            _btnCheck.Click += new EventHandler(BtnCheck_Click);
            _btnAlign.Click += new EventHandler(BtnAlign_Click);
            _btnArbitrate.Click += new EventHandler(BtnArbitrate_Click);

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
            UpdateVisibilityButtonText();
            if (_btnPaste != null) _btnPaste.Text = Strings.Palette_PasteRecognize;
            if (_btnAddEntry != null) _btnAddEntry.Text = Strings.Palette_AddEntry;
            if (_btnCompare != null) _btnCompare.Text = Strings.Palette_Compare;
            if (_btnCheck != null) _btnCheck.Text = Strings.Palette_Check;
            if (_btnAlign != null) _btnAlign.Text = Strings.Palette_Align;
            if (_btnArbitrate != null) _btnArbitrate.Text = Strings.Palette_Arbitrate;
            if (_btnRenameDiscard != null) _btnRenameDiscard.Text = RenameSyncText.DiscardPendingButton();
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
                LoadDict(_currentDict, false);
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
            LoadDict(dict, true);
        }

        /// <summary>
        /// Loads the current dictionary. A document activation can preserve a
        /// valid PATCHECK result for that document; actual dictionary changes
        /// pass true to invalidate it.
        /// </summary>
        public void LoadDict(DictModel dict, bool clearCheckResult)
        {
            if (dict == null) { ShowNoDict(); return; }
            object checkDocumentKey = GetCurrentCheckDocumentKey();
            if (clearCheckResult)
                Commands.PatCheckResult.Clear(checkDocumentKey);
            _view.SetCheckDocumentKey(checkDocumentKey);
            _session.Load(dict, _workflow.PreviousModel);
            _btnCompare.Enabled = _currentDiff != null;
            if (_currentDiff == null) _compareMode = false;
            _view.RenderDictionary(dict, _session, _compareMode);
            UpdateConflictState();
            UpdateVisibilityButtonText();
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
            object checkDocumentKey = GetCurrentCheckDocumentKey();
            Commands.PatCheckResult.Clear(checkDocumentKey);
            _view.SetCheckDocumentKey(checkDocumentKey);
            _session.Clear();
            _view.ShowNoDictionary();
            UpdateConflictState();
            UpdateVisibilityButtonText();
        }

        // ===== 事件 =====

        private void TxtSearch_TextChanged(object sender, EventArgs e)
        {
            _view.SetCheckDocumentKey(GetCurrentCheckDocumentKey());
            string keyword = _txtSearch.Text.Trim().ToLowerInvariant();
            _view.RenderFiltered(_session.Filter(keyword), _currentDiff, _compareMode);
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
                    PatPaletteCommand.RequestPanelCommand(doc, "PATBRACE");
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

        // v5.1：检测（PATCHECK 漏标清单，完成后本面板高亮未标注条目）
        private void BtnCheck_Click(object sender, EventArgs e)
        {
            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            PatPaletteCommand.RequestPanelCommand(doc, "PATCHECK");
        }

        // v5.1：对齐（PATALIGN v2：先选中标注再点击本按钮）
        private void BtnAlign_Click(object sender, EventArgs e)
        {
            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            PatPaletteCommand.RequestPanelCommand(doc, "PATALIGN");
        }

        private void BtnArbitrate_Click(object sender, EventArgs e)
        {
            try
            {
                string dictPath = _workflow.ResolveDictPath();
                if (dictPath == null)
                {
                    UpdateConflictState();
                    return;
                }

                // R-034：恢复入口只依赖磁盘上的当前文件与备份，不依赖面板缓存模型。
                // 当前 JSON 损坏时 _currentDict 可能为 null 或已过期，若把缓存身份
                // 与新文件字节比对，用户就再也无法从合法备份恢复。
                DictConflict.RecoverySelection selection = _workflow.CaptureRecoverySelection(dictPath);
                if (selection == null)
                {
                    _lblStatus.Text = Strings.Dict_Changed;
                    UpdateConflictState();
                    return;
                }
                if (!DictPaletteWorkflow.IsCurrentContext(selection.Document, dictPath))
                {
                    _lblStatus.Text = Strings.Dict_Changed;
                    return;
                }
                // R-034（G20）：不以「备份能否解析」决定整个对话框能否打开。
                // 备份损坏只约束「恢复 CAD 版」这一个动作；「采用 Word 版」由
                // 当前文件能否解析约束。两者均坏时对话框给出明确原因并保留「稍后」。
                // 采用当前版本仍必须由用户显式选择，不自动删除坏备份。

                using (ArbitrateDialog dlg = new ArbitrateDialog(selection))
                {
                    DialogResult result = dlg.ShowDialog(this);
                    DictConflict.RecoveryOutcome outcome = dlg.Outcome;
                    if (result == DialogResult.OK || result == DialogResult.Yes)
                    {
                        if (result == DialogResult.Yes)
                        {
                            DictModel dict = _workflow.ReloadAfterRecovery(dictPath, outcome.Model);
                            if (dict != null) LoadDict(dict);
                            else ShowNoDict();
                            _lblStatus.Text = Strings.Conflict_RestoreOk;
                        }
                        else
                        {
                            DictModel dict = _workflow.ReloadCurrent();
                            if (dict != null) LoadDict(dict);
                            else ShowNoDict();
                            _lblStatus.Text = Strings.Conflict_KeepWordOk;
                        }
                    }
                }
                UpdateConflictState();
            }
            catch (System.Exception ex)
            {
                _lblStatus.Text = string.Format(Strings.Conflict_Failed, ex.Message);
                PatentMarkerApp.RawLog("BtnArbitrate error: " + ex.Message);
            }
        }

        private void UpdateConflictState()
        {
            if (_btnArbitrate == null) return;
            string dictPath = _workflow.ResolveDictPath();
            // R-034：是否有待裁决备份只看磁盘。当前字典解析失败时也必须能进入
            // 「从合法备份恢复」，否则坏文件就永久卡住。
            bool pending = dictPath != null && _workflow.IsPendingConflict(dictPath);
            _btnArbitrate.Enabled = pending;
            if (pending && _lblStatus != null &&
                (_lblStatus.Text == Strings.Status_DictLoaded ||
                 _lblStatus.Text == Strings.Status_Ready))
                _lblStatus.Text = Strings.Conflict_StatusDetected;
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
            if (entry == null || entry.IsRemoved) return;

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

            var doc = IO.RuntimeHost.ActiveDocument;
            string name = target.Name != null ? target.Name : "";
            if (!PatPaletteCommand.RequestMark(doc, target.Number, name))
            {
                _lblStatus.Text = string.Format(Strings.Status_LoadFailed, "PATMARK");
                return;
            }

            _lblStatus.Text = string.Format(Strings.Status_Loaded, target.Number);
            doc.Editor.WriteMessage(string.Format(Strings.Status_LoadedCmd,
                target.Number, name));
        }

        /// <summary>编辑入口：右键菜单或 F2 打开单条目编辑对话框。</summary>
        private void EditSelectedEntry()
        {
            if (_lstEntries.SelectedItems.Count == 0) return;
            PaletteEntry entry = _lstEntries.SelectedItems[0].Tag as PaletteEntry;
            if (entry == null || entry.IsRemoved) return;

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
            object document = IO.RuntimeHost.ActiveDocument;
            string identity = DictLoader.GetIdentity(_currentDict, dictPath);
            if (identity == null || !DictPaletteWorkflow.IsCurrentContext(document, dictPath))
            {
                _lblStatus.Text = Strings.Dict_Changed;
                return;
            }

            // R-004：已有待同步操作时，后续相关改号先要求完成或显式放弃原操作。
            // 守卫交给对话框，在它写 JSON 之前生效，因此拦截发生在第二次写入前。
            PendingRenameGuard guard = new PendingRenameGuard(_pendingRenames, document, dictPath);
            using (EditEntryDialog dlg = new EditEntryDialog(
                _currentDict, target, dictPath, document, identity, guard))
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
                        // 首次同步也使用写回后的真实上下文，不在对话框关闭后
                        // 盲用另一个 ActiveDocument。
                        string writtenIdentity = DictLoader.GetIdentity(_currentDict, dictPath);
                        RenameSyncContext context = RenameSyncContext.Capture(
                            document, dictPath, writtenIdentity);
                        RenameLeadersInDrawing(oldNumber, target.Number, context);
                    }
                }
            }
        }

        /// <summary>
        /// v4.0：编号变更后同步图纸标注文字（PAT 引线/MLeader 文字 oldNumber → newNumber），
        /// 修改后 Regen 刷新显示。返回实际更新条数。
        /// 字典与图纸不是一个事务：部分/全部失败必须显示"字典已改，图纸未完成"，
        /// 并保留失败对象、旧号与新号供用户显式重试；不回写旧 JSON 做补偿。
        ///
        /// R-004：<paramref name="context"/> 是本次 JSON 写出时的图纸／字典快照。
        /// 首次同步就使用它，重试也只在同一快照上继续；实际 CAD 事务前再核对
        /// 磁盘身份，避免对话框关闭后盲用另一个 ActiveDocument。
        /// </summary>
        private int RenameLeadersInDrawing(string oldNumber, string newNumber)
        {
            return RenameLeadersInDrawing(oldNumber, newNumber, null);
        }

        private int RenameLeadersInDrawing(string oldNumber, string newNumber,
            RenameSyncContext context)
        {
            if (context == null)
                context = RenameSyncContext.Capture(IO.RuntimeHost.ActiveDocument, _workflow.ResolveDictPath());
            if (context == null || context.Document == null) return 0;

            // 实际 CAD 事务前核对：文档仍是同一张、磁盘字节仍是确认过的那一份。
            string drift = context.DescribeDrift();
            if (drift.Length > 0)
            {
                // R-004（G21）：JSON 已经写出去了，只是图纸没同步。这不是「什么都没
                // 发生」，而是「字典已改、图纸未改」——必须登记原操作并要求重新确认，
                // 不能静默丢弃欠账。拒绝路径保留已有欠账与原因。
                PatentMarkerApp.RawLog("Renumber drawing sync refused: " + drift);
                string reason = drift == RenameSyncContext.DocumentChanged
                    ? RenameSyncText.RetryWrongDocument()
                    : Strings.Dict_Changed;
                if (RecordPendingRename(context, oldNumber, newNumber, reason, 0, 0))
                    _pendingRenameAwaitingReconfirm = true;
                ShowRenameRetry(true);
                _lblStatus.Text = reason;
                return 0;
            }

            var doc = IO.RuntimeHost.ActiveDocument;
            DictPaletteRenameResult result;
            try
            {
                result = DictPaletteCadService.RenameNumber(doc, oldNumber, newNumber);
            }
            catch (System.Exception ex)
            {
                PatentMarkerApp.RawLog("RenameLeadersInDrawing error: " + ex.Message);
                _lblStatus.Text = string.Format(Strings.Status_NumberSyncFailed, ex.Message);
                RecordPendingRename(context, oldNumber, newNumber, ex.Message, 0, 0);
                return 0;
            }

            if (result.Complete)
            {
                doc.Editor.Regen();
                doc.Editor.WriteMessage(string.Format(
                    Strings.Status_NumberSyncedCmd, result.Changed, oldNumber, newNumber));
                _lblStatus.Text = string.Format(
                    Strings.Status_NumberSynced, result.Changed, oldNumber, newNumber);
                ClearPendingRename(context.Document, oldNumber, newNumber);
                return result.Changed;
            }

            if (result.Matched == 0 && result.Error.Length == 0)
            {
                // 图纸里没有旧号标注：字典照常生效，不谎称同步过。
                _lblStatus.Text = string.Format(Strings.Status_NumberSynced, 0, oldNumber, newNumber);
                ClearPendingRename(context.Document, oldNumber, newNumber);
                return 0;
            }

            PatentMarkerApp.RawLog(string.Format(
                "Renumber drawing sync incomplete: old={0} new={1} matched={2} changed={3} error={4} {5}",
                oldNumber, newNumber, result.Matched, result.Changed, result.Error,
                string.Join(" | ", result.Failures.ToArray())));
            string detail = result.Error.Length > 0 ? result.Error : Strings.Status_NumberSyncFailed;
            RecordPendingRename(context, oldNumber, newNumber, detail,
                result.Matched, result.Changed);
            ReportRenamePending(oldNumber, newNumber, detail);
            return result.Changed;
        }

        /// <summary>
        /// R-004：把“具体的待同步操作”绑定到本次图纸与字典快照后保存。
        ///
        /// G21：单槽位——同一图纸已经欠着**另一次**操作时不覆盖它，保留原欠账
        /// 与原因。同一操作的重试／重新确认可以刷新快照。
        /// </summary>
        private bool RecordPendingRename(RenameSyncContext context,
            string oldNumber, string newNumber, string detail, int matched, int changed)
        {
            if (context == null || context.Document == null) return false;
            PendingRenameOperation operation = new PendingRenameOperation();
            operation.Document = context.Document;
            operation.DrawingPath = context.DrawingPath;
            operation.DictPath = context.DictPath;
            operation.DictIdentity = context.DictIdentity;
            operation.OldNumber = oldNumber;
            operation.NewNumber = newNumber;
            operation.Detail = detail;
            operation.Matched = matched;
            operation.Changed = changed;
            if (!_pendingRenames.Record(operation))
            {
                // 该图纸已欠着另一次操作：不覆盖、不丢弃，只记录诊断。
                PendingRenameOperation existing = _pendingRenames.Get(context.Document);
                PatentMarkerApp.RawLog(string.Format(
                    "Pending renumber kept, new failure not recorded: kept={0}>{1} ignored={2}>{3}",
                    existing == null ? "?" : existing.OldNumber,
                    existing == null ? "?" : existing.NewNumber,
                    oldNumber, newNumber));
                ShowRenameRetry(true);
                return false;
            }
            _pendingRenameAwaitingReconfirm = false;
            ShowRenameRetry(true);
            return true;
        }

        /// <summary>
        /// R-004（G21）：只清除**完成的那一次**操作。按 Document 无条件清空会让
        /// 无关操作的完成顺带抹掉另一条欠账。
        /// </summary>
        private void ClearPendingRename(object document, string oldNumber, string newNumber)
        {
            if (document == null) return;
            if (!_pendingRenames.ClearIfMatches(document, oldNumber, newNumber)) return;
            _pendingRenameAwaitingReconfirm = false;
            ShowRenameRetry(false);
        }

        /// <summary>清除该图纸的全部待同步记录（图纸关闭／面板销毁／用户放弃）。</summary>
        private void ClearPendingRename(object document)
        {
            if (document != null) _pendingRenames.Clear(document);
            _pendingRenameAwaitingReconfirm = false;
            ShowRenameRetry(false);
        }

        /// <summary>图纸关闭时释放该图纸的待同步记录，不影响其他图纸。</summary>
        internal void ReleasePendingRenameForDrawing(string drawingPath, object document)
        {
            _pendingRenames.ReleaseForDrawing(drawingPath, document);
            if (_pendingRenames.Count == 0) ShowRenameRetry(false);
        }

        /// <summary>面板销毁时清空全部待同步记录（会话结束，不残留任务）。</summary>
        public void ReleaseAllPendingRenames()
        {
            _pendingRenames.ClearAll();
            _pendingRenameAwaitingReconfirm = false;
            ShowRenameRetry(false);
        }

        /// <summary>
        /// 显式重试：只在同一图纸、同一本字典、同一份已确认内容上继续。
        /// 字典内容或路径变化后，保留失败记录但要求用户重新确认／重载，
        /// 不因“新字典恰好含同号”就执行。
        /// </summary>
        private void RetryRenameInDrawing()
        {
            PendingRenameOperation pending = _pendingRenames.Get(IO.RuntimeHost.ActiveDocument);
            if (pending == null) return;

            RenameSyncContext context = RenameSyncContext.Capture(
                IO.RuntimeHost.ActiveDocument, _workflow.ResolveDictPath());
            DictModel model = _currentDict;
            RenameRetryDecision decision = RenameRetryPlanner.Decide(
                pending, context, delegate (string number) { return _workflow.FindEntry(model, number); });

            switch (decision.Outcome)
            {
                case RenameRetryOutcome.Ready:
                    break;
                case RenameRetryOutcome.WrongDocument:
                    _lblStatus.Text = decision.Reason;
                    return;
                case RenameRetryOutcome.NeedsReconfirm:
                case RenameRetryOutcome.MissingNumber:
                    // 保留失败记录，要求用户对当前字典重新确认或重载。
                    _pendingRenameAwaitingReconfirm = true;
                    ShowRenameRetry(true);
                    _lblStatus.Text = decision.Reason;
                    return;
                default:
                    _lblStatus.Text = Strings.Dict_Changed;
                    return;
            }

            PendingRenameOperation retry = pending.Copy();
            RenameLeadersInDrawing(retry.OldNumber, retry.NewNumber, context);
        }

        /// <summary>
        /// R-004：用户对当前字典显式重新确认同一个待同步操作（换绑新快照，
        /// 保留旧号／新号与失败信息），随后在写图前再核对一次磁盘身份。
        /// </summary>
        private void ConfirmPendingRenameRetry()
        {
            PendingRenameOperation pending = _pendingRenames.Get(IO.RuntimeHost.ActiveDocument);
            if (pending == null) return;
            RenameSyncContext context = RenameSyncContext.Capture(
                IO.RuntimeHost.ActiveDocument, _workflow.ResolveDictPath());
            if (context == null || !context.SameDocumentAs(pending.Document))
            {
                _lblStatus.Text = RenameSyncText.RetryWrongDocument();
                return;
            }
            if (!_pendingRenames.Rebind(pending, context))
            {
                _lblStatus.Text = Strings.Dict_Changed;
                return;
            }
            _pendingRenameAwaitingReconfirm = false;
            PatentMarkerApp.RawLog("Pending renumber retry reconfirmed: dict=" + pending.DictPath
                + ", old=" + pending.OldNumber + ", new=" + pending.NewNumber);
            RetryRenameInDrawing();
        }

        /// <summary>
        /// R-004（G21）：用户显式放弃该图纸的待同步操作。
        /// 这是欠账除「完成」之外的唯一出路，因此必须有真实的按钮入口。
        /// </summary>
        private void BtnRenameDiscard_Click(object sender, EventArgs e)
        {
            try
            {
                DiscardPendingRename();
            }
            catch (System.Exception ex)
            {
                PatentMarkerApp.RawLog("BtnRenameDiscard error: " + ex.Message);
                _lblStatus.Text = string.Format(Strings.Status_NumberSyncFailed, ex.Message);
            }
        }

        /// <summary>用户显式放弃该图纸的待同步操作。</summary>
        private void DiscardPendingRename()
        {
            PendingRenameOperation pending = _pendingRenames.Get(IO.RuntimeHost.ActiveDocument);
            if (pending == null) return;
            PatentMarkerApp.RawLog("Pending renumber sync discarded by user: old="
                + pending.OldNumber + ", new=" + pending.NewNumber
                + ", dict=" + pending.DictPath);
            ClearPendingRename(pending.Document);
            _lblStatus.Text = Strings.Status_Ready;
        }

        private void ClearRenameRetry()
        {
            ClearPendingRename(IO.RuntimeHost.ActiveDocument);
        }

        /// <summary>重试／放弃入口只在失败后可见，避免正常流程多出按钮。</summary>
        private void ShowRenameRetry(bool visible)
        {
            if (_btnRenameRetry != null) _btnRenameRetry.Visible = visible;
            if (_btnRenameDiscard != null) _btnRenameDiscard.Visible = visible;
        }

        /// <summary>当前图纸是否有待同步操作（供状态行与测试读取）。</summary>
        public bool HasPendingRename
        {
            get { return _pendingRenames.Get(IO.RuntimeHost.ActiveDocument) != null; }
        }

        /// <summary>当前图纸的待同步操作（只读快照，供诊断与测试读取）。</summary>
        public PendingRenameOperation CurrentPendingRename
        {
            get
            {
                PendingRenameOperation pending = _pendingRenames.Get(IO.RuntimeHost.ActiveDocument);
                return pending == null ? null : pending.Copy();
            }
        }

        /// <summary>重试是否在等待用户对当前字典重新确认。</summary>
        public bool PendingRenameNeedsReconfirm
        {
            get { return _pendingRenameAwaitingReconfirm; }
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
                string dictPath = _workflow.ResolveDictPath();
                object document = IO.RuntimeHost.ActiveDocument;
                string identity = DictLoader.GetIdentity(_currentDict, dictPath);
                if (identity == null || !DictPaletteWorkflow.IsCurrentContext(document, dictPath))
                {
                    _lblStatus.Text = Strings.Dict_Changed;
                    return;
                }
                using (PasteRecognizeDialog dlg = new PasteRecognizeDialog(_currentDict, dictPath, document, identity))
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
        /// 改号同步失败后的显式重试入口。
        /// R-004：字典在失败后变化时，这一次点击是用户对当前字典的显式重新确认，
        /// 因此先换绑快照再写图；没有变化时按原快照重试。
        /// </summary>
        private void BtnRenameRetry_Click(object sender, EventArgs e)
        {
            try
            {
                if (_pendingRenameAwaitingReconfirm) ConfirmPendingRenameRetry();
                else RetryRenameInDrawing();
            }
            catch (System.Exception ex)
            {
                PatentMarkerApp.RawLog("BtnRenameRetry error: " + ex.Message);
                _lblStatus.Text = string.Format(Strings.Status_NumberSyncFailed, ex.Message);
            }
        }

        /// <summary>
        /// 失败结果必须在不依赖已销毁对话框的情况下仍可查看：
        /// 把旧号／新号／失败对象写到状态行与命令行，并保留日志。
        /// </summary>
        private void ReportRenamePending(string oldNumber, string newNumber, string detail)
        {
            _lblStatus.Text = string.Format(
                Strings.Status_DictChangedDrawingPending, oldNumber, newNumber, detail);
            try
            {
                var doc = IO.RuntimeHost.ActiveDocument;
                if (doc != null) doc.Editor.WriteMessage("\n" + _lblStatus.Text);
            }
            catch { }
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

                object document = IO.RuntimeHost.ActiveDocument;
                string identity = DictLoader.GetIdentity(_currentDict, dictPath);
                if (identity == null || !DictPaletteWorkflow.IsCurrentContext(document, dictPath))
                {
                    _lblStatus.Text = Strings.Dict_Changed;
                    return;
                }
                using (EditEntryDialog dlg = new EditEntryDialog(_currentDict, null, dictPath, document, identity))
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
                // Use the same resolver as loading/editing so configured fallback
                // dictionaries and the current drawing never diverge.
                string dictPath = _workflow.ResolveDictPath();
                if (dictPath == null || !System.IO.File.Exists(dictPath))
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

        private void UpdateVisibilityButtonText()
        {
            if (_btnVisibility == null) return;

            string dictPath = _workflow.ResolveDictPath();
            bool hasFile = dictPath != null && System.IO.File.Exists(dictPath);
            _btnVisibility.Enabled = hasFile;
            _btnVisibility.Text = hasFile && _workflow.IsDictVisible(dictPath)
                ? Strings.Palette_HideJson
                : Strings.Palette_ShowJson;
        }

        private void BtnVisibility_Click(object sender, EventArgs e)
        {
            try
            {
                string dictPath = _workflow.ResolveDictPath();
                if (dictPath == null || !System.IO.File.Exists(dictPath))
                {
                    _lblStatus.Text = Strings.Status_NoDictFile;
                    UpdateVisibilityButtonText();
                    return;
                }

                bool makeVisible = !_workflow.IsDictVisible(dictPath);
                string error;
                if (!_workflow.TrySetDictVisible(dictPath, makeVisible, out error))
                {
                    _lblStatus.Text = string.Format(Strings.Status_JsonVisibilityFailed, DictPaletteWorkflow.DescribeWriteError(error));
                    return;
                }

                _lblStatus.Text = makeVisible ? Strings.Status_JsonShown : Strings.Status_JsonHidden;
                UpdateVisibilityButtonText();
            }
            catch (System.Exception ex)
            {
                _lblStatus.Text = string.Format(Strings.Status_JsonVisibilityFailed, ex.Message);
                PatentMarkerApp.RawLog("BtnVisibility error: " + ex.Message);
            }
        }

    }

}
