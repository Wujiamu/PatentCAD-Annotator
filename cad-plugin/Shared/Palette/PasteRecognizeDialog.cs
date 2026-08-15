using PatentMarker.IO;
using PatentMarker.I18n;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PatentMarker.Palette
{
    /// <summary>
    /// v4.0 功能 1：粘贴识别对话框。
    /// 多行输入附图标记说明文本 → MarkingTextParser 识别 → 预览列表（行内可编辑）
    /// → 确认时选择覆盖 / 合并写回 dict.json（metadata 记录 modified_by=CAD）。
    /// </summary>
    public class PasteRecognizeDialog : Form
    {
        private TextBox _txtInput = null;
        private Button _btnRecognize = null;
        private DataGridView _grid = null;
        private Label _lblInfo = null;
        private Button _btnConfirm = null;
        private Button _btnCancel = null;

        private readonly List<PreviewRow> _rows = new List<PreviewRow>();

        public PasteRecognizeDialog()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = Strings.Paste_Title;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimumSize = new Size(560, 480);
            this.Size = new Size(640, 560);
            this.Font = new Font("Microsoft Sans Serif", 9F);

            // 上部：输入区
            Label lblInput = new Label();
            lblInput.Text = Strings.Paste_InputHint;
            lblInput.Dock = DockStyle.Top;
            lblInput.Height = 22;
            lblInput.Padding = new Padding(2);

            _txtInput = new TextBox();
            _txtInput.Dock = DockStyle.Top;
            _txtInput.Height = 120;
            _txtInput.Multiline = true;
            _txtInput.ScrollBars = ScrollBars.Vertical;
            _txtInput.AcceptsReturn = true;

            // 识别按钮
            _btnRecognize = new Button();
            _btnRecognize.Text = Strings.Paste_BtnRecognize;
            _btnRecognize.AutoSize = true;
            _btnRecognize.Click += new EventHandler(BtnRecognize_Click);
            _btnRecognize.Dock = DockStyle.Top;
            _btnRecognize.Height = 30;

            // 信息标签
            _lblInfo = new Label();
            _lblInfo.Dock = DockStyle.Top;
            _lblInfo.Height = 22;
            _lblInfo.Padding = new Padding(2);
            _lblInfo.ForeColor = SystemColors.GrayText;

            // 中部：预览表格（行内可编辑）
            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = true;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            DataGridViewTextBoxColumn colNumber = new DataGridViewTextBoxColumn();
            colNumber.HeaderText = Strings.Paste_GridNumber;
            colNumber.DataPropertyName = "Number";
            colNumber.FillWeight = 30;

            DataGridViewTextBoxColumn colName = new DataGridViewTextBoxColumn();
            colName.HeaderText = Strings.Paste_GridName;
            colName.DataPropertyName = "Name";
            colName.FillWeight = 70;

            _grid.Columns.AddRange(new DataGridViewColumn[] { colNumber, colName });

            // 底部：操作按钮
            FlowLayoutPanel bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 38;
            bottom.FlowDirection = FlowDirection.RightToLeft;
            bottom.Padding = new Padding(4);

            _btnConfirm = new Button();
            _btnConfirm.Text = Strings.Paste_BtnConfirm;
            _btnConfirm.AutoSize = true;
            _btnConfirm.Enabled = false;
            _btnConfirm.Click += new EventHandler(BtnConfirm_Click);

            _btnCancel = new Button();
            _btnCancel.Text = Strings.Paste_BtnCancel;
            _btnCancel.AutoSize = true;
            _btnCancel.DialogResult = DialogResult.Cancel;

            bottom.Controls.AddRange(new Control[] { _btnConfirm, _btnCancel });

            this.Controls.Add(_grid);
            this.Controls.Add(_lblInfo);
            this.Controls.Add(_btnRecognize);
            this.Controls.Add(_txtInput);
            this.Controls.Add(lblInput);
            this.Controls.Add(bottom);

            this.AcceptButton = _btnConfirm;
            this.CancelButton = _btnCancel;
        }

        // ===== 事件 =====

        private void BtnRecognize_Click(object sender, EventArgs e)
        {
            string text = _txtInput.Text;
            // string.IsNullOrWhiteSpace is .NET 4.0+; 2007/2010 target .NET 2.0/3.5
            if (text == null || text.Trim().Length == 0)
            {
                _lblInfo.Text = Strings.Paste_NoInput;
                return;
            }

            try
            {
                string pre = MarkingTextParser.Preprocess(text);
                MarkingSectionResult section = MarkingTextParser.ExtractMarkingSection(pre);
                List<MarkingHit> hits = MarkingTextParser.ExtractAll(section.SectionText);

                _rows.Clear();
                foreach (MarkingHit h in hits)
                {
                    if (string.IsNullOrEmpty(h.Number) && string.IsNullOrEmpty(h.Name)) continue;
                    _rows.Add(new PreviewRow { Number = h.Number, Name = h.Name });
                }

                _grid.DataSource = null;
                _grid.DataSource = _rows;

                string sectionDesc = section.HeaderFound
                    ? string.Format(Strings.Paste_SectionFound, section.SectionText.Length)
                    : Strings.Paste_SectionFallback;
                _lblInfo.Text = string.Format(Strings.Paste_ResultInfo, _rows.Count) + "  " + sectionDesc;
                _btnConfirm.Enabled = _rows.Count > 0;
            }
            catch (Exception ex)
            {
                _lblInfo.Text = string.Format(Strings.Paste_RecognizeFailed, ex.Message);
            }
        }

        private void BtnConfirm_Click(object sender, EventArgs e)
        {
            try
            {
                // 收集编辑后的预览行
                List<PreviewRow> rows = CollectRows();
                if (rows.Count == 0)
                {
                    _lblInfo.Text = Strings.Paste_NoRows;
                    return;
                }

                string dictPath = DictLoader.CurrentPath ?? DictLoader.ResolveDictPath();
                if (dictPath == null)
                {
                    MessageBox.Show(this, Strings.Paste_NoDict, Strings.Paste_Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 覆盖 / 合并选择：是=覆盖，否=合并，取消=不写回
                DialogResult mode = MessageBox.Show(this, Strings.Paste_ConfirmMsg,
                    Strings.Paste_ConfirmTitle,
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (mode == DialogResult.Cancel) return;

                DictModel model = DictLoader.LoadForCurrentDrawing();
                if (model == null)
                {
                    _lblInfo.Text = Strings.Paste_NoDict;
                    return;
                }

                DictModel newModel = DictWriter.BuildWriteModel(model, RowsToWrite(rows), mode == DialogResult.Yes);
                if (newModel == null)
                {
                    _lblInfo.Text = Strings.Paste_NoRows;
                    return;
                }

                string error;
                if (!DictWriter.Write(dictPath, newModel, out error))
                {
                    MessageBox.Show(this, string.Format(Strings.Paste_WriteFail, error),
                        Strings.Paste_Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                DictLoader.NotifySelfWrite(newModel, dictPath);
                _lblInfo.Text = string.Format(Strings.Paste_WriteOk, newModel.Entries.Count, dictPath);
                MessageBox.Show(this, string.Format(Strings.Paste_WriteOk, newModel.Entries.Count, dictPath),
                    Strings.Paste_Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                _lblInfo.Text = string.Format(Strings.Paste_WriteFail, ex.Message);
                MessageBox.Show(this, string.Format(Strings.Paste_WriteFail, ex.Message),
                    Strings.Paste_Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 收集表格行并转为写回行类型（跳过空行、去重编号）。
        /// </summary>
        private List<DictWriteRow> RowsToWrite(List<PreviewRow> rows)
        {
            List<DictWriteRow> result = new List<DictWriteRow>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (PreviewRow r in rows)
            {
                string number = r.Number != null ? r.Number.Trim() : "";
                string name = r.Name != null ? r.Name.Trim() : "";
                if (number.Length == 0 || name.Length == 0) continue;
                if (seen.ContainsKey(number)) continue;
                seen[number] = true;
                result.Add(new DictWriteRow { Number = number, Name = name });
            }
            return result;
        }

        /// <summary>
        /// 从表格收集编辑后的行：跳过空行、合并重复编号（取首次出现），返回有效行。
        /// </summary>
        private List<PreviewRow> CollectRows()
        {
            List<PreviewRow> result = new List<PreviewRow>();
            Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int idx = 0;

            _grid.EndEdit();

            foreach (DataGridViewRow r in _grid.Rows)
            {
                idx++;
                if (r.IsNewRow) continue;

                string number = r.Cells[0].Value as string;
                string name = r.Cells[1].Value as string;
                number = number != null ? number.Trim() : "";
                name = name != null ? name.Trim() : "";

                if (number.Length == 0 || name.Length == 0) continue;

                if (seen.TryGetValue(number, out int firstIdx))
                {
                    // 重复编号：保留首次出现行的 name（用户可自行去重）
                    continue;
                }
                seen[number] = idx;
                result.Add(new PreviewRow { Number = number, Name = name });
            }
            return result;
        }
    }

    /// <summary>预览表格行（DataGridView 绑定，行内可编辑）</summary>
    public class PreviewRow
    {
        public string Number { get; set; } = "";
        public string Name { get; set; } = "";
    }
}
