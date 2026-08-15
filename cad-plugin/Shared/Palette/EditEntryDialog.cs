using PatentMarker.IO;
using PatentMarker.I18n;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PatentMarker.Palette
{
    /// <summary>
    /// v4.0 功能 2：单条目编辑对话框（右键/F2 编辑或「新增」按钮打开）。
    /// 可改 number/name；新增条目；删除条目（确认框）。
    /// 交互约定：
    ///   DialogResult.OK   = 已保存（写回 dict.json）
    ///   DialogResult.Abort = 已删除
    /// </summary>
    public class EditEntryDialog : Form
    {
        private TextBox _txtNumber = null;
        private TextBox _txtName = null;
        private Button _btnSave = null;
        private Button _btnDelete = null;
        private Button _btnCancel = null;
        private Label _lblError = null;

        private readonly DictModel _model;
        private readonly DictEntry _entry;
        private readonly string _dictPath;

        /// <param name="model">当前字典模型（写回时原地修改后落盘）</param>
        /// <param name="entry">要编辑的条目；null 表示新增</param>
        /// <param name="dictPath">dict.json 路径</param>
        public EditEntryDialog(DictModel model, DictEntry entry, string dictPath)
        {
            _model = model;
            _entry = entry;
            _dictPath = dictPath;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = _entry == null ? Strings.Edit_TitleAdd : Strings.Edit_TitleEdit;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ClientSize = new Size(340, 190);
            this.Font = new Font("Microsoft Sans Serif", 9F);

            Label lblNumber = new Label();
            lblNumber.Text = Strings.Edit_Number;
            lblNumber.SetBounds(14, 14, 60, 24);

            _txtNumber = new TextBox();
            _txtNumber.SetBounds(80, 14, 240, 24);
            if (_entry != null) _txtNumber.Text = _entry.Number;

            Label lblName = new Label();
            lblName.Text = Strings.Edit_Name;
            lblName.SetBounds(14, 48, 60, 24);

            _txtName = new TextBox();
            _txtName.SetBounds(80, 48, 240, 24);
            if (_entry != null) _txtName.Text = _entry.Name;

            _lblError = new Label();
            _lblError.ForeColor = Color.Firebrick;
            _lblError.SetBounds(14, 78, 306, 22);
            _lblError.Text = "";

            _btnSave = new Button();
            _btnSave.Text = Strings.Edit_BtnSave;
            _btnSave.SetBounds(14, 150, 90, 28);
            _btnSave.Click += new EventHandler(BtnSave_Click);

            _btnDelete = new Button();
            _btnDelete.Text = Strings.Edit_BtnDelete;
            _btnDelete.SetBounds(110, 150, 90, 28);
            _btnDelete.Click += new EventHandler(BtnDelete_Click);
            if (_entry == null) _btnDelete.Enabled = false;

            _btnCancel = new Button();
            _btnCancel.Text = Strings.Paste_BtnCancel;
            _btnCancel.SetBounds(210, 150, 90, 28);
            _btnCancel.DialogResult = DialogResult.Cancel;

            this.Controls.AddRange(new Control[] { lblNumber, _txtNumber, lblName, _txtName, _lblError, _btnSave, _btnDelete, _btnCancel });
            this.AcceptButton = _btnSave;
            this.CancelButton = _btnCancel;
        }

        // ===== 事件 =====

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (ApplyAndWrite())
                this.DialogResult = DialogResult.OK;
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            if (_entry == null) return;
            DialogResult confirm = MessageBox.Show(this, Strings.Edit_DeleteConfirm,
                Strings.Edit_TitleEdit, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            List<DictEntry> originalEntries = _model.Entries == null
                ? null : new List<DictEntry>(_model.Entries);
            DictMetadata originalMetadata = _model.Metadata;
            string originalModifiedBy = originalMetadata != null ? originalMetadata.ModifiedBy : null;
            string originalModifiedAt = originalMetadata != null ? originalMetadata.ModifiedAt : null;

            if (!DictWriter.TryRemoveEntry(_model, _entry))
            {
                _lblError.Text = Strings.Edit_DeleteFailed;
                return;
            }

            if (!WriteBack())
            {
                RestoreModelMutation(originalEntries, originalMetadata,
                    originalModifiedBy, originalModifiedAt, null, null, false);
                _lblError.Text = _lastWriteError;
                return;
            }
            this.DialogResult = DialogResult.Abort;
        }

        private string _lastWriteError = "";

        /// <summary>校验并应用编辑（改号冲突/空值检查），成功后落盘。返回是否成功。</summary>
        private bool ApplyAndWrite()
        {
            string newNumber = _txtNumber.Text;
            string newName = _txtName.Text;

            List<DictEntry> originalEntries = _model.Entries == null
                ? null : new List<DictEntry>(_model.Entries);
            DictMetadata originalMetadata = _model.Metadata;
            string originalModifiedBy = originalMetadata != null ? originalMetadata.ModifiedBy : null;
            string originalModifiedAt = originalMetadata != null ? originalMetadata.ModifiedAt : null;
            string originalNumber = _entry != null ? _entry.Number : null;
            string originalName = _entry != null ? _entry.Name : null;

            string conflict;
            if (!DictWriter.TryApplyEdit(_model, _entry, newNumber, newName, out conflict))
            {
                RestoreModelMutation(originalEntries, originalMetadata,
                    originalModifiedBy, originalModifiedAt, null, null, false);
                if (conflict != null)
                    _lblError.Text = string.Format(Strings.Edit_NumberConflict, conflict);
                else
                    _lblError.Text = Strings.Edit_EmptyField;
                return false;
            }

            if (!WriteBack())
            {
                RestoreModelMutation(originalEntries, originalMetadata,
                    originalModifiedBy, originalModifiedAt, originalNumber, originalName, true);
                _lblError.Text = _lastWriteError;
                return false;
            }
            return true;
        }

        private void RestoreModelMutation(List<DictEntry> originalEntries, DictMetadata originalMetadata,
            string originalModifiedBy, string originalModifiedAt,
            string originalNumber, string originalName, bool restoreEntryText)
        {
            if (restoreEntryText && _entry != null)
            {
                _entry.Number = originalNumber;
                _entry.Name = originalName;
            }
            _model.Entries = originalEntries;
            if (originalMetadata == null)
            {
                _model.Metadata = null;
            }
            else
            {
                _model.Metadata = originalMetadata;
                originalMetadata.ModifiedBy = originalModifiedBy;
                originalMetadata.ModifiedAt = originalModifiedAt;
            }
        }

        private bool WriteBack()
        {
            string error;
            if (!DictWriter.Write(_dictPath, _model, out error))
            {
                _lastWriteError = error;
                return false;
            }
            DictLoader.NotifySelfWrite(_model, _dictPath);
            return true;
        }
    }
}
