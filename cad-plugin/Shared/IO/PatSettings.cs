using System;
using System.Collections.Generic;
using System.IO;

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
