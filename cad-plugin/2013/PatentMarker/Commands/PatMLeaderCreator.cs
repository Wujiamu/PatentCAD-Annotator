// ============================================================================
// 复线（mleader 分支）版本本地文件 — 仅 MLeader 复线版本编译，不进入 Shared 单源层
//（2010/2013/2015/2025；2007 无 MLeader API，不适用）。
// 来源：tools/MLeaderRepro/MLeaderFormProbe.cs 配置 F（三点顶点链）生产化。
// 差异基线：Shared/Commands/PatMarkCommand.cs 的 Leader+MText 创建路径。
// 方案文档：docs/mleader-f-plan.md
// ============================================================================
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;

namespace PatentMarker.Commands
{
    /// <summary>
    /// Plan-F MLeader creation. All automatic geometry is disabled and the
    /// user-picked text point is appended as the LAST leader vertex, so the
    /// drawn path matches the picked points instead of MLeader's own landing
    /// computation (which produced the historical "fishhook" distortion).
    /// </summary>
    public static class PatMLeaderCreator
    {
        public const string StyleName = "PAT_MLEADER";
        public const string MarkerKey = "PATENTMARKER_MLEADER";
        public const string MarkerValue = "PATENTMARKER_MLEADER_V1";
        private const string NoArrowBlockName = "_PAT_NO_ARROW";
        private const string TextStyleName = "PatentTimesNewRoman";

        /// <summary>Creates one MLeader callout carrying its own MText.</summary>
        public static ObjectId Create(Database db, Transaction tr,
            BlockTableRecord modelSpace, Point3d attachPt,
            List<Point3d> doglegPts, Point3d textPt, string number)
        {
            IO.PatRuntimeSettings settings = IO.PatSettingsStore.Current;
            ObjectId styleId = EnsureStyle(db, tr);

            MLeader ml = new MLeader();
            ml.SetDatabaseDefaults(db);
            ml.ContentType = ContentType.MTextContent;
            ml.MLeaderStyle = styleId;

            // ---- 实体级全禁用（与样式级双保险，探针配置 F 已验证）----
            ml.EnableDogleg = false;
            ml.EnableLanding = false;
            SetExtendLeaderToText(ml, false);
            ml.DoglegLength = 0.0;
            ml.LandingGap = 0.0;
            ml.TextAttachmentDirection = TextAttachmentDirection.AttachmentHorizontal;
            ml.TextAttachmentType = TextAttachmentType.AttachmentMiddle;
            ml.TextAngleType = TextAngleType.HorizontalAngle;
            ml.LeaderLineType = settings.IsSplined
                ? LeaderType.SplineLeader : LeaderType.StraightLeader;
            // 无箭头时 ArrowSize 必须为 0：非零 ArrowSize 即使配空箭头块，
            // 也会把引线起点修剪掉 ArrowSize（实测 2.5），导致引线不触及零件。
            ml.ArrowSize = settings.HasArrowHead ? settings.ArrowSize : 0.0;
            ml.ArrowSymbolId = settings.HasArrowHead
                ? ObjectId.Null
                : EnsureNoArrowBlock(db, tr);

            // ---- F 方案顶点链：attach → dogleg… → text（文字点必须进链）----
            int line = ml.AddLeaderLine(attachPt);
            foreach (Point3d dogleg in doglegPts)
                ml.AddLastVertex(line, dogleg);
            if (doglegPts.Count == 0 ||
                !SamePoint(doglegPts[doglegPts.Count - 1], textPt))
                ml.AddLastVertex(line, textPt);

            // ---- 文字挂接（顺序与探针一致：先顶点后文字）----
            MText mt = new MText();
            mt.SetDatabaseDefaults(db);
            mt.TextStyleId = GetOrCreateTextStyleId(db, tr);
            mt.Contents = IO.PatEntityHelper.FormatText(
                number, settings.UnderlineText);
            mt.TextHeight = settings.TextHeight;
            mt.Rotation = 0.0;
            mt.Location = textPt;
            ml.MText = mt;
            ml.TextLocation = textPt;
            ml.TextHeight = settings.TextHeight;

            ObjectId id = modelSpace.AppendEntity(ml);
            tr.AddNewlyCreatedDBObject(ml, true);
            Mark(ml, tr, attachPt, doglegPts, textPt,
                settings.HasArrowHead, settings.IsSplined);
            return id;
        }

