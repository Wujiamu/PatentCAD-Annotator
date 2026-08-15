// ============================================================================
// 复线（mleader 分支）版本本地文件 — 仅 MLeader 复线版本编译
//（2010/2013/2015/2025；2007 无 MLeader API，不适用）。
// PATMLSET：QA/自动化钩子——在 Core Console 等无面板环境下切换运行时标注
// 设置（面板按钮的脚本等价物）。非用户日常命令。
// ============================================================================
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Globalization;

namespace PatentMarker.Commands
{
    public class PatMLeaderSetCommand
    {
        [CommandMethod("PATMLSET", CommandFlags.Modal)]
        public void Run()
        {
            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var s = IO.PatSettingsStore.Current;

            var kw = new PromptKeywordOptions("Setting to change");
            kw.Keywords.Add("Spline");
            kw.Keywords.Add("Arrow");
            kw.Keywords.Add("ThreePoint");
            kw.Keywords.Add("Underline");
            kw.Keywords.Add("Height");
            kw.AllowNone = false;
            var res = ed.GetKeywords(kw);
            if (res.Status != PromptStatus.OK) return;

            switch (res.StringResult)
            {
                case "Spline":
                    s.IsSplined = PromptOnOff(ed, "Spline");
                    break;
                case "Arrow":
                    s.HasArrowHead = PromptOnOff(ed, "Arrow");
                    break;
                case "ThreePoint":
                    s.ThreePointMode = PromptOnOff(ed, "ThreePoint");
                    break;
                case "Underline":
                    s.UnderlineText = PromptOnOff(ed, "Underline");
                    break;
                case "Height":
                    var hr = ed.GetDouble("\nText height: ");
                    if (hr.Status == PromptStatus.OK && hr.Value > 0)
                        s.TextHeight = hr.Value;
                    break;
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "PATMLSET: spline={0} arrow={1} threePoint={2} underline={3} height={4}\n",
                s.IsSplined, s.HasArrowHead, s.ThreePointMode,
                s.UnderlineText, s.TextHeight.ToString("F1", CultureInfo.InvariantCulture)));
        }

        private static bool PromptOnOff(Editor ed, string label)
        {
            var kw = new PromptKeywordOptions(label + " mode");
            kw.Keywords.Add("On");
            kw.Keywords.Add("Off");
            kw.AllowNone = false;
            var res = ed.GetKeywords(kw);
            if (res.Status != PromptStatus.OK) return false;
            return res.StringResult == "On";
        }
    }
}
