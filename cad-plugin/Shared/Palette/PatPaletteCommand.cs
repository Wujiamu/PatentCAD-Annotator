using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using PatentMarker.IO;
using PatentMarker.I18n;
using System;
using System.Collections.Generic;
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

        // 面板请求是异步交给 AutoCAD 命令队列的，不能再用一个跨图纸的全局槽位传递。
        // Document 作为 key 也能区分同名的未保存图纸。
        private sealed class MarkDispatchState
        {
            public string Number;
            public string Name;
            public bool HasPending;
            public string PendingPanelCommand = "";
            public string QueuedPanelCommand = "";
            public string ActivePanelCommand = "";
            public bool CancelRequested;
            public bool CommandEventsAttached;
            public bool LaunchQueued;
            public DateTime LaunchQueuedAtUtc;
        }

        private static readonly Dictionary<Document, MarkDispatchState> _markDispatchStates =
            new Dictionary<Document, MarkDispatchState>();

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
            LoadDictForCurrentDrawing(true);

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
            // Keep the target document's last completed PATCHECK result when
            // its dictionary is unchanged. If Word changed the dictionary
            // while this document was inactive, the old highlight is stale.
            LoadDictForCurrentDrawing(false);
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
            ClearMarkDispatchState(closing);
            if (closing != null)
                Commands.PatCheckResult.Release(closing);
            string drawingPath = closing != null ? closing.Name : "";
            IO.DictLoader.ReleaseForDrawing(drawingPath);
            IO.PatSettingsStore.Release(drawingPath);
            IO.ConfigLoader.ReleaseDrawing(drawingPath);
        }

        /// <summary>接受面板标注请求，并按目标图纸保存待执行状态。</summary>
        public static bool RequestMark(Document doc, string number, string name)
        {
            if (doc == null || IsNullOrWhiteSpace(number)) return false;

            MarkDispatchState state = GetMarkDispatchState(doc, true);
            AttachCommandEvents(doc, state);
            state.Number = number;
            state.Name = name != null ? name : "";
            state.HasPending = true;
            state.PendingPanelCommand = "PATMARK";

            PatentMarkerApp.RawLog("PATMARK request accepted: doc=" + doc.Name
                + ", number=" + number);
            return TryDispatchPending(doc);
        }

        /// <summary>
        /// 接受其他面板命令。面板启动的交互命令可以互相切换；外部或原生命令
        /// 仍保持原状，待其结束后再由面板计时器提交最新请求。
        /// </summary>
        public static bool RequestPanelCommand(Document doc, string command)
        {
            if (doc == null) return false;
            string normalized = NormalizePanelCommand(command);
            if (!IsDrawingPanelCommand(normalized)) return false;

            MarkDispatchState state = GetMarkDispatchState(doc, true);
            AttachCommandEvents(doc, state);
            state.Number = null;
            state.Name = null;
            state.HasPending = false;
            state.PendingPanelCommand = normalized;
            PatentMarkerApp.RawLog("Panel command request accepted: command="
                + normalized + ", doc=" + doc.Name);
            return TryDispatchPending(doc);
        }

        /// <summary>由 PATMARK 命令按当前图纸原子地取走一个待标注请求。</summary>
        public static bool TryConsumePending(Document doc, out string number, out string name)
        {
            number = null;
            name = null;
            if (doc == null) return false;

            MarkDispatchState state;
            if (!_markDispatchStates.TryGetValue(doc, out state) || !state.HasPending)
                return false;

            number = state.Number;
            name = state.Name;
            state.Number = null;
            state.Name = null;
            state.HasPending = false;
            if (string.Equals(state.PendingPanelCommand, "PATMARK",
                StringComparison.OrdinalIgnoreCase))
                state.PendingPanelCommand = null;
            state.LaunchQueued = false;
            state.QueuedPanelCommand = null;
            state.LaunchQueuedAtUtc = DateTime.MinValue;

            // 不让空编号把命令实例锁死；畸形字典也只能影响本次请求。
            if (IsNullOrWhiteSpace(number))
            {
                number = null;
                name = null;
                return false;
            }
            return true;
        }

        /// <summary>命令真正进入 AutoCAD 后调用，确认此前的异步排队已经被消费。</summary>
        public static void NotifyPatMarkStarted(Document doc)
        {
            if (doc == null) return;
            MarkDispatchState state;
            if (_markDispatchStates.TryGetValue(doc, out state))
            {
                if (state.LaunchQueued && string.Equals(
                    NormalizePanelCommand(state.QueuedPanelCommand), "PATMARK",
                    StringComparison.OrdinalIgnoreCase))
                {
                    state.ActivePanelCommand = "PATMARK";
                    state.CancelRequested = false;
                }
                state.LaunchQueued = false;
                state.QueuedPanelCommand = null;
                state.LaunchQueuedAtUtc = DateTime.MinValue;
            }
            PatentMarkerApp.RawLog("PATMARK START: doc=" + doc.Name);
        }

        /// <summary>命令结束、取消或失败时调用，确保下一次面板点击不会被旧状态拦截。</summary>
        public static void NotifyPatMarkFinished(Document doc)
        {
            if (doc == null) return;
            MarkDispatchState state;
            if (_markDispatchStates.TryGetValue(doc, out state))
            {
                state.LaunchQueued = false;
                state.QueuedPanelCommand = null;
                state.LaunchQueuedAtUtc = DateTime.MinValue;
                if (string.Equals(state.ActivePanelCommand, "PATMARK",
                    StringComparison.OrdinalIgnoreCase))
                {
                    state.ActivePanelCommand = null;
                    state.CancelRequested = false;
                }
                if (!state.HasPending)
                    state.Number = state.Name = null;
                RemoveDispatchStateIfIdle(doc, state);
            }
            PatentMarkerApp.RawLog("PATMARK END: doc=" + doc.Name);
        }

        /// <summary>
        /// 面板计时器调用的轻量重试。若点击时另一个命令正在运行，请在它结束后再发起 PATMARK。
        /// </summary>
        public static void TryDispatchPendingForCurrentDocument()
        {
            Document doc = IO.RuntimeHost.ActiveDocument;
            if (doc != null)
                TryDispatchPending(doc);
        }

        private static bool TryDispatchPending(Document doc)
        {
            MarkDispatchState state;
            if (doc == null || !_markDispatchStates.TryGetValue(doc, out state))
                return true;

            if (IsNullOrWhiteSpace(state.PendingPanelCommand) && state.HasPending)
                state.PendingPanelCommand = "PATMARK";
            if (IsNullOrWhiteSpace(state.PendingPanelCommand))
            {
                RemoveDispatchStateIfIdle(doc, state);
                return true;
            }

            string commandToQueue = "";
            try
            {
                string inProgress = doc.CommandInProgress;
                string normalizedInProgress = NormalizePanelCommand(inProgress);
                string activePanelCommand = NormalizePanelCommand(
                    state.ActivePanelCommand);

                // Preserve the existing PATMARK -> PATMARK behavior: the active
                // command consumes the most recent dictionary row at its next
                // prompt boundary instead of being restarted.
                if (string.Equals(state.PendingPanelCommand, "PATMARK",
                    StringComparison.OrdinalIgnoreCase) &&
                    IsPatMarkCommand(inProgress))
                {
                    state.PendingPanelCommand = null;
                    state.LaunchQueued = false;
                    state.QueuedPanelCommand = null;
                    state.LaunchQueuedAtUtc = DateTime.MinValue;
                    PatentMarkerApp.RawLog("PATMARK request attached to active command: doc=" + doc.Name);
                    return true;
                }

                if (!IsNullOrWhiteSpace(state.ActivePanelCommand))
                {
                    bool activePanelPrompt =
                        string.Equals(normalizedInProgress, activePanelCommand,
                            StringComparison.OrdinalIgnoreCase) ||
                        (IsNullOrWhiteSpace(inProgress) && !doc.Editor.IsQuiescent);
                    if (activePanelPrompt)
                    {
                        if (!state.CancelRequested)
                        {
                            // This state is set only for a command launched by
                            // this panel, so the cancel input cannot interrupt
                            // an unrelated AutoCAD or user command.
                            doc.SendStringToExecute("\x03\x03", true, false, false);
                            state.CancelRequested = true;
                            PatentMarkerApp.RawLog("Panel command cancel queued: active="
                                + activePanelCommand + ", next="
                                + state.PendingPanelCommand + ", doc=" + doc.Name);
                        }
                        return true;
                    }

                    if (!IsNullOrWhiteSpace(inProgress) || !doc.Editor.IsQuiescent)
                    {
                        PatentMarkerApp.RawLog("Panel command switch deferred; active panel command="
                            + state.ActivePanelCommand + ", CAD command=" + inProgress
                            + ", doc=" + doc.Name);
                        return true;
                    }

                    // Fallback if this host did not deliver a terminal command event.
                    state.ActivePanelCommand = null;
                    state.CancelRequested = false;
                }

                if (!IsNullOrWhiteSpace(inProgress))
                {
                    PatentMarkerApp.RawLog("Panel request deferred; command in progress="
                        + inProgress + ", doc=" + doc.Name);
                    return true;
                }
                if (!doc.Editor.IsQuiescent)
                {
                    PatentMarkerApp.RawLog("Panel request deferred; editor is not quiescent: doc="
                        + doc.Name);
                    return true;
                }
                if (state.LaunchQueued)
                {
                    if (!IsLaunchQueueStale(state)) return true;
                    state.LaunchQueued = false;
                    state.QueuedPanelCommand = null;
                    state.LaunchQueuedAtUtc = DateTime.MinValue;
                    PatentMarkerApp.RawLog("Panel queued request considered stale; retrying: doc=" + doc.Name);
                }

                string command = state.PendingPanelCommand;
                state.PendingPanelCommand = null;
                commandToQueue = command;
                state.LaunchQueued = true;
                state.QueuedPanelCommand = command;
                state.LaunchQueuedAtUtc = DateTime.UtcNow;
                // activate=true：确保 MDI 切换后请求仍投递给点击时对应的图纸。
                doc.SendStringToExecute(command + " ", true, false, false);
                PatentMarkerApp.RawLog("Panel command queued: command=" + command
                    + ", doc=" + doc.Name);
                return true;
            }
            catch (System.Exception ex)
            {
                state.LaunchQueued = false;
                state.QueuedPanelCommand = null;
                state.LaunchQueuedAtUtc = DateTime.MinValue;
                if (!IsNullOrWhiteSpace(commandToQueue) &&
                    IsNullOrWhiteSpace(state.PendingPanelCommand))
                    state.PendingPanelCommand = commandToQueue;
                PatentMarkerApp.RawLog("Panel command queue failed: " + ex.GetType().FullName
                    + ": " + ex.Message);
                return false;
            }
        }

        private static void AttachCommandEvents(Document doc, MarkDispatchState state)
        {
            if (state.CommandEventsAttached) return;
            doc.CommandWillStart += Document_CommandWillStart;
            doc.CommandEnded += Document_CommandEnded;
            doc.CommandCancelled += Document_CommandCancelled;
            doc.CommandFailed += Document_CommandFailed;
            state.CommandEventsAttached = true;
        }

        private static void DetachCommandEvents(Document doc, MarkDispatchState state)
        {
            if (!state.CommandEventsAttached) return;
            doc.CommandWillStart -= Document_CommandWillStart;
            doc.CommandEnded -= Document_CommandEnded;
            doc.CommandCancelled -= Document_CommandCancelled;
            doc.CommandFailed -= Document_CommandFailed;
            state.CommandEventsAttached = false;
        }

        private static void Document_CommandWillStart(object sender, CommandEventArgs e)
        {
            Document doc = sender as Document;
            if (doc == null || e == null) return;
            MarkDispatchState state;
            if (!_markDispatchStates.TryGetValue(doc, out state)) return;

            string command = NormalizePanelCommand(e.GlobalCommandName);
            string queued = NormalizePanelCommand(state.QueuedPanelCommand);
            if (state.LaunchQueued &&
                string.Equals(command, queued, StringComparison.OrdinalIgnoreCase))
            {
                state.LaunchQueued = false;
                state.QueuedPanelCommand = null;
                state.LaunchQueuedAtUtc = DateTime.MinValue;
                state.ActivePanelCommand = command;
                state.CancelRequested = false;
                PatentMarkerApp.RawLog("Panel command started: command=" + command
                    + ", doc=" + doc.Name);
            }
        }

        private static void Document_CommandEnded(object sender, CommandEventArgs e)
        {
            FinishTrackedPanelCommand(sender, e, "ended");
        }

        private static void Document_CommandCancelled(object sender, CommandEventArgs e)
        {
            FinishTrackedPanelCommand(sender, e, "cancelled");
        }

        private static void Document_CommandFailed(object sender, CommandEventArgs e)
        {
            FinishTrackedPanelCommand(sender, e, "failed");
        }

        private static void FinishTrackedPanelCommand(object sender,
            CommandEventArgs e, string outcome)
        {
            Document doc = sender as Document;
            if (doc == null || e == null) return;
            MarkDispatchState state;
            if (!_markDispatchStates.TryGetValue(doc, out state)) return;

            string command = NormalizePanelCommand(e.GlobalCommandName);
            if (!string.Equals(state.ActivePanelCommand, command,
                StringComparison.OrdinalIgnoreCase)) return;
            state.ActivePanelCommand = null;
            state.CancelRequested = false;
            PatentMarkerApp.RawLog("Panel command " + outcome + ": command="
                + command + ", doc=" + doc.Name);
        }

        private static bool IsDrawingPanelCommand(string command)
        {
            return string.Equals(command, "PATBRACE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, "PATCHECK", StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, "PATALIGN", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePanelCommand(string command)
        {
            if (IsNullOrWhiteSpace(command)) return "";
            string normalized = command.Trim();
            while (normalized.Length > 0 &&
                (normalized[0] == '*' || normalized[0] == '.' || normalized[0] == '_'))
                normalized = normalized.Substring(1);
            normalized = normalized.ToUpperInvariant();
            if (normalized == "BZM") return "PATMARK";
            if (normalized == "DAGUOHAO") return "PATBRACE";
            return normalized;
        }

        private static void RemoveDispatchStateIfIdle(Document doc,
            MarkDispatchState state)
        {
            if (state.HasPending || !IsNullOrWhiteSpace(state.PendingPanelCommand) ||
                !IsNullOrWhiteSpace(state.QueuedPanelCommand) ||
                !IsNullOrWhiteSpace(state.ActivePanelCommand) || state.LaunchQueued)
                return;
            DetachCommandEvents(doc, state);
            _markDispatchStates.Remove(doc);
        }

        private static MarkDispatchState GetMarkDispatchState(Document doc, bool create)
        {
            MarkDispatchState state;
            if (_markDispatchStates.TryGetValue(doc, out state)) return state;
            if (!create) return null;
            state = new MarkDispatchState();
            _markDispatchStates.Add(doc, state);
            return state;
        }

        private static void ClearMarkDispatchState(Document doc)
        {
            if (doc == null) return;
            MarkDispatchState state;
            if (!_markDispatchStates.TryGetValue(doc, out state)) return;
            DetachCommandEvents(doc, state);
            _markDispatchStates.Remove(doc);
        }

        private static bool IsPatMarkCommand(string command)
        {
            if (IsNullOrWhiteSpace(command)) return false;
            string normalized = NormalizePanelCommand(command);
            return string.Equals(normalized, "PATMARK", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "BZM", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLaunchQueueStale(MarkDispatchState state)
        {
            if (state.LaunchQueuedAtUtc == DateTime.MinValue) return true;
            return DateTime.UtcNow - state.LaunchQueuedAtUtc >= TimeSpan.FromSeconds(5);
        }

        private static bool IsNullOrWhiteSpace(string value)
        {
            return value == null || value.Trim().Length == 0;
        }

        public static void LoadDictForCurrentDrawing()
        {
            LoadDictForCurrentDrawing(true);
        }

        public static void LoadDictForCurrentDrawing(bool clearCheckResult)
        {
            if (_control == null) return;

            try
            {
                bool dictChanged = DictLoader.IsFileChanged();
                var dict = DictLoader.LoadForCurrentDrawing();
                if (dict != null)
                    _control.LoadDict(dict, clearCheckResult || dictChanged);
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
            foreach (KeyValuePair<Document, MarkDispatchState> entry in _markDispatchStates)
                DetachCommandEvents(entry.Key, entry.Value);
            _markDispatchStates.Clear();
            Commands.PatCheckResult.ClearAll();
            _paletteSet = null;
        }
    }
}