        /// <summary>Creates or refreshes the all-off PAT_MLEADER style.</summary>
        public static ObjectId EnsureStyle(Database db, Transaction tr)
        {
            DBDictionary dict = (DBDictionary)tr.GetObject(
                db.MLeaderStyleDictionaryId, OpenMode.ForRead);
            MLeaderStyle style;
            if (dict.Contains(StyleName))
            {
                style = (MLeaderStyle)tr.GetObject(dict.GetAt(StyleName), OpenMode.ForWrite);
            }
            else
            {
                dict.UpgradeOpen();
                style = new MLeaderStyle();
                dict.SetAt(StyleName, style);
                tr.AddNewlyCreatedDBObject(style, true);
                style.Name = StyleName;
            }

            style.ContentType = ContentType.MTextContent;
            style.TextHeight = IO.PatSettingsStore.Current.TextHeight;
            style.TextAttachmentType = TextAttachmentType.AttachmentMiddle;
            style.TextAttachmentDirection = TextAttachmentDirection.AttachmentHorizontal;
            style.TextAngleType = TextAngleType.HorizontalAngle;
            style.LeaderLineType = LeaderType.StraightLeader;
            style.EnableDogleg = false;
            style.EnableLanding = false;
            SetExtendLeaderToText(style, false);
            style.DoglegLength = 0.0;
            style.LandingGap = 0.0;
            style.ArrowSize = IO.PatSettingsStore.Current.ArrowSize;
            style.ArrowSymbolId = ObjectId.Null;
            style.TextStyleId = GetOrCreateTextStyleId(db, tr);
            return style.ObjectId;
        }

        /// <summary>An empty arrow block reliably renders "no arrowhead"
        /// (ObjectId.Null inherits the closed-filled default).</summary>
        private static ObjectId EnsureNoArrowBlock(Database db, Transaction tr)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(NoArrowBlockName))
                return bt[NoArrowBlockName];

