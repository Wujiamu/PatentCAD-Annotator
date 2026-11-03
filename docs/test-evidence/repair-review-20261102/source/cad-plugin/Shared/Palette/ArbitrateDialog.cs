using PatentMarker.IO;
using PatentMarker.I18n;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace PatentMarker.Palette
{
    /// <summary>
    /// v4.0 功能 3：双端冲突裁决对话框（三选）。
    /// 打开时面板已确认存在 Word 备份（FindWordBackup 非空），本对话框只做 UI 与动作转发：
    ///   DialogResult.OK    = 采用 Word 版（删除备份，当前 dict.json 即 Word 最新导出）
    ///   DialogResult.Yes   = 恢复 CAD 版（备份覆盖回 + 清除 CAD 标记 + 删备份）
    ///   DialogResult.Cancel = 稍后再说（不做任何动作）
    /// 失败时错误显示在对话框内，不关闭。
    ///
    /// R-034：对话框持有的是打开时抓取的「恢复选择上下文」（磁盘真实身份），
    /// 不是面板缓存模型。当前文件解析失败时仍可恢复备份；只有「采用 Word 版」
    /// 需要当前内容仍是合法字典。动作期间才短时持有产品锁，不把锁带到对话框生命周期。
    /// </summary>
    public class ArbitrateDialog : Form
    {
        private Label _lblInfo = null;
        private Button _btnKeepWord = null;
        private Button _btnRestoreCad = null;
        private Button _btnLater = null;

        private readonly string _dictPath;
        private readonly string _backupPath;
        private readonly string _identity;
        private readonly string _backupIdentity;
        private readonly object _document;

        private readonly DictConflict.RecoverySelection _selection;

        /// <summary>兼容入口：只带身份字符串，动作时按当时磁盘字节复核。</summary>
        public ArbitrateDialog(string dictPath, object document, string identity, string backupPath, string backupIdentity)
            : this(BuildSelection(dictPath, document, identity, backupPath, backupIdentity))
        {
        }

        /// <summary>R-034 入口：使用打开对话框时抓取的恢复选择上下文。</summary>
        public ArbitrateDialog(DictConflict.RecoverySelection selection)
        {
            _selection = selection;
            if (selection != null)
            {
                _dictPath = selection.DictPath;
                _document = selection.Document;
                _identity = selection.CurrentIdentity;
                _backupPath = selection.BackupPath;
                _backupIdentity = selection.BackupIdentity;
            }
            InitializeComponent();
        }

        private static DictConflict.RecoverySelection BuildSelection(string dictPath, object document,
            string identity, string backupPath, string backupIdentity)
        {
            DictConflict.RecoverySelection selection = new DictConflict.RecoverySelection();
            selection.DictPath = dictPath;
            selection.Document = document;
            selection.CurrentIdentity = identity;
            selection.BackupPath = backupPath;
            selection.BackupIdentity = backupIdentity;
            return selection;
        }

        private bool IsCurrentContext()
        {
            return DictPaletteWorkflow.IsCurrentContext(_document, _dictPath);
        }

        /// <summary>恢复成功后由面板读取：写入是否发生、备份是否已清理。</summary>
        public DictConflict.RecoveryOutcome Outcome { get { return _outcome; } }
        private DictConflict.RecoveryOutcome _outcome = new DictConflict.RecoveryOutcome();

        private void InitializeComponent()
        {
            this.Text = Strings.Conflict_Title;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ClientSize = new Size(460, 170);
            this.Font = new Font("Microsoft Sans Serif", 9F);

            _lblInfo = new Label();
            _lblInfo.SetBounds(14, 12, 432, 70);
            _lblInfo.Text = string.Format(Strings.Conflict_Msg, _backupPath);

            _btnKeepWord = new Button();
            _btnKeepWord.Text = Strings.Conflict_BtnKeepWord;
            _btnKeepWord.SetBounds(14, 128, 132, 30);
            _btnKeepWord.Click += new EventHandler(BtnKeepWord_Click);
            // 当前文件损坏时没有可「采用」的当前版本；按钮直接禁用，
            // 而不是让用户点击后拿到一个含糊的失败。
            _btnKeepWord.Enabled = _selection == null || _selection.CurrentParsed;

            _btnRestoreCad = new Button();
            _btnRestoreCad.Text = Strings.Conflict_BtnRestoreCad;
            _btnRestoreCad.SetBounds(160, 128, 136, 30);
            _btnRestoreCad.Click += new EventHandler(BtnRestoreCad_Click);
            _btnRestoreCad.Enabled = _selection == null || _selection.BackupParsed;

            _btnLater = new Button();
            _btnLater.Text = Strings.Conflict_BtnLater;
            _btnLater.SetBounds(344, 128, 102, 30);
            _btnLater.DialogResult = DialogResult.Cancel;

            this.Controls.AddRange(new Control[] { _lblInfo, _btnKeepWord, _btnRestoreCad, _btnLater });
            this.CancelButton = _btnLater;
        }

        // ===== 事件 =====

        private void BtnKeepWord_Click(object sender, EventArgs e)
        {
            DictConflict.RecoveryOutcome outcome =
                DictConflict.KeepSelectedVersion(_selection, new DictConflict.ContextCheck(IsCurrentContext));
            _outcome = outcome;
            if (!outcome.Succeeded)
            {
                _lblInfo.Text = string.Format(Strings.Conflict_Failed, DictPaletteWorkflow.DescribeWriteError(outcome.Error));
                return;
            }
            this.DialogResult = DialogResult.OK;
        }

        private void BtnRestoreCad_Click(object sender, EventArgs e)
        {
            DictConflict.RecoveryOutcome outcome =
                DictConflict.RestoreSelectedBackup(_selection, new DictConflict.ContextCheck(IsCurrentContext));
            _outcome = outcome;
            if (!outcome.Succeeded)
            {
                _lblInfo.Text = string.Format(Strings.Conflict_Failed, DescribeRecoveryFailure(outcome));
                return;
            }
            this.DialogResult = DialogResult.Yes;
        }

        /// <summary>
        /// 恢复失败时如实说明已经发生的写入。字典字节已替换但基线或备份清理
        /// 失败时，不能让用户以为什么都没写、然后反复点一个永远过期的快照。
        /// </summary>
        private static string DescribeRecoveryFailure(DictConflict.RecoveryOutcome outcome)
        {
            if (outcome.Wrote)
            {
                // 写入已发生：说明现状并要求重载，而不是笼统的「内容已变化」。
                return DictPaletteWorkflow.DescribeWriteError(DictFileVisibility.BaselineError) +
                    " " + DictPaletteWorkflow.DescribeWriteError(outcome.Error);
            }
            return DictPaletteWorkflow.DescribeWriteError(outcome.Error);
        }    }
}
