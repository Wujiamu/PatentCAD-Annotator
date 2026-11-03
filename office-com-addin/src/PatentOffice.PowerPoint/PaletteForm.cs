using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PatentMarker.IO;
using PatentOffice.Shared;

namespace PatentOffice.PowerPoint
{
    internal sealed class PaletteForm : Form
    {
        private readonly PowerPointAddIn _addIn;
        private readonly System.Windows.Forms.Timer _refreshTimer;
        private readonly Label _bindingLabel;
        private readonly Label _statusLabel;
        private readonly TextBox _searchBox;
        private readonly TextBox _pathInput;
        private readonly ListView _entries;
        private readonly Button _markButton;
        private readonly Button _checkButton;
        private readonly Button _bindPathButton;
        private dynamic _activePresentation;
        private string _activePresentationPath;
        private string _boundPath;
        private string _lastLoadedHash;
        private string _lastAttemptedHash;
        private string _lastAttemptError;
        private bool _lastAttemptSucceeded;
        private PatentDictionary _dictionary;
        private bool _dictionaryCurrent;
        private bool _closingForHost;
        private bool _hasReportedReadFailure;
        private bool _hasReportedBindingFailure;
        private HashSet<string> _markedNumbers = new HashSet<string>(NumberIdentity.Comparer);

        public PaletteForm(PowerPointAddIn addIn)
        {
            _addIn = addIn;
            Text = "专利标注字典工具 · PowerPoint";
            Width = 680;
            Height = 520;
            MinimumSize = new Size(540, 360);
            StartPosition = FormStartPosition.CenterScreen;

            _bindingLabel = new Label { Dock = DockStyle.Top, Height = 40, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            _statusLabel = new Label { Dock = DockStyle.Bottom, Height = 42, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            _searchBox = new TextBox { Dock = DockStyle.Top };
            _searchBox.TextChanged += delegate { RenderEntries(); };

            _entries = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false
            };
            _entries.Columns.Add("编号", 110);
            _entries.Columns.Add("名称", 360);
            _entries.Columns.Add("次数", 70);

            _pathInput = new TextBox { Dock = DockStyle.Fill, AccessibleName = "字典文件路径" };
            _pathInput.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                BindPathButton_Click(sender, EventArgs.Empty);
            };
            _bindPathButton = new Button { Text = "绑定此路径", Dock = DockStyle.Fill };
            _bindPathButton.Click += BindPathButton_Click;
            TableLayoutPanel pathRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(6, 4, 6, 4)
            };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            pathRow.Controls.Add(new Label { Text = "字典路径", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            pathRow.Controls.Add(_pathInput, 1, 0);
            pathRow.Controls.Add(_bindPathButton, 2, 0);

            Button bindButton = new Button { Text = "选择 / 绑定字典", AutoSize = true };
            bindButton.Click += BindButton_Click;
            Button reloadButton = new Button { Text = "重载", AutoSize = true };
            reloadButton.Click += delegate { RefreshBoundDictionary(true); };
            _markButton = new Button { Text = "标注所选直线", AutoSize = true, Enabled = false };
            _markButton.Click += MarkButton_Click;
            _checkButton = new Button { Text = "检查整份演示文稿", AutoSize = true, Enabled = false };
            _checkButton.Click += CheckButton_Click;

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 38,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                AutoScroll = true
            };
            actions.Controls.Add(bindButton);
            actions.Controls.Add(reloadButton);
            actions.Controls.Add(_markButton);
            actions.Controls.Add(_checkButton);

            Controls.Add(_entries);
            Controls.Add(_statusLabel);
            Controls.Add(_searchBox);
            Controls.Add(pathRow);
            Controls.Add(actions);
            Controls.Add(_bindingLabel);

            _refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _refreshTimer.Tick += delegate { RefreshHostState(); };
            _refreshTimer.Start();
            FormClosing += PaletteForm_FormClosing;
            FormClosed += delegate { _refreshTimer.Stop(); _refreshTimer.Dispose(); };

            RefreshHostState();
        }

        internal void CloseFromHost()
        {
            _closingForHost = true;
            Close();
        }