            bt.UpgradeOpen();
            BlockTableRecord block = new BlockTableRecord();
            block.Name = NoArrowBlockName;
            ObjectId id = bt.Add(block);
            tr.AddNewlyCreatedDBObject(block, true);
            return id;
        }

        public static ObjectId GetOrCreateTextStyleId(Database db, Transaction tr)
        {
            TextStyleTable table = (TextStyleTable)tr.GetObject(
                db.TextStyleTableId, OpenMode.ForRead);
            if (table.Has(TextStyleName))
                return table[TextStyleName];

            table.UpgradeOpen();
            TextStyleTableRecord record = new TextStyleTableRecord();
            record.Name = TextStyleName;
            record.FileName = "times.ttf";
            ObjectId styleId = table.Add(record);
            tr.AddNewlyCreatedDBObject(record, true);
            return styleId;
        }

        /// <summary>Marks the MLeader and records the user-picked chain so
        /// PATSELECTALL can recognize it and PATMLVERIFY can replay it.</summary>
        public static void Mark(MLeader ml, Transaction tr, Point3d attachPt,
            List<Point3d> doglegPts, Point3d textPt, bool hasArrow, bool isSplined)
        {
            if (ml.ExtensionDictionary.IsNull)
                ml.CreateExtensionDictionary();

            DBDictionary dictionary = (DBDictionary)tr.GetObject(
                ml.ExtensionDictionary, OpenMode.ForWrite);

            ResultBuffer data = new ResultBuffer();
            data.Add(new TypedValue(1, MarkerValue));
            data.Add(new TypedValue(70, hasArrow ? 1 : 0));
            data.Add(new TypedValue(71, isSplined ? 1 : 0));
            data.Add(new TypedValue(10, attachPt));
            foreach (Point3d dogleg in doglegPts)
                data.Add(new TypedValue(10, dogleg));
            data.Add(new TypedValue(10, textPt));

            if (dictionary.Contains(MarkerKey))
            {
                Xrecord existing = (Xrecord)tr.GetObject(
                    dictionary.GetAt(MarkerKey), OpenMode.ForWrite);
                existing.Data = data;
                return;
            }
            Xrecord record = new Xrecord();
            record.Data = data;
            dictionary.SetAt(MarkerKey, record);
            tr.AddNewlyCreatedDBObject(record, true);
        }

        public static bool IsPatMLeader(MLeader ml, Transaction tr)
        {
            if (ml == null || ml.ExtensionDictionary.IsNull) return false;
            try
            {
                DBDictionary dictionary = (DBDictionary)tr.GetObject(
                    ml.ExtensionDictionary, OpenMode.ForRead);
                if (!dictionary.Contains(MarkerKey)) return false;
                Xrecord record = (Xrecord)tr.GetObject(
                    dictionary.GetAt(MarkerKey), OpenMode.ForRead);
                using (ResultBuffer data = record.Data)
                {
                    if (data == null) return false;
                    foreach (TypedValue value in data)
                    {
                        if (value.TypeCode == 1 &&
                            value.Value is string &&
                            (string)value.Value == MarkerValue)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>Reads the recorded chain: attach, doglegs…, text (last).</summary>
        public static List<Point3d> ReadChain(MLeader ml, Transaction tr,
            out bool hasArrow, out bool isSplined)
        {
            hasArrow = false;
            isSplined = false;
            List<Point3d> points = new List<Point3d>();
            if (ml == null || ml.ExtensionDictionary.IsNull) return points;
            try
            {
                DBDictionary dictionary = (DBDictionary)tr.GetObject(
                    ml.ExtensionDictionary, OpenMode.ForRead);
                if (!dictionary.Contains(MarkerKey)) return points;
                Xrecord record = (Xrecord)tr.GetObject(
                    dictionary.GetAt(MarkerKey), OpenMode.ForRead);
                using (ResultBuffer data = record.Data)
                {
                    if (data == null) return points;
                    foreach (TypedValue value in data)
                    {
                        if (value.TypeCode == 70) hasArrow = Convert.ToInt32(value.Value) == 1;
                        else if (value.TypeCode == 71) isSplined = Convert.ToInt32(value.Value) == 1;
                        else if (value.TypeCode == 10) points.Add((Point3d)value.Value);
                    }
                }
            }
            catch { }
            return points;
        }

        /// <summary>PATALIGN 移动文字后同步记录链的末点（否则 PATMLVERIFY
        /// 的 C3 检查会失败）。链不足 3 点（异常数据）时静默跳过。</summary>
        public static void UpdateChainTextPoint(MLeader ml, Transaction tr,
            Point3d newTextPt)
        {
            bool hasArrow, isSplined;
            List<Point3d> chain = ReadChain(ml, tr, out hasArrow, out isSplined);
            if (chain.Count < 3) return;
            Point3d attach = chain[0];
            List<Point3d> doglegs = chain.GetRange(1, chain.Count - 2);
            Mark(ml, tr, attach, doglegs, newTextPt, hasArrow, isSplined);
        }

        /// <summary>ExtendLeaderToText 为 2014+ SDK 属性（2010-2012 无此成员），
        /// 反射设置以维持单源码覆盖全版本；不支持时静默跳过（其默认行为即不延伸）。</summary>
        public static void SetExtendLeaderToText(object target, bool value)
        {
            try
            {
                System.Reflection.PropertyInfo property =
                    target.GetType().GetProperty("ExtendLeaderToText");
                if (property != null && property.CanWrite)
                    property.SetValue(target, value, null);
            }
            catch { }
        }

        /// <summary>读取 ExtendLeaderToText；SDK 不支持时返回 null（用于诊断日志）。</summary>
        public static object GetExtendLeaderToText(object target)
        {
            try
            {
                System.Reflection.PropertyInfo property =
                    target.GetType().GetProperty("ExtendLeaderToText");
                if (property != null && property.CanRead)
                    return property.GetValue(target, null);
            }
            catch { }
            return null;
        }

        private static bool SamePoint(Point3d left, Point3d right)
        {
            return Math.Abs(left.X - right.X) < 0.000000001 &&
                Math.Abs(left.Y - right.Y) < 0.000000001 &&
                Math.Abs(left.Z - right.Z) < 0.000000001;
        }
    }
}
