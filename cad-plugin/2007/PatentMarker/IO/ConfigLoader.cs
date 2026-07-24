using System;
using System.Collections.Generic;
using System.IO;

namespace PatentMarker.IO
{
    /// <summary>
    /// 配置模型（从 config.json 读取）。2007 版用 SimpleJson 替代 Newtonsoft.Json。
    /// </summary>
    public class PatConfig
    {
        public string DefaultDictPath = "";
        public PatStyleConfig PatStyle;
        public AlignConfig Align;
    }

    public class PatStyleConfig
    {
        public double TextHeight = 3.5;
    }

    public class AlignConfig
    {
        public double MarginToFrame = 5.0;
    }

    /// <summary>
    /// 加载 config.json。查找顺序：
    ///  1. DWG 目录下的 config.local.json（用户覆盖）
    ///  2. DWG 目录下的 config.json
    ///  3. DLL 目录下的 config.json
    ///  4. 内置默认值
    /// </summary>
    public static class ConfigLoader
    {
        public static PatConfig Current;

        public static PatConfig Load(string path)
        {
            string filePath = path;

            if (filePath == null)
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null && doc.Name != null && doc.Name.Length > 0)
                {
                    string dwgDir = Path.GetDirectoryName(doc.Name);
                    if (dwgDir != null)
                    {
                        string localCfg = Path.Combine(dwgDir, "config.local.json");
                        if (File.Exists(localCfg))
                            filePath = localCfg;
                        else
                        {
                            string dwgCfg = Path.Combine(dwgDir, "config.json");
                            if (File.Exists(dwgCfg))
                                filePath = dwgCfg;
                        }
                    }
                }

                if (filePath == null)
                {
                    // 修复 D5：用 Assembly.Location 而非 AppDomain.BaseDirectory
                    string dllDir = null;
                    try
                    {
                        dllDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    }
                    catch { }
                    if (dllDir != null)
                    {
                        string dllCfg = Path.Combine(dllDir, "config.json");
                        if (File.Exists(dllCfg))
                            filePath = dllCfg;
                    }
                }
            }

            if (filePath != null && File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    Dictionary<string, object> root = SimpleJson.ParseObject(json);

                    PatConfig config = new PatConfig();
                    config.DefaultDictPath = SimpleJson.GetStr(root, "defaultDictPath");

                    Dictionary<string, object> ps = SimpleJson.GetObj(root, "patStyle");
                    config.PatStyle = new PatStyleConfig();
                    if (ps != null)
                        config.PatStyle.TextHeight = SimpleJson.GetDouble(ps, "textHeight", 3.5);

                    Dictionary<string, object> al = SimpleJson.GetObj(root, "align");
                    config.Align = new AlignConfig();
                    if (al != null)
                        config.Align.MarginToFrame = SimpleJson.GetDouble(al, "marginToFrame", 5.0);

                    return config;
                }
                catch (Exception ex)
                {
                    var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                    if (doc != null)
                        doc.Editor.WriteMessage("\nPatentMarker: config load error: " + ex.Message + "\n");
                }
            }

            return new PatConfig();
        }
    }
}
