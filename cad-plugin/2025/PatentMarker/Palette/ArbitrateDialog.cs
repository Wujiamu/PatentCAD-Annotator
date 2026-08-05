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
    ///   DialogResult.Cancel = 稍后再说（不做任何操作）
    /// 失败时错误显示在对话框内，不关闭。
    /// </summary>
    public class ArbitrateDialog : Form
    {
        private Label _lblInfo = null!;
        private Button _btnKeepWord = null!;
        private Button _btnRestoreCad = null!;
        private Button _btnLater = null!;

        private readonly string _dictPath;
        private readonly string _backupPath;

        /// <param name="dictPath">当前 dict.json 路径（备份与其同目录）</param>
        public ArbitrateDialog(string dictPath)
        {
            _dictPath = dictPath;
            _backupPath = DictConflict.FindWordBackup(dictPath) ?? "";
            InitializeComponent();
        }

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

            _btnRestoreCad = new Button();
            _btnRestoreCad.Text = Strings.Conflict_BtnRestoreCad;
            _btnRestoreCad.SetBounds(160, 128, 136, 30);
            _btnRestoreCad.Click += new EventHandler(BtnRestoreCad_Click);

            _btnLater = new Button();
            _btnLater.Text = Strings.Conflict_BtnLater;
            _btnLater.SetBounds(344, 128, 102, 30);
            _btnLater.DialogResult = DialogResult.Cancel;

            this.Controls.AddRange(new Control[] { _lblInfo, _btnKeepWord, _btnRestoreCad, _btnLater });
            this.CancelButton = _btnLater;
        }

        // ===== 事件 =====

        private void BtnKeepWord_Click(object? sender, EventArgs e)
        {
            string error;
            if (!DictConflict.ResolveKeepWord(_backupPath, out error))
            {
                _lblInfo.Text = string.Format(Strings.Conflict_Failed, error);
                return;
            }
            this.DialogResult = DialogResult.OK;
        }

        private void BtnRestoreCad_Click(object? sender, EventArgs e)
        {
            string error;
            if (DictConflict.ResolveRestoreCad(_dictPath, _backupPath, out error) == null)
            {
                _lblInfo.Text = string.Format(Strings.Conflict_Failed, error);
                return;
            }
            this.DialogResult = DialogResult.Yes;
        }
    }
}
