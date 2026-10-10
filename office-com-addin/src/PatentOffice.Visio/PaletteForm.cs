using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using PatentMarker.IO;
using PatentOffice.Shared;
using VisioDiagnostics = PatentOffice.Shared.OfficeDiagnostics;

namespace PatentOffice.Visio
{
    internal sealed class PaletteForm : Form
    {
        private readonly VisioAddIn _addIn;
        private readonly Label _documentLabel;
        private readonly Label _bindingLabel;
        private readonly Label _statusLabel;
        private readonly ListView _entries;
        private readonly TextBox _pathInput;
        private readonly Button _bindPathButton;
        private readonly Button _bindButton;
        private readonly Button _markButton;
        private readonly Button _checkButton;
        private readonly Timer _refreshTimer;
        private object _activeDocument;
        private string _activeDocumentKey;
        private string _boundPath;
        private string _lastAttemptedHash;
        private string _lastAttemptError;
        private bool _lastAttemptSucceeded;
        private bool _dictionaryCurrent;
        private DictionarySnapshot _snapshot;
        private HashSet<string> _markedNumbers = new HashSet<string>(NumberIdentity.Comparer);
        private int _malformedCount;
        private bool _closingForHost;

        public PaletteForm(VisioAddIn addIn)
        {
            _addIn = addIn;
            Text = "专利标注字典工具 · Visio";
            StartPosition = FormStartPosition.Manual;
            Location = new Point(80, 100);
            Size = new Size(600, 560);
            MinimumSize = new Size(500, 400);
            ShowInTaskbar = true;

            _documentLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Padding = new Padding(8, 7, 8, 0),
                Text = "当前文档：读取中",
                AutoEllipsis = true
            };
            _bindingLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 42,
                Padding = new Padding(8, 4, 8, 4),
                Text = "字典关联：读取中",
                AutoEllipsis = true
            };
            _statusLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 62,
                Padding = new Padding(8, 7, 8, 7),
                Text = "正在检查 Visio 文档。",
                AutoEllipsis = true
            };
            _entries = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false
            };
            _entries.Columns.Add("编号", 105);
            _entries.Columns.Add("名称", 300);
            _entries.Columns.Add("出现次数", 80);

            _pathInput = new TextBox { Dock = DockStyle.Fill, AccessibleName = "字典文件路径" };
            _bindPathButton = new Button { Text = "绑定此路径", Dock = DockStyle.Fill };
            _bindPathButton.Click += BindPathButton_Click;
            _pathInput.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                BindPathButton_Click(sender, EventArgs.Empty);
            };

            TableLayoutPanel pathRow = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(8, 5, 8, 5)
            };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            pathRow.Controls.Add(new Label { Text = "字典路径", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            pathRow.Controls.Add(_pathInput, 1, 0);
            pathRow.Controls.Add(_bindPathButton, 2, 0);

            _bindButton = new Button { Text = "选择 / 绑定字典", AutoSize = true };
            _markButton = new Button { Text = "标注所选线条", AutoSize = true, Enabled = false };
            _checkButton = new Button { Text = "检查整份文档", AutoSize = true, Enabled = false };
            _bindButton.Click += BindButton_Click;
            _markButton.Click += MarkButton_Click;
            _checkButton.Click += CheckButton_Click;

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                Padding = new Padding(8, 4, 8, 4),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            actions.Controls.Add(_bindButton);
            actions.Controls.Add(_markButton);
            actions.Controls.Add(_checkButton);

            Controls.Add(_entries);
            Controls.Add(_statusLabel);
            Controls.Add(actions);
            Controls.Add(pathRow);
            Controls.Add(_bindingLabel);
            Controls.Add(_documentLabel);

            _refreshTimer = new Timer { Interval = 2000 };
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
            VisioDiagnostics.Write("palette", "INFO", "minimized_by_user");
        }

        private void RefreshHostState()
        {
            try
            {
                dynamic document = VisioAnnotations.GetActiveDocument(_addIn.ApplicationObject);
                if (document == null)
                {
                    ClearDocumentState();
                    _documentLabel.Text = "当前文档：没有打开的 Visio 文档";
                    _bindingLabel.Text = "字典关联：无";
                    _statusLabel.Text = "打开并保存一份 Visio 文档，再选择字典。";
                    SetActionsEnabled(false);
                    return;
                }

                string key = GetDocumentKey(document);
                bool changed = !ReferenceEquals((object)document, _activeDocument) ||
                    !string.Equals(key, _activeDocumentKey, StringComparison.OrdinalIgnoreCase);
                if (changed)
                {
                    _activeDocument = document;
                    _activeDocumentKey = key;
                    _boundPath = null;
                    _lastAttemptedHash = null;
                    _lastAttemptError = null;
                    _lastAttemptSucceeded = false;
                    _dictionaryCurrent = false;
                    _snapshot = null;
                    _markedNumbers.Clear();
                    _malformedCount = 0;
                    RenderEntries();

                    BindingResult binding = VisioDocumentBinding.Resolve(document);
                    if (!string.IsNullOrEmpty(binding.Path)) _boundPath = binding.Path;
                    _pathInput.Text = _boundPath ?? "";
                    if (!string.IsNullOrEmpty(binding.Error))
                    {
                        _documentLabel.Text = "当前文档：" + SafeName(document);
                        _bindingLabel.Text = "字典关联：" + binding.Error;
                        _statusLabel.Text = "保存文档后可以选择字典；关联损坏时请重新绑定。";
                        SetActionsEnabled(false);
                        VisioDiagnostics.Write("binding.resolve", "FAIL", "doc=" + key + ";" + binding.Error);
                        return;
                    }
                    VisioDiagnostics.Write("binding.resolve", string.IsNullOrEmpty(_boundPath) ? "UNBOUND" : "PASS",
                        "doc=" + key + ";dict=" + (_boundPath ?? ""));
                }

                _activeDocument = document;
                _documentLabel.Text = "当前文档：" + SafeName(document);
                if (_boundPath == null)
                {
                    _bindingLabel.Text = "字典关联：未设置";
                    _statusLabel.Text = "先选择 Word 导出的 .dict.json。";
                    SetActionsEnabled(false);
                    return;
                }

                _bindingLabel.Text = "字典：" + _boundPath;
                RefreshBoundDictionary(false);
                SetActionsEnabled(_dictionaryCurrent && _snapshot != null);
            }
            catch (Exception ex)
            {
                _dictionaryCurrent = false;
                _statusLabel.Text = "Visio 状态读取失败：" + ex.Message;
                SetActionsEnabled(false);
                VisioDiagnostics.Write("host.refresh", "FAIL", ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void RefreshBoundDictionary(bool force)
        {
            if (string.IsNullOrEmpty(_boundPath)) return;
            try
            {
                if (!File.Exists(_boundPath))
                {
                    _dictionaryCurrent = false;
                    _markButton.Enabled = false;
                    _statusLabel.Text = "字典文件暂不可用。上次有效列表保留供查看；请选择有效字典或恢复文件。";
                    return;
                }

                string hash = DictionaryReader.ComputeHash(_boundPath);
                if (!force && string.Equals(hash, _lastAttemptedHash, StringComparison.OrdinalIgnoreCase))
                {
                    if (_lastAttemptSucceeded && _dictionaryCurrent) return;
                    if (!_lastAttemptSucceeded)
                    {
                        _dictionaryCurrent = false;
                        _markButton.Enabled = false;
                        _statusLabel.Text = "字典读取失败，已暂停新标注；上次有效列表仍保留。" + _lastAttemptError;
                        return;
                    }
                }

                DictionarySnapshot current = DictionaryReader.Read(_boundPath);
                _snapshot = current;
                _lastAttemptedHash = current.Sha256;
                _lastAttemptError = null;
                _lastAttemptSucceeded = true;
                _dictionaryCurrent = true;
                ScanCurrentDocument();
                RenderEntries();
                _statusLabel.Text = "已读取 " + current.Dictionary.Entries.Count + " 个编号；字典只读，标注保存在当前文档。";
                VisioDiagnostics.Write("dict.read", "PASS", "doc=" + _activeDocumentKey + ";path=" +
                    _boundPath + ";entries=" + current.Dictionary.Entries.Count + ";sha256=" + current.Sha256);
            }
            catch (Exception ex)
            {
                _dictionaryCurrent = false;
                try { _lastAttemptedHash = DictionaryReader.ComputeHash(_boundPath); }
                catch { _lastAttemptedHash = null; }
                _lastAttemptError = ex.GetType().Name + ":" + ex.Message;
                _lastAttemptSucceeded = false;
                _markButton.Enabled = false;
                _statusLabel.Text = "字典读取失败，已暂停新标注；上次有效列表仍保留。" + _lastAttemptError;
                VisioDiagnostics.Write("dict.read", "FAIL", "doc=" + _activeDocumentKey + ";path=" +
                    _boundPath + ";" + _lastAttemptError);
            }
        }

        private void BindButton_Click(object sender, EventArgs e)
        {
            try
            {
                dynamic document = VisioAnnotations.GetActiveDocument(_addIn.ApplicationObject);
                if (document == null) throw new InvalidOperationException("请先打开 Visio 文档。");
                string documentPath = VisioDocumentBinding.GetDocumentPath(document);
                if (string.IsNullOrEmpty(documentPath) || !File.Exists(documentPath))
                    throw new InvalidOperationException("请先保存 Visio 文档，再绑定字典。");

                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "选择 Word 导出的专利标注字典";
                    dialog.Filter = "Word 导出的专利标注字典 (*.dict.json)|*.dict.json";
                    dialog.CheckFileExists = true;
                    dialog.Multiselect = false;
                    try { dialog.InitialDirectory = Path.GetDirectoryName(documentPath); }
                    catch { }
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    BindDictionaryPath(document, dialog.FileName);
                }
            }
            catch (Exception ex)
            {
                ShowFailure("绑定失败", ex);
                VisioDiagnostics.Write("binding.save", "FAIL", ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void BindPathButton_Click(object sender, EventArgs e)
        {
            try
            {
                dynamic document = VisioAnnotations.GetActiveDocument(_addIn.ApplicationObject);
                if (document == null) throw new InvalidOperationException("请先打开 Visio 文档。");
                BindDictionaryPath(document, _pathInput.Text);
            }
            catch (Exception ex)
            {
                ShowFailure("绑定失败", ex);
                VisioDiagnostics.Write("binding.save", "FAIL", ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void BindDictionaryPath(dynamic document, string selectedPath)
        {
            string documentPath = VisioDocumentBinding.GetDocumentPath(document);
            if (string.IsNullOrEmpty(documentPath) || !File.Exists(documentPath))
                throw new InvalidOperationException("请先保存 Visio 文档，再绑定字典。");
            if (string.IsNullOrWhiteSpace(selectedPath))
                throw new InvalidDataException("请输入 Word 导出的 .dict.json 文件路径。");
            string dictionaryPath = Path.GetFullPath(selectedPath.Trim());
            if (!dictionaryPath.EndsWith(".dict.json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("请选择 Word 导出的 .dict.json 文件。");

            DictionarySnapshot snapshot = DictionaryReader.Read(dictionaryPath);
            VisioDocumentBinding.Save(document, dictionaryPath);
            _activeDocument = document;
            _activeDocumentKey = GetDocumentKey(document);
            _boundPath = snapshot.Path;
            _snapshot = snapshot;
            _lastAttemptedHash = snapshot.Sha256;
            _lastAttemptError = null;
            _lastAttemptSucceeded = true;
            _dictionaryCurrent = true;
            ScanCurrentDocument();
            RenderEntries();
            _pathInput.Text = _boundPath;
            _bindingLabel.Text = "字典：" + _boundPath;
            _statusLabel.Text = "绑定成功。请保存 Visio 文档以保存关联。";
            VisioDiagnostics.Write("binding.save", "PASS", "doc=" + _activeDocumentKey +
                ";dict=" + _boundPath + ";sha256=" + snapshot.Sha256);
        }

        private void MarkButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (!RefreshActionContext()) return;
                if (!_dictionaryCurrent || _snapshot == null)
                    throw new InvalidOperationException("当前字典不可用，已暂停标注。");
                if (_entries.SelectedItems.Count != 1)
                    throw new InvalidOperationException("请先选择一个编号。");
                PatentEntry entry = _entries.SelectedItems[0].Tag as PatentEntry;
                if (entry == null) throw new InvalidOperationException("所选编号无法读取。");

                VisioAnnotations.MarkSelectedLine(_addIn.ApplicationObject, entry);
                ScanCurrentDocument();
                RenderEntries();
                _statusLabel.Text = "已添加“" + entry.Number + "”标注。请保存 Visio 文档以保留标注。";
            }
            catch (Exception ex)
            {
                ShowFailure("无法添加标注", ex);
                VisioDiagnostics.Write("annotation.create", "FAIL", ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void CheckButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (!RefreshActionContext()) return;
                if (!_dictionaryCurrent || _snapshot == null)
                    throw new InvalidOperationException("当前字典不可用，无法检查。");
                ScanCurrentDocument();
                List<string> missing = new List<string>();
                HashSet<string> seen = new HashSet<string>(NumberIdentity.Comparer);
                for (int i = 0; i < _snapshot.Dictionary.Entries.Count; i++)
                {
                    PatentEntry entry = _snapshot.Dictionary.Entries[i];
                    string normalized = NumberIdentity.Normalize(entry.Number);
                    if (seen.Add(normalized) && !_markedNumbers.Contains(normalized))
                        missing.Add(entry.Number);
                }

                HashSet<string> dictionaryNumbers = new HashSet<string>(NumberIdentity.Comparer);
                for (int i = 0; i < _snapshot.Dictionary.Entries.Count; i++)
                    dictionaryNumbers.Add(NumberIdentity.Normalize(_snapshot.Dictionary.Entries[i].Number));
                List<string> stale = new List<string>();
                foreach (string number in _markedNumbers)
                    if (!dictionaryNumbers.Contains(number)) stale.Add(number);

                RenderEntries();
                if (missing.Count == 0 && stale.Count == 0 && _malformedCount == 0)
                    _statusLabel.Text = "检查完成：字典中的编号均已标注。";
                else
                {
                    List<string> details = new List<string>();
                    if (missing.Count > 0) details.Add("漏标 " + missing.Count + " 项：" + string.Join("、", missing.ToArray()));
                    if (stale.Count > 0) details.Add("字典中已不存在：" + string.Join("、", stale.ToArray()));
                    if (_malformedCount > 0) details.Add("损坏的产品标注：" + _malformedCount);
                    _statusLabel.Text = "检查完成：" + string.Join("；", details.ToArray());
                }
                VisioDiagnostics.Write("marking.check", "PASS", "doc=" + _activeDocumentKey +
                    ";missing=" + missing.Count + ";stale=" + stale.Count + ";malformed=" + _malformedCount +
                    ";marked=" + _markedNumbers.Count + ";scope=foreground-pages");
            }
            catch (Exception ex)
            {
                ShowFailure("检查失败", ex);
                VisioDiagnostics.Write("marking.check", "FAIL", ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void ScanCurrentDocument()
        {
            if (_activeDocument == null) return;
            AnnotationScanResult scan = VisioAnnotations.ScanDocument((dynamic)_activeDocument);
            _markedNumbers = scan.MarkedNumbers;
            _malformedCount = scan.MalformedCount;
        }

        private bool RefreshActionContext()
        {
            string previousKey = _activeDocumentKey;
            RefreshHostState();
            if (string.Equals(previousKey, _activeDocumentKey, StringComparison.OrdinalIgnoreCase)) return true;
            _statusLabel.Text = "当前文档已切换。请在新文档重新选择编号或执行检查。";
            VisioDiagnostics.Write("palette.action", "INFO", "document_changed;from=" +
                (previousKey ?? "") + ";to=" + (_activeDocumentKey ?? ""));
            return false;
        }

        private void RenderEntries()
        {
            _entries.BeginUpdate();
            try
            {
                _entries.Items.Clear();
                if (_snapshot == null || _snapshot.Dictionary.Entries == null) return;
                for (int i = 0; i < _snapshot.Dictionary.Entries.Count; i++)
                {
                    PatentEntry entry = _snapshot.Dictionary.Entries[i];
                    ListViewItem item = new ListViewItem(entry.Number ?? "");
                    item.SubItems.Add(entry.Name ?? "");
                    item.SubItems.Add(entry.Occurrences.ToString());
                    item.Tag = entry;
                    item.ForeColor = _markedNumbers.Contains(NumberIdentity.Normalize(entry.Number))
                        ? Color.DarkGreen : Color.DarkOrange;
                    _entries.Items.Add(item);
                }
            }
            finally { _entries.EndUpdate(); }
        }

        private void ClearDocumentState()
        {
            _activeDocument = null;
            _activeDocumentKey = null;
            _boundPath = null;
            _lastAttemptedHash = null;
            _lastAttemptError = null;
            _lastAttemptSucceeded = false;
            _dictionaryCurrent = false;
            _snapshot = null;
            _markedNumbers.Clear();
            _malformedCount = 0;
            _pathInput.Text = "";
            RenderEntries();
        }

        private void SetActionsEnabled(bool enabled)
        {
            _markButton.Enabled = enabled && _snapshot != null && _snapshot.Dictionary.Entries.Count > 0;
            _checkButton.Enabled = enabled && _snapshot != null;
            _bindButton.Enabled = _activeDocument != null;
            _bindPathButton.Enabled = _activeDocument != null;
        }

        private static string GetDocumentKey(dynamic document)
        {
            string path = VisioDocumentBinding.GetDocumentPath(document);
            if (!string.IsNullOrEmpty(path))
                return path + "#" + RuntimeHelpers.GetHashCode((object)document).ToString("X8");
            string name = SafeName(document);
            return "unsaved:" + name + ":" + RuntimeHelpers.GetHashCode((object)document);
        }

        private static string SafeName(dynamic document)
        {
            try { return Convert.ToString(document.Name); }
            catch { return "(无法读取名称)"; }
        }

        private void ShowFailure(string title, Exception exception)
        {
            _statusLabel.Text = title + "：" + exception.Message;
            MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