        private void PaletteForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_closingForHost || e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true;
            WindowState = FormWindowState.Minimized;
            OfficeDiagnostics.Write("palette", "INFO", "minimized_by_user");
        }

        private void RefreshHostState()
        {
            try
            {
                dynamic presentation = PowerPointAnnotations.GetActivePresentation(_addIn.ApplicationObject);
                if (presentation == null)
                {
                    if (_activePresentation != null)
                    {
                        _activePresentation = null;
                        _activePresentationPath = null;
                        ClearDictionaryState();
                        _pathInput.Text = "";
                        RenderEntries();
                    }
                    _bindingLabel.Text = "当前没有打开的演示文稿。";
                    _statusLabel.Text = "打开并保存一个 .pptx，然后选择字典。";
                    _markButton.Enabled = false;
                    _checkButton.Enabled = false;
                    _bindPathButton.Enabled = false;
                    return;
                }

                string presentationPath = GetPresentationPath(presentation);
                bool changedPresentation = !object.ReferenceEquals(presentation, _activePresentation) ||
                    !string.Equals(presentationPath, _activePresentationPath, StringComparison.OrdinalIgnoreCase);
                if (changedPresentation)
                {
                    ClearDictionaryState();
                    RenderEntries();
                }

                _activePresentation = presentation;
                _activePresentationPath = presentationPath;
                _bindPathButton.Enabled = true;

                // The binding lives in the presentation and can be changed by Undo/Redo or an
                // external edit while the presentation object and path stay the same, so it is
                // re-resolved on every poll instead of only when the presentation key changes.
                BindingResult binding = PresentationBinding.Resolve(presentation);
                ApplyResolvedBinding(binding, presentationPath);
                if (!string.IsNullOrEmpty(binding.Error))
                {
                    _statusLabel.Text = "可使用“选择 / 绑定字典”重新关联。";
                    _markButton.Enabled = false;
                    _checkButton.Enabled = false;
                    ReportBindingFailure(presentationPath + ";" + binding.Error);
                    return;
                }
                if (_boundPath == null)
                {
                    _statusLabel.Text = "先选择 Word 导出的 .dict.json。";
                    _markButton.Enabled = false;
                    _checkButton.Enabled = false;
                    return;
                }

                _bindingLabel.Text = "字典：" + _boundPath;
                RefreshBoundDictionary(false);
            }
            catch (Exception ex)
            {
                _dictionaryCurrent = false;
                _statusLabel.Text = "宿主状态读取失败：" + ex.Message;
                _markButton.Enabled = false;
                _checkButton.Enabled = false;
                OfficeDiagnostics.Write("host.refresh", "FAIL", ex.GetType().Name + ":" + ex.Message);
            }
        }

        /// <summary>
        /// Adopts the freshly resolved binding. A different binding invalidates everything that
        /// belonged to the previous dictionary: rows, selection, marked statistics and the read
        /// failure recorded for the other file. Without this the panel would show the old rows
        /// next to the new path.
        /// </summary>
        private void ApplyResolvedBinding(BindingResult binding, string presentationPath)
        {
            string resolved = string.IsNullOrEmpty(binding.Path) ? null : binding.Path;
            string previous = _boundPath;
            bool samePath = string.Equals(resolved, previous, StringComparison.OrdinalIgnoreCase);
            if (!samePath)
            {
                bool hadBinding = previous != null || _dictionary != null;
                ClearDictionaryState();
                RenderEntries();
                _boundPath = resolved;
                if (hadBinding || resolved != null)
                    OfficeDiagnostics.Write("binding.changed", "INFO",
                        "ppt=" + presentationPath + ";from=" + (previous ?? "") + ";to=" + (resolved ?? ""));
            }
            else
            {
                _boundPath = resolved;
            }

            _pathInput.Text = _boundPath ?? "";
            if (!string.IsNullOrEmpty(binding.Error))
            {
                // A binding that cannot be resolved to a readable file must never leave the panel
                // acting on the previously loaded snapshot; the rows stay for viewing only.
                _dictionaryCurrent = false;
                _bindingLabel.Text = "字典关联：" + binding.Error;
                return;
            }
            if (_boundPath == null)
            {
                _dictionaryCurrent = false;
                _bindingLabel.Text = "字典关联：未设置";
                return;
            }
            _bindingLabel.Text = "字典：" + _boundPath;
            // Only a transition is worth a log line; the panel re-resolves the binding on every
            // 2-second poll, so logging each success would flood the diagnostic log.
            bool recovered = _hasReportedBindingFailure;
            _hasReportedBindingFailure = false;
            if (!samePath || recovered)
                OfficeDiagnostics.Write("binding.resolve", "PASS", "ppt=" + presentationPath + ";dict=" + _boundPath);
        }

