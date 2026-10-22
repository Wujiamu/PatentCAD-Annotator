using System;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;

namespace PatentMarker.IO
{
    /// <summary>
    /// Runtime settings shared by the palette, style initializer and commands.
    /// Keeping one store prevents configuration values from being shadowed by UI fields.
    /// </summary>
    public class PatRuntimeSettings
    {
        public double TextHeight = 3.5;
        // Keep the historical behavior by default: new annotations use a leader.
        public bool HasLeader = true;
        public bool UnderlineText = false;
        public bool HasArrowHead = false;
        public double ArrowSize = 2.5;
        public bool IsSplined = true;
        // 默认使用三点模式；用户点击点数按钮后才切换到无限点模式。
        public bool ThreePointMode = true;
        public double MarginToFrame = 5.0;
    }

    public static class PatSettingsStore
    {
        public const double DefaultTextHeight = 3.5;
        public const double DefaultArrowSize = 2.5;
        public const double DefaultMarginToFrame = 5.0;

        private static PatRuntimeSettings _current = new PatRuntimeSettings();
        private static readonly Dictionary<string, PatRuntimeSettings> _settingsByDrawing =
            new Dictionary<string, PatRuntimeSettings>(StringComparer.OrdinalIgnoreCase);

        public static PatRuntimeSettings Current
        {
            get { return _current; }
        }

        public static void Activate(string drawingPath)
        {
            string key = NormalizeDrawingPath(drawingPath);
            if (_settingsByDrawing.ContainsKey(key))
            {
                _current = _settingsByDrawing[key];
                return;
            }

            _current = new PatRuntimeSettings();
            _settingsByDrawing[key] = _current;
        }

        public static void Release(string drawingPath)
        {
            string key = NormalizeDrawingPath(drawingPath);
            _settingsByDrawing.Remove(key);
            _current = new PatRuntimeSettings();
        }

        private static string NormalizeDrawingPath(string drawingPath)
        {
            if (drawingPath == null || drawingPath.Length == 0)
                return "<default>";
            try { return Path.GetFullPath(drawingPath); }
            catch { return drawingPath; }
        }

        public static void ResetConfigDefaults()
        {
            _current.TextHeight = DefaultTextHeight;
            _current.MarginToFrame = DefaultMarginToFrame;
        }

        public static void Apply(PatConfig config)
        {
            if (config == null) return;

            if (config.PatStyle != null && config.PatStyle.TextHeight > 0)
                _current.TextHeight = config.PatStyle.TextHeight;

            if (config.Align != null && config.Align.MarginToFrame > 0)
                _current.MarginToFrame = config.Align.MarginToFrame;
        }
    }

    /// <summary>
    /// Controls the Explorer visibility of a dictionary file without changing
    /// its JSON content.  The v5.2 default remains Hidden + System, while the
    /// palette can opt a specific existing dictionary into manual editing.
    /// </summary>
    public static class DictFileVisibility
    {
        private const FileAttributes HiddenSystem = FileAttributes.Hidden | FileAttributes.System;

        [DllImport("advapi32.dll", EntryPoint = "SetFileSecurityW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileSecurity(string path, uint information, byte[] descriptor);

        [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string from, string to, uint flags);

        private static string ReadSecurity(string path)
        {
            return new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(
                AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);
        }

        private static void VerifyAndRestoreSecurity(string path, byte[] descriptor, string expected)
        {
            if (ReadSecurity(path) == expected) return;
            RawSecurityDescriptor original = new RawSecurityDescriptor(descriptor, 0);
            if ((original.ControlFlags & ControlFlags.DiscretionaryAclAutoInherited) == 0)
            {
                // ReplaceFile can convert a legacy inherited DACL to the
                // automatic-inheritance model, adding explicit ACEs. The old
                // API deliberately avoids that conversion. It is used only
                // when the original descriptor also lacked AUTO_INHERITED.
                // Write the DACL only: never owner/group/SACL or privileges.
                if (!SetFileSecurity(path, 4, descriptor))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            else
            {
                FileSecurity restored = new FileSecurity();
                restored.SetSecurityDescriptorBinaryForm(descriptor, AccessControlSections.Access);
                new FileInfo(path).SetAccessControl(restored);
            }
            if (ReadSecurity(path) != expected)
                throw new IOException("Original dictionary security descriptor was not preserved.");
        }

        /// <summary>
        /// Stage a complete UTF-8 file beside the target without changing the
        /// target's attributes. ReplaceFile retains streams and creation
        /// metadata. Verify exact file security after its ACL merge and keep
        /// the original file until that verification and cleanup succeed.
        /// </summary>
        public static bool TryWriteAtomic(string path, string json, out string error)
        {
            error = "";
            string temporary = null;
            string rollback = null;
            bool ownsTemporary = false;
            bool ownsRollback = false;
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    error = "字典目录不存在: " + (directory ?? "");
                    return false;
                }
                bool existing = File.Exists(path);
                FileAttributes attributes = existing
                    ? File.GetAttributes(path) : FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive;
                if (existing && (attributes & FileAttributes.ReadOnly) != 0)
                {
                    error = "字典文件为只读: " + path;
                    return false;
                }
                byte[] security = existing
                    ? new FileInfo(path).GetAccessControl().GetSecurityDescriptorBinaryForm() : null;
                string expectedSecurity = existing ? ReadSecurity(path) : null;
                temporary = path + ".pat-" + Guid.NewGuid().ToString("N") + ".tmp";
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                {
                    ownsTemporary = true;
                    using (StreamWriter writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
                        writer.Write(json);
                }
                // Set all requested DOS attributes on our own staged file;
                // never normalize the user's original, even temporarily.
                File.SetAttributes(temporary, attributes);
                if (existing)
                {
                    rollback = temporary + ".rollback";
                    if (File.Exists(rollback))
                        throw new IOException("Dictionary rollback path already exists.");
                    ownsRollback = true;
                    File.Replace(temporary, path, rollback);
                    ownsTemporary = false;
                    VerifyAndRestoreSecurity(path, security, expectedSecurity);
                    File.Delete(rollback);
                    rollback = null;
                }
                else
                    File.Move(temporary, path);
                ownsTemporary = false;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                if (ownsRollback && rollback != null && File.Exists(rollback))
                {
                    // This renames the ORIGINAL file back, including its
                    // streams and ACL. It is never a new-data overwrite
                    // fallback for a denied ReplaceFile operation.
                    if (!MoveFileEx(rollback, path, 1 | 8))
                        error += "; 原文件恢复失败: " + new Win32Exception(Marshal.GetLastWin32Error()).Message
                            + "; 原文件保留于: " + rollback;
                    else
                        error += "; 原文件已恢复";
                }
                return false;
            }
            finally
            {
                if (ownsTemporary && temporary != null)
                {
                    try
                    {
                        if (File.Exists(temporary))
                        {
                            File.SetAttributes(temporary, FileAttributes.Normal);
                            File.Delete(temporary);
                        }
                    }
                    catch (Exception cleanup)
                    {
                        error += "; 临时文件清理失败: " + cleanup.Message;
                    }
                }
            }
        }

        public static bool IsVisible(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            try
            {
                return (File.GetAttributes(path) & HiddenSystem) == 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool TrySetVisible(string path, bool visible, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                error = "字典文件不存在: " + (path ?? "");
                return false;
            }

            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if (visible)
                    attributes &= ~HiddenSystem;
                else
                    attributes |= HiddenSystem;
                File.SetAttributes(path, attributes);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }
}
