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
    /// user-picked bends are preserved. The terminal is derived by the host
    /// from the facing MText edge with a text-height-proportional outside gap.
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
            // Enter in unlimited mode uses the last pick as the text anchor.
            // That coincident point is content, not another bend to preserve.
            List<Point3d> bends = new List<Point3d>(doglegPts);
            while (bends.Count > 0 && SamePoint(bends[bends.Count - 1], textPt))
                bends.RemoveAt(bends.Count - 1);
            if (bends.Count == 0 && SamePoint(attachPt, textPt))
                throw new ArgumentException("Leader attachment and text must differ.");
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
            ml.LandingGap = settings.TextHeight * PatLeaderTextAttachment.TextGapPerHeight;
            ml.TextAttachmentDirection = TextAttachmentDirection.AttachmentHorizontal;
            ml.TextAttachmentType = TextAttachmentType.AttachmentMiddle;
            // AttachmentMiddle on the generic property does not initialize
            // both directional values in AutoCAD. In particular, the unset
            // RightLeader value defaults to AttachmentBottomOfTopLine and
            // leaves a horizontal tail under left-side text.
            ml.SetTextAttachmentType(TextAttachmentType.AttachmentMiddle,
                LeaderDirectionType.LeftLeader);
            ml.SetTextAttachmentType(TextAttachmentType.AttachmentMiddle,
                LeaderDirectionType.RightLeader);
            ml.TextAngleType = TextAngleType.HorizontalAngle;
            ml.LeaderLineType = settings.IsSplined
                ? LeaderType.SplineLeader : LeaderType.StraightLeader;
            // 无箭头时 ArrowSize 必须为 0：非零 ArrowSize 即使配空箭头块，
            // 也会把引线起点修剪掉 ArrowSize（实测 2.5），导致引线不触及零件。
            ml.ArrowSize = settings.HasArrowHead ? settings.ArrowSize : 0.0;
            ml.ArrowSymbolId = settings.HasArrowHead
                ? ObjectId.Null
                : EnsureNoArrowBlock(db, tr);

            // ---- F chain: attach → picked bends → native text-side terminal ----
            int line = ml.AddLeaderLine(attachPt);
            Point3d lastVertex = attachPt;
            foreach (Point3d dogleg in bends)
            {
                ml.AddLastVertex(line, dogleg);
                lastVertex = dogleg;
            }
            // Seed the terminal; native attachment derives its final location
            // from the actual text box, rather than from this insertion point.
            Point3d endpoint = textPt;
            if (!SamePoint(endpoint, lastVertex))
                ml.AddLastVertex(line, endpoint);

            // Even with dogleg geometry disabled, its direction selects the
            // MText connection side. Leaving the default direction lets the
            // host attach at the far edge and bend a spline across the text.
            // Set it before assigning MText, so creation is deterministic for
            // either side and does not depend on the previous callout.
            ml.SetDogleg(ml.GetLeaderIndex(line), new Vector3d(
                textPt.X >= lastVertex.X ? 1.0 : -1.0, 0.0, 0.0));

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
            RefreshTextAttachment(ml, lastVertex, textPt);
            Mark(ml, tr, attachPt, bends, textPt,
                settings.HasArrowHead, settings.IsSplined);
            return id;
        }

        // Use native boundary attachment rather than overriding the last
        // vertex relative to MText's insertion point (which can be inside the
        // text on its right side). Native LandingGap also survives grip edits.
        public static void RefreshTextAttachment(MLeader ml, Point3d previous,
            Point3d pickedText)
        {
            ml.LandingGap = ml.TextHeight * PatLeaderTextAttachment.TextGapPerHeight;
            ml.TextLocation = pickedText;
            Extents3d bounds = ml.MText.GeometricExtents;
            double center = bounds.MinPoint.X +
                (bounds.MaxPoint.X - bounds.MinPoint.X) / 2.0;
            ml.SetDogleg(ml.GetLeaderIndex(0), new Vector3d(
                previous.X <= center ? 1.0 : -1.0, 0.0, 0.0));
            // Changing a gap/direction can shift native content. Restore the
            // user pick LAST, then let the host derive the terminal from bounds.
            ml.TextLocation = pickedText;
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
            style.SetTextAttachmentType(TextAttachmentType.AttachmentMiddle,
                LeaderDirectionType.LeftLeader);
            style.SetTextAttachmentType(TextAttachmentType.AttachmentMiddle,
                LeaderDirectionType.RightLeader);
            style.TextAttachmentDirection = TextAttachmentDirection.AttachmentHorizontal;
            style.TextAngleType = TextAngleType.HorizontalAngle;
            style.LeaderLineType = LeaderType.StraightLeader;
            style.EnableDogleg = false;
            style.EnableLanding = false;
            SetExtendLeaderToText(style, false);
            style.DoglegLength = 0.0;
            style.LandingGap = IO.PatSettingsStore.Current.TextHeight *
                PatLeaderTextAttachment.TextGapPerHeight;
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
            if (chain.Count < 2) return;
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
