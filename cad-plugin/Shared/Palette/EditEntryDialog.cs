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
        private readonly object _document;
        private readonly string _identity;
        private readonly IRenameFollowUpGuard _guard;

        // R-004（G24）：写入方保存的提交事实。成功 Write 已经通过
        // NotifySelfWrite 登记真实序列化身份，这里在写成功的那一刻把它连同
        // Document、DWG 路径、字典路径一起留住，供调用方刷新列表后读取。
        private readonly RenameCommitFact _commit = new RenameCommitFact();

        /// <param name="model">当前字典模型（写回时原地修改后落盘）</param>
        /// <param name="entry">要编辑的条目；null 表示新增</param>
        /// <param name="dictPath">dict.json 路径</param>
        /// <param name="document">打开对话框时的目标图纸</param>
        /// <param name="identity">打开对话框时的字典内容身份</param>
        /// <param name="guard">
        /// R-004：改号前守卫。在写 JSON 之前判定是否存在必须先完成或显式放弃的
        /// 待同步操作；为 null 时不做该判定（新增条目等不涉及改号的入口）。
        /// </param>
        public EditEntryDialog(DictModel model, DictEntry entry, string dictPath, object document, string identity)
            : this(model, entry, dictPath, document, identity, null)
        {
        }

        public EditEntryDialog(DictModel model, DictEntry entry, string dictPath, object document, string identity,
            IRenameFollowUpGuard guard)
        {
            _model = model;
            _entry = entry;
            _dictPath = dictPath;
            _document = document;
            _identity = identity;
            _guard = guard;
            InitializeComponent();
        }

        /// <summary>
        /// R-004（G24）：本次对话框**成功写出的那一版**的提交事实。
        ///
        /// 调用方（DictPaletteControl.EditSelectedEntry）必须在刷新列表**之后**
        /// 仍然读这一份，而不是重新 Reload 再从新模型取身份：对话框关闭到首次
        /// 同步之间的窗口里，外部合法 JSON、损坏文件、被删除的文件都不能被
        /// 当成「本次提交版本」。空身份不会退化为回读当前文件，
        /// <c>MISSING</c> 也不算成功提交（见 <see cref="RenameCommitFact.HasTrustedIdentity"/>）。
        /// 编号／名称语义与写入失败的回滚保持不变。
        /// </summary>
        public RenameCommitFact CommitFact
        {
            get { return _commit; }
        }

        /// <summary>清空提交事实（写入失败或删除后重新开始）。</summary>
        private void ResetCommitFact()
        {
            _commit.Written = false;
            _commit.Document = null;
            _commit.DrawingPath = "";
            _commit.DictPath = "";
            _commit.DictIdentity = "";
            _commit.OldNumber = "";
            _commit.NewNumber = "";
            _commit.OldName = "";
            _commit.NewName = "";
        }

        /// <summary>
        /// 记录一次成功的写入。身份取自 <see cref="DictLoader.GetIdentity"/>：
        /// 它只在缓存里仍然是**这次写出的同一个模型对象**时才返回身份，因此
        /// 拿到的就是 DictWriter 真正序列化的那一份字节，而不是之后回读的结果。
        /// </summary>
        private void CaptureCommitFact(string oldNumber, string newNumber,
            string oldName, string newName)
        {
            ResetCommitFact();
            _commit.Written = true;
            _commit.Document = _document;
            _commit.DrawingPath = RenameSyncContext.DrawingPathOf(_document);
            _commit.DictPath = RenameSyncContext.FullPath(_dictPath);
            _commit.DictIdentity = DictLoader.GetIdentity(_model, _dictPath);
            _commit.OldNumber = oldNumber == null ? "" : oldNumber;
            _commit.NewNumber = newNumber == null ? "" : newNumber;
            _commit.OldName = oldName == null ? "" : oldName;
            _commit.NewName = newName == null ? "" : newName;
            if (_commit.DictIdentity == null) _commit.DictIdentity = "";
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
            ResetCommitFact();
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

            // R-004：有待同步操作时，后续相关改号必须先完成或显式放弃。
            // 这里在第二次 JSON 写入之前拦住，不猜测串接映射（302→303）。
            if (_guard != null && _entry != null &&
                !NumberIdentity.AreEqual(originalNumber, newNumber))
            {
                RenameFollowUpDecision decision = _guard.Evaluate(originalNumber, newNumber);
                if (decision != null && !decision.Allowed)
                {
                    _lblError.Text = decision.Reason;
                    return false;
                }
            }

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
            // R-004（G24）：写成功了，就在这一刻留住「这次提交写了什么」。
            CaptureCommitFact(originalNumber, _entry != null ? _entry.Number : newNumber,
                originalName, _entry != null ? _entry.Name : newName);
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
            if (!DictPaletteWorkflow.IsCurrentContext(_document, _dictPath))
            {
                _lastWriteError = Strings.Dict_Changed;
                return false;
            }
            string error;
            if (!DictWriter.Write(_dictPath, _model, _identity, out error))
            {
                _lastWriteError = DictPaletteWorkflow.DescribeWriteError(error);
                return false;
            }
            return true;
        }
    }
}
