using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using PatentMarker.Palette;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

// L2_HOST_INTERNAL: uses the real palette Button.PerformClick and real host
// command events. It deliberately does not claim mouse/keyboard UI coverage.
public sealed class PanelSwitchProbe
{
    private sealed class Case
    {
        public string From;
        public string To;
        public bool Rapid;
        public bool External;
    }

    private static readonly Case[] Cases = new Case[] {
        new Case { From = "PATMARK", To = "PATBRACE" },
        new Case { From = "PATBRACE", To = "PATMARK" },
        new Case { From = "PATBRACE", To = "PATALIGN" },
        new Case { From = "PATALIGN", To = "PATBRACE" },
        new Case { From = "PATMARK", To = "PATCHECK" },
        new Case { From = "PATBRACE", To = "PATCHECK" },
        new Case { From = "PATALIGN", To = "PATCHECK" },
        new Case { From = "PATMARK", To = "PATALIGN", Rapid = true },
        new Case { From = "LINE", To = "PATBRACE", External = true }
    };

    private static Document _doc;
    private static Timer _timer;
    private static string _report;
    private static int _case;
    private static int _stage;
    private static string _active = "";
    private static long _at;
    private static readonly Stopwatch Clock = new Stopwatch();
    private static readonly List<string> Started = new List<string>();
    private static int _passed;
    private static int _failed;
    private static int _checkVersion;

    [CommandMethod("PATPANELVERIFY", CommandFlags.Session)]
    public void Run()
    {
        if (_timer != null) throw new InvalidOperationException("Probe already running.");
        _doc = AcadApp.DocumentManager.MdiActiveDocument;
        PromptStringOptions options = new PromptStringOptions("\nProbe report file: ");
        options.AllowSpaces = true;
        PromptResult result = _doc.Editor.GetString(options);
        if (result.Status != PromptStatus.OK) return;
        _report = Path.GetFullPath(result.StringResult);
        Directory.CreateDirectory(Path.GetDirectoryName(_report));
        File.WriteAllText(_report, "L2_HOST_INTERNAL\r\nAssembly="
            + typeof(PatPaletteCommand).Assembly.Location + "\r\nHost="
            + AcadApp.Version.ToString() + "\r\nDrawing=" + _doc.Name + "\r\n");
        _case = _stage = _passed = _failed = 0;
        Started.Clear();
        Clock.Restart();
        _at = 0;
        _doc.CommandWillStart += StartedCommand;
        _doc.CommandEnded += FinishedCommand;
        _doc.CommandCancelled += FinishedCommand;
        _doc.CommandFailed += FailedCommand;
        _timer = new Timer();
        _timer.Interval = 200;
        _timer.Tick += Tick;
        _timer.Start();
        Log("BEGIN: callbacks only after this command returns");
    }

    private static void Log(string text)
    {
        File.AppendAllText(_report, Clock.ElapsedMilliseconds + "|" + text + "\r\n");
    }

    private static void StartedCommand(object sender, CommandEventArgs e)
    {
        _active = e.GlobalCommandName.ToUpperInvariant();
        Started.Add(_active);
        Log("START " + _active);
    }

    private static void FinishedCommand(object sender, CommandEventArgs e)
    {
        Log("END " + e.GlobalCommandName);
        if (string.Equals(_active, e.GlobalCommandName, StringComparison.OrdinalIgnoreCase))
            _active = "";
    }

    private static void FailedCommand(object sender, CommandEventArgs e)
    {
        Log("HOST_FAIL " + e.GlobalCommandName);
        FinishedCommand(sender, e);
        if (_timer != null) Finish(false, "Host command failed: " + e.GlobalCommandName);
    }