        /// <summary>Drops the dictionary view and every statistic derived from it.</summary>
        private void ClearDictionaryState()
        {
            _boundPath = null;
            _lastLoadedHash = null;
            _lastAttemptedHash = null;
            _lastAttemptError = null;
            _lastAttemptSucceeded = false;
            _dictionary = null;
            _dictionaryCurrent = false;
            _markedNumbers.Clear();
            _hasReportedReadFailure = false;
            _hasReportedBindingFailure = false;
        }

        /// <summary>Logs a binding failure once so a 2-second poll cannot flood the log.</summary>
        private void ReportBindingFailure(string details)
        {
            if (_hasReportedBindingFailure) return;
            _hasReportedBindingFailure = true;
            OfficeDiagnostics.Write("binding.resolve", "FAIL", details);
        }

        private void BindButton_Click(object sender, EventArgs e)
        {
            try
            {
                dynamic presentation = PowerPointAnnotations.GetActivePresentation(_addIn.ApplicationObject);
                if (presentation == null) throw new InvalidOperationException("请先打开演示文稿。");
                if (!PowerPointAnnotations.IsSavedPresentation(presentation))
                    throw new InvalidOperationException("请先保存为 .pptx，再绑定字典。");

                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "选择 Word 导出的专利标注字典";
                    dialog.Filter = "Word 导出的专利标注字典 (*.dict.json)|*.dict.json";
                    dialog.CheckFileExists = true;
                    dialog.Multiselect = false;
                    try { dialog.InitialDirectory = Path.GetDirectoryName(Convert.ToString(presentation.FullName)); }
                    catch { }
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    BindDictionaryPath(presentation, dialog.FileName);
                }
            }
            catch (Exception ex)
            {
                ShowFailure("绑定失败", ex);
                OfficeDiagnostics.Write("binding.save", "FAIL", ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void BindPathButton_Click(object sender, EventArgs e)
        {
            try
            {
                dynamic presentation = PowerPointAnnotations.GetActivePresentation(_addIn.ApplicationObject);
                if (presentation == null) throw new InvalidOperationException("请先打开演示文稿。");
                BindDictionaryPath(presentation, _pathInput.Text);
            }
            catch (Exception ex)
            {
                ShowFailure("绑定失败", ex);
                OfficeDiagnostics.Write("binding.save", "FAIL", ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void BindDictionaryPath(dynamic presentation, string selectedPath)
        {
            if (!PowerPointAnnotations.IsSavedPresentation(presentation))
                throw new InvalidOperationException("请先保存为 .pptx，再绑定字典。");
            if (string.IsNullOrWhiteSpace(selectedPath))
                throw new InvalidDataException("请输入 Word 导出的 .dict.json 文件路径。");
            string dictionaryPath = Path.GetFullPath(selectedPath.Trim());
            if (!dictionaryPath.EndsWith(".dict.json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("请选择 Word 导出的 .dict.json 文件。");

            DictionarySnapshot snapshot = DictionaryReader.Read(dictionaryPath);
            PresentationBinding.Save(presentation, dictionaryPath);
            _activePresentation = presentation;
            _activePresentationPath = GetPresentationPath(presentation);
            ClearDictionaryState();
            _boundPath = snapshot.Path;
            _pathInput.Text = _boundPath;
            _bindingLabel.Text = "字典：" + _boundPath;
            _statusLabel.Text = "绑定成功。请保存演示文稿以保存此关联。";
            OfficeDiagnostics.Write("binding.save", "PASS", "ppt=" + _activePresentationPath + ";dict=" + _boundPath + ";sha256=" + snapshot.Sha256);
            RefreshBoundDictionary(true);
        }

        private void RefreshBoundDictionary(bool force)
        {
            if (string.IsNullOrEmpty(_boundPath)) return;
            string path = _boundPath;
            try
            {
                if (!File.Exists(path))
                {
                    _dictionaryCurrent = false;
                    _lastAttemptedHash = null;
                    _lastAttemptSucceeded = false;
                    _lastAttemptError = null;
                    _markButton.Enabled = false;
                    _checkButton.Enabled = false;
                    _hasReportedReadFailure = false;
                    _statusLabel.Text = "绑定的字典文件不存在。上次内容保留供查看；请选择有效字典重新绑定。";
                    return;
                }

                // One read of the bytes yields both the identity and the content, so a failure can
                // never be attributed to a hash taken from a different (possibly repaired) file.
                DictionarySnapshot snapshot = DictionaryReader.Read(path);
                if (!force && _lastAttemptSucceeded && _dictionaryCurrent &&
                    string.Equals(snapshot.Sha256, _lastAttemptedHash, StringComparison.OrdinalIgnoreCase))
                    return;

                _dictionary = snapshot.Dictionary;
                _lastLoadedHash = snapshot.Sha256;
                _lastAttemptedHash = snapshot.Sha256;
                _lastAttemptError = null;
                _lastAttemptSucceeded = true;
                _dictionaryCurrent = true;
                _hasReportedReadFailure = false;
                _markButton.Enabled = _dictionary.Entries.Count > 0;
                _checkButton.Enabled = true;
                _markedNumbers = PowerPointAnnotations.GetMarkedNumbers(_activePresentation);
                RenderEntries();
                _statusLabel.Text = "已读取 " + _dictionary.Entries.Count + " 个编号；标注只写入演示文稿。";
                OfficeDiagnostics.Write("dict.read", "PASS", "path=" + _boundPath + ";entries=" + _dictionary.Entries.Count + ";sha256=" + _lastLoadedHash);
            }
            catch (Exception ex)
            {
                _dictionaryCurrent = false;
                // Never carry the failed bytes' identity into the cache: a transient I/O error must
                // be retried on the next poll, and a content error must not be pinned to a hash that
                // may already describe a repaired file.
                _lastAttemptedHash = null;
                _lastAttemptSucceeded = false;
                _lastAttemptError = ex.GetType().Name + ":" + ex.Message;
                _markButton.Enabled = false;
                _checkButton.Enabled = false;
                _statusLabel.Text = "字典读取失败，已暂停新标注；下次刷新会自动重试。上次有效列表仍保留。" + _lastAttemptError;
                if (!_hasReportedReadFailure)
                {
                    _hasReportedReadFailure = true;
                    OfficeDiagnostics.Write("dict.read", "FAIL", "path=" + path + ";" + ex.GetType().Name + ":" + ex.Message);
                }
            }
        }

        private void MarkButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (!RefreshActionContext(true)) return;
                if (!_dictionaryCurrent || _dictionary == null)
                    throw new InvalidOperationException("当前字典不可用，已暂停标注。");
                if (_entries.SelectedItems.Count != 1)
                    throw new InvalidOperationException("请先选择一个编号。");
                PatentEntry entry = _entries.SelectedItems[0].Tag as PatentEntry;
                if (entry == null) throw new InvalidOperationException("所选编号无法读取。");

                PowerPointAnnotations.MarkSelectedLine(_addIn.ApplicationObject, entry);
                _markedNumbers = PowerPointAnnotations.GetMarkedNumbers(_activePresentation);
                RenderEntries();
                _statusLabel.Text = "已添加“" + entry.Number + "”标注。请保存演示文稿以保留标注。";
            }
            catch (Exception ex)
            {
                ShowFailure("无法添加标注", ex);
                OfficeDiagnostics.Write("annotation.create", "FAIL", ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void CheckButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (!RefreshActionContext(false)) return;
                if (!_dictionaryCurrent || _dictionary == null)
                    throw new InvalidOperationException("当前字典不可用，无法检查。");
                _markedNumbers = PowerPointAnnotations.GetMarkedNumbers(_activePresentation);
                List<string> missing = new List<string>();
                for (int i = 0; i < _dictionary.Entries.Count; i++)
                {
                    PatentEntry entry = _dictionary.Entries[i];
                    if (!_markedNumbers.Contains(NumberIdentity.Normalize(entry.Number)))
                        missing.Add(entry.Number);
                }
                RenderEntries();
                if (missing.Count == 0)
                    _statusLabel.Text = "检查完成：字典中的编号均已在演示文稿中标注。";
                else
                    _statusLabel.Text = "整份演示文稿漏标 " + missing.Count + " 项：" + string.Join("、", missing.ToArray());
                OfficeDiagnostics.Write("marking.check", "PASS", "missing=" + missing.Count + ";marked=" + _markedNumbers.Count);
            }
            catch (Exception ex)
            {
                ShowFailure("检查失败", ex);
                OfficeDiagnostics.Write("marking.check", "FAIL", ex.GetType().Name + ":" + ex.Message);
            }
        }

        /// <summary>
        /// Re-checks the host context before an action. Both the presentation identity and the
        /// binding are re-validated; when either changed, the stale row selection is dropped so a
        /// number from the previous dictionary cannot be applied to the new one. A whole-presentation
        /// check needs no selection, so it may continue once the new dictionary has been read.
        /// </summary>
        private bool RefreshActionContext(bool requiresSelection)
        {
            object previous = _activePresentation;
            string previousPath = _activePresentationPath;
            string previousBinding = _boundPath;
            bool hadSelection = _entries.SelectedItems.Count == 1;
            RefreshHostState();

            if (!object.ReferenceEquals(previous, _activePresentation) ||
                !string.Equals(previousPath, _activePresentationPath, StringComparison.OrdinalIgnoreCase))
            {
                _statusLabel.Text = "当前演示文稿已切换。请在新文稿重新选择编号或执行检查。";
                OfficeDiagnostics.Write("palette.action", "INFO", "presentation_changed;from=" +
                    (previousPath ?? "") + ";to=" + (_activePresentationPath ?? ""));
                return false;
            }

            if (!string.Equals(previousBinding, _boundPath, StringComparison.OrdinalIgnoreCase))
            {
                OfficeDiagnostics.Write("palette.action", "INFO", "binding_changed;from=" +
                    (previousBinding ?? "") + ";to=" + (_boundPath ?? ""));
                if (requiresSelection)
                {
                    _statusLabel.Text = "字典关联已变化，已清除旧选择。请重新选择编号后再执行操作。";
                    return false;
                }
                return true;
            }

            if (requiresSelection && hadSelection && _entries.SelectedItems.Count != 1)
            {
                _statusLabel.Text = "列表已刷新，已清除旧选择。请重新选择编号后再执行操作。";
                OfficeDiagnostics.Write("palette.action", "INFO", "selection_cleared");
                return false;
            }
            return true;
        }

        private void RenderEntries()
        {
            _entries.BeginUpdate();
            try
            {
                _entries.Items.Clear();
                if (_dictionary == null || _dictionary.Entries == null) return;
                string filter = _searchBox.Text.Trim();
                for (int i = 0; i < _dictionary.Entries.Count; i++)
                {
                    PatentEntry entry = _dictionary.Entries[i];
                    string number = entry.Number ?? "";
                    string name = entry.Name ?? "";
                    if (filter.Length > 0 && number.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    ListViewItem item = new ListViewItem(number);
                    item.SubItems.Add(name);
                    item.SubItems.Add(entry.Occurrences.ToString());
                    item.Tag = entry;
                    if (_markedNumbers.Contains(NumberIdentity.Normalize(number)))
                        item.ForeColor = Color.DarkGreen;
                    else
                        item.ForeColor = Color.DarkOrange;
                    _entries.Items.Add(item);
                }
            }
            finally { _entries.EndUpdate(); }
        }

        private static string GetPresentationPath(dynamic presentation)
        {
            try { return Convert.ToString(presentation.FullName); }
            catch { return ""; }
        }

        private void ShowFailure(string title, Exception exception)
        {
            _statusLabel.Text = title + "：" + exception.Message;
            MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
