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
                        _boundPath = null;
                        _lastLoadedHash = null;
                        _lastAttemptedHash = null;
                        _lastAttemptError = null;
                        _lastAttemptSucceeded = false;
                        _dictionary = null;
                        _dictionaryCurrent = false;
                        _markedNumbers.Clear();
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
                    _activePresentation = presentation;
                    _activePresentationPath = presentationPath;
                    _boundPath = null;
                    _lastLoadedHash = null;
                    _lastAttemptedHash = null;
                    _lastAttemptError = null;
                    _lastAttemptSucceeded = false;
                    _dictionary = null;
                    _dictionaryCurrent = false;
                    _markedNumbers.Clear();
                    RenderEntries();
                    BindingResult binding = PresentationBinding.Resolve(presentation);
                    if (!string.IsNullOrEmpty(binding.Path)) _boundPath = binding.Path;
                    _pathInput.Text = _boundPath ?? "";
                    _bindPathButton.Enabled = true;
                    if (!string.IsNullOrEmpty(binding.Error))
                    {
                        _bindingLabel.Text = "字典关联：" + binding.Error;
                        _statusLabel.Text = "可使用“选择 / 绑定字典”重新关联。";
                        _markButton.Enabled = false;
                        _checkButton.Enabled = false;
                        OfficeDiagnostics.Write("binding.resolve", "FAIL", "ppt=" + presentationPath + ";" + binding.Error);
                        return;
                    }
                    if (_boundPath == null)
                    {
                        _bindingLabel.Text = "字典关联：未设置";
                        _statusLabel.Text = "先选择 Word 导出的 .dict.json。";
                        _markButton.Enabled = false;
                        _checkButton.Enabled = false;
                        return;
                    }
                    OfficeDiagnostics.Write("binding.resolve", "PASS", "ppt=" + presentationPath + ";dict=" + _boundPath);
                }

                if (_boundPath != null)
                {
                    _bindingLabel.Text = "字典：" + _boundPath;
                    RefreshBoundDictionary(false);
                }
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
            _boundPath = snapshot.Path;
            _lastLoadedHash = null;
            _lastAttemptedHash = null;
            _lastAttemptError = null;
            _lastAttemptSucceeded = false;
            _dictionary = null;
            _dictionaryCurrent = false;
            _markedNumbers.Clear();
            _pathInput.Text = _boundPath;
            _statusLabel.Text = "绑定成功。请保存演示文稿以保存此关联。";
            OfficeDiagnostics.Write("binding.save", "PASS", "ppt=" + _activePresentationPath + ";dict=" + _boundPath + ";sha256=" + snapshot.Sha256);
            RefreshBoundDictionary(true);
        }

        private void RefreshBoundDictionary(bool force)
        {
            if (string.IsNullOrEmpty(_boundPath)) return;
            string observedHash = null;
            try
            {
                if (!File.Exists(_boundPath))
                {
                    _dictionaryCurrent = false;
                    _markButton.Enabled = false;
                    _checkButton.Enabled = false;
                    _statusLabel.Text = "绑定的字典文件不存在。上次内容保留供查看；请选择有效字典重新绑定。";
                    return;
                }

                observedHash = DictionaryReader.ComputeHash(_boundPath);
                if (!force && string.Equals(observedHash, _lastAttemptedHash, StringComparison.OrdinalIgnoreCase))
                {
                    if (_lastAttemptSucceeded && _dictionaryCurrent) return;
                    if (!_lastAttemptSucceeded)
                    {
                        _dictionaryCurrent = false;
                        _markButton.Enabled = false;
                        _checkButton.Enabled = false;
                        _statusLabel.Text = "字典读取失败，已暂停新标注；上次有效列表仍保留。" + _lastAttemptError;
                        return;
                    }
                }

                DictionarySnapshot snapshot = DictionaryReader.Read(_boundPath);
                _dictionary = snapshot.Dictionary;
                _lastLoadedHash = snapshot.Sha256;
                _lastAttemptedHash = snapshot.Sha256;
                _lastAttemptError = null;
                _lastAttemptSucceeded = true;
                _dictionaryCurrent = true;
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
                _lastAttemptedHash = observedHash;
                _lastAttemptError = ex.GetType().Name + ":" + ex.Message;
                _lastAttemptSucceeded = false;
                _markButton.Enabled = false;
                _checkButton.Enabled = false;
                _statusLabel.Text = "字典读取失败，已暂停新标注；上次有效列表仍保留。" + _lastAttemptError;
                OfficeDiagnostics.Write("dict.read", "FAIL", "path=" + _boundPath + ";" + ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void MarkButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (!RefreshActionContext()) return;
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
                if (!RefreshActionContext()) return;
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

        private bool RefreshActionContext()
        {
            object previous = _activePresentation;
            string previousPath = _activePresentationPath;
            RefreshHostState();
            if (object.ReferenceEquals(previous, _activePresentation) &&
                string.Equals(previousPath, _activePresentationPath, StringComparison.OrdinalIgnoreCase))
                return true;
            _statusLabel.Text = "当前演示文稿已切换。请在新文稿重新选择编号或执行检查。";
            OfficeDiagnostics.Write("palette.action", "INFO", "presentation_changed;from=" +
                (previousPath ?? "") + ";to=" + (_activePresentationPath ?? ""));
            return false;
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