    private static void Request(string command)
    {
        Log("REQUEST " + command);
        if (command == "PATCHECK")
            _checkVersion = PatentMarker.Commands.PatCheckResult.GetVersion(_doc);
        if (command == "PATMARK")
        {
            PatPaletteCommand.RequestMark(_doc, "901", "Switch Test");
            return;
        }
        if (command == "LINE")
        {
            _doc.SendStringToExecute("_.LINE ", true, false, false);
            return;
        }
        object control = typeof(PatPaletteCommand).GetField("_control",
            BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        string field = command == "PATBRACE" ? "_btnBrace"
            : command == "PATALIGN" ? "_btnAlign" : "_btnCheck";
        Button button = (Button)control.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(control);
        if (!button.Enabled) throw new InvalidOperationException(field + " is disabled");
        button.PerformClick();
    }

    private static void Step(int stage)
    {
        _stage = stage;
        _at = Clock.ElapsedMilliseconds;
    }

    private static void Tick(object sender, EventArgs e)
    {
        try
        {
            long elapsed = Clock.ElapsedMilliseconds - _at;
            if (_stage == 0)
            {
                if (_active.Length != 0 || !_doc.Editor.IsQuiescent)
                {
                    if (elapsed > 10000) Finish(false, "Startup command did not become idle: " + _active);
                    return;
                }
                _doc.SendStringToExecute("PATPALETTE ", true, false, false);
                Step(1);
                return;
            }
            if (_stage == 1)
            {
                if (!Started.Contains("PATPALETTE") || _active.Length != 0 ||
                    !_doc.Editor.IsQuiescent)
                {
                    if (elapsed > 10000) Finish(false, "Palette did not initialize");
                    return;
                }
                Step(2);
            }
            if (_case >= Cases.Length) { Finish(true, "All scenarios executed"); return; }
            Case current = Cases[_case];
            if (_stage == 2)
            {
                if (_active.Length != 0 || !_doc.Editor.IsQuiescent)
                {
                    if (elapsed > 10000) Finish(false, "Between-case editor did not become idle: " + _active);
                    return;
                }
                Started.Clear();
                Request(current.From);
                Step(3);
                return;
            }
            if (_stage == 3)
            {
                if (_active != current.From || _doc.Editor.IsQuiescent)
                {
                    if (elapsed > 10000) Finish(false, "Source prompt did not start: " + current.From);
                    return;
                }
                if (elapsed < 800) return;
                Started.Clear();
                if (current.Rapid) Request("PATBRACE");
                Request(current.To);
                Step(current.External ? 4 : 5);
                return;
            }
            if (_stage == 4)
            {
                if (elapsed < 1200) return;
                if (_active != "LINE" || Started.Contains(current.To))
                { Finish(false, "Panel request cancelled an unrelated native command"); return; }
                Log("ASSERT native LINE remains active");
                _doc.SendStringToExecute("\x03\x03", true, false, false);
                Step(5);
                return;
            }
            if (_stage == 5)
            {
                if (!Started.Contains(current.To))
                {
                    if (elapsed > 10000)
                    { Finish(false, "Switch timeout: " + current.From + " -> " + current.To + "; active=" + _active); }
                    return;
                }
                if (current.Rapid && Started.Contains("PATBRACE"))
                { Finish(false, "Rapid request launched superseded PATBRACE"); return; }
                if (current.To == "PATCHECK")
                {
                    if (_active.Length != 0) return;
                    if (PatentMarker.Commands.PatCheckResult.GetVersion(_doc) == _checkVersion ||
                        !PatentMarker.Commands.PatCheckResult.IsUnmarked(_doc, "901"))
                    { Finish(false, "PATCHECK did not produce the expected unmarked 901 result"); return; }
                }
                else if (_active != current.To || _doc.Editor.IsQuiescent)
                { Finish(false, "Target command did not retain its interactive prompt: " + current.To); return; }
                _passed++;
                Log("PASS " + current.From + " -> " + current.To
                    + (current.Rapid ? " latest-request-wins" : ""));
                if (_active.Length != 0)
                    _doc.SendStringToExecute("\x03\x03", true, false, false);
                Step(6);
                return;
            }
            if (_stage == 6)
            {
                if (_active.Length != 0 || !_doc.Editor.IsQuiescent)
                {
                    if (elapsed > 10000) Finish(false, "Cleanup cancellation timed out: " + _active);
                    return;
                }
                _case++;
                Step(2);
            }
        }
        catch (System.Exception ex) { Finish(false, ex.ToString()); }
    }

    private static void Finish(bool completed, string reason)
    {
        if (!completed) _failed++;
        Log((completed ? "COMPLETE " : "FAIL ") + reason);
        File.AppendAllText(_report, "SUMMARY PASS=" + _passed + " FAIL=" + _failed
            + " SKIP=" + Math.Max(0, Cases.Length - _passed - _failed) + "\r\n");
        _timer.Stop();
        _timer.Dispose();
        _timer = null;
        _doc.CommandWillStart -= StartedCommand;
        _doc.CommandEnded -= FinishedCommand;
        _doc.CommandCancelled -= FinishedCommand;
        _doc.CommandFailed -= FailedCommand;
        if (_active.Length != 0)
            _doc.SendStringToExecute("\x03\x03", true, false, false);
        // The launch script/process owner handles shutdown. Never quit an
        // arbitrary user's AutoCAD session from a diagnostic command.
    }
}
