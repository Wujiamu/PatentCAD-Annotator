using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using PatentMarker.IO;
using PatentMarker.I18n;
using System;
using AppAcad = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PatentMarker.Palette
{
    /// <summary>
    /// PATPALETTE — 可停靠侧面板（五个 AutoCAD 版本共用同一源码）。
    /// </summary>
    public class PatPaletteCommand
    {
        private static PaletteSet _paletteSet;
        private static DictPaletteControl _control;

        // 修复 D1：使用静态字段而非自动属性初始化器（C# 3.0 不支持 = 3.5）
        public static string PendingNumber;
        public static string PendingName;
        public static double TextHeight { get { return PatSettingsStore.Current.TextHeight; } set { PatSettingsStore.Current.TextHeight = value; } }
        // v2：箭头开关，默认无箭头（专利标注惯例）。由面板按钮切换，影响后续新建引线。
        public static bool HasArrowHead { get { return PatSettingsStore.Current.HasArrowHead; } set { PatSettingsStore.Current.HasArrowHead = value; } }
        public static bool HasLeader { get { return PatSettingsStore.Current.HasLeader; } set { PatSettingsStore.Current.HasLeader = value; } }
        public static bool UnderlineText { get { return PatSettingsStore.Current.UnderlineText; } set { PatSettingsStore.Current.UnderlineText = value; } }
        // v2.1：箭头大小，由面板 NumericUpDown 调节。创建引线时同步到 PAT_DIM 样式。
        public static double ArrowSize { get { return PatSettingsStore.Current.ArrowSize; } set { PatSettingsStore.Current.ArrowSize = value; } }
        // v2.1：样条曲线开关，默认样条曲线。由面板按钮切换，影响后续新建引线。
        public static bool IsSplined { get { return PatSettingsStore.Current.IsSplined; } set { PatSettingsStore.Current.IsSplined = value; } }
        // v3.1：三点模式开关，默认开启。点击点数按钮后切换到无限拐点模式。
        // 三点模式固定采集：附着点 → 1 个拐点 → 文字位置，第 3 点点击后自动创建。
        public static bool ThreePointMode { get { return PatSettingsStore.Current.ThreePointMode; } set { PatSettingsStore.Current.ThreePointMode = value; } }

        [CommandMethod("PATPALETTE")]
        [CommandMethod("BIAOZHU")]   // 拼音别名：标注
        [CommandMethod("BZ")]        // 拼音缩写别名
        public void Run()
        {
            PatentMarkerApp.RawLog("=== PATPALETTE START ===");

            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) { PatentMarkerApp.RawLog("PATPALETTE ABORT: no active document"); return; }

            bool isNew = false;
            if (_paletteSet == null)
            {
                isNew = true;
                // 2007 PaletteSet API：仅使用基本样式标志
                _paletteSet = new PaletteSet("PatentMarker",
                    new Guid("D4F5A1B2-3C4D-4E5F-8A7B-9C0D1E2F3A4B"));
                _paletteSet.Style = PaletteSetStyles.ShowAutoHideButton |
                                    PaletteSetStyles.ShowCloseButton;
                _paletteSet.MinimumSize = new System.Drawing.Size(280, 400);
                _paletteSet.Visible = true;

                _control = new DictPaletteControl();
                _paletteSet.Add(Strings.Palette_TabTitle, _control);
            }
            else
            {
                _paletteSet.Visible = true;
            }

            ReloadRuntimeSettingsForCurrentDrawing();
            LoadDictForCurrentDrawing();

            AppAcad.DocumentManager.DocumentActivated -= DocManager_DocumentActivated;
            AppAcad.DocumentManager.DocumentActivated += DocManager_DocumentActivated;
            AppAcad.DocumentManager.DocumentToBeDestroyed -= DocManager_DocumentToBeDestroyed;
            AppAcad.DocumentManager.DocumentToBeDestroyed += DocManager_DocumentToBeDestroyed;

            PatentMarkerApp.RawLog("PATPALETTE: palette " + (isNew ? "created" : "shown"));
        }

        private static void DocManager_DocumentActivated(object sender, DocumentCollectionEventArgs e)
        {
            // 修复 B6：事件处理器中加 null 守卫，防止 Terminate 后崩溃
            if (_control == null) return;
            ReloadRuntimeSettingsForCurrentDrawing();
            LoadDictForCurrentDrawing();
        }

        private static void ReloadRuntimeSettingsForCurrentDrawing()
        {
            Document activeDocument = IO.RuntimeHost.ActiveDocument;
            string drawingPath = activeDocument != null ? activeDocument.Name : "";
            IO.PatSettingsStore.Activate(drawingPath);
            IO.PatSettingsStore.ResetConfigDefaults();
            IO.ConfigLoader.Current = IO.ConfigLoader.ActivateForDrawing(drawingPath);
            if (IO.ConfigLoader.Current != null)
                IO.PatSettingsStore.Apply(IO.ConfigLoader.Current);
            if (_control != null)
                _control.ApplyRuntimeSettings();
        }

        private static void DocManager_DocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
        {
            Document closing = e != null ? e.Document : null;
            string drawingPath = closing != null ? closing.Name : "";
            IO.DictLoader.ReleaseForDrawing(drawingPath);
            IO.PatSettingsStore.Release(drawingPath);
            IO.ConfigLoader.ReleaseDrawing(drawingPath);
        }

        public static void LoadDictForCurrentDrawing()
        {
            if (_control == null) return;

            try
            {
                var dict = DictLoader.LoadForCurrentDrawing();
                if (dict != null)
                    _control.LoadDict(dict);
                else
                    _control.ShowNoDict();
            }
            catch (System.Exception ex)
            {
            var doc = IO.RuntimeHost.ActiveDocument;
                if (doc != null)
                    doc.Editor.WriteMessage("\nPatentMarker: dict load error: " + ex.Message + "\n");
                if (_control != null)
                    _control.ShowNoDict();
            }
        }

        public static void DisposePalette()
        {
            AppAcad.DocumentManager.DocumentActivated -= DocManager_DocumentActivated;
            AppAcad.DocumentManager.DocumentToBeDestroyed -= DocManager_DocumentToBeDestroyed;
            if (_control != null)
            {
                _control.Dispose();
                _control = null;
            }
            _paletteSet = null;
        }
    }
}
