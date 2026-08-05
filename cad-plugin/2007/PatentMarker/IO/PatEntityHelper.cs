using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PatentMarker.Styles;

namespace PatentMarker.IO
{
    /// <summary>
    /// PAT 实体识别工具 — 统一所有命令中的 PAT_DIM 过滤逻辑（修复 S3）。
    /// 2007 版通过 DimStyle 名 == "PAT_DIM" 识别 PAT 引线。
    /// </summary>
    public static class PatEntityHelper
    {
        /// <summary>
        /// 判断实体是否为 PAT 引线（Leader 且 DimStyle == PAT_DIM）。
        /// </summary>
        public static bool IsPatEntity(Entity ent, Transaction tr)
        {
            Leader leader = ent as Leader;
            if (leader == null) return false;
            if (leader.DimensionStyle.IsNull) return false;
            try
            {
                DimStyleTableRecord dsr = (DimStyleTableRecord)tr.GetObject(
                    leader.DimensionStyle, OpenMode.ForRead);
                return dsr.Name == Styles.PatStyleInitializer.DimStyleName;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 读取 PAT 引线的编号文字。
        /// 优先从关联的 MText 读取，回退到 DimensionText。
        /// </summary>
        public static string GetLeaderNumber(Leader leader, Transaction tr)
        {
            try
            {
                if (!leader.Annotation.IsNull)
                {
                    MText mt = (MText)tr.GetObject(leader.Annotation, OpenMode.ForRead);
                    string text = mt.Contents;
                    if (text != null) text = text.Trim();
                    if (text != null && text.Length > 0) return text;
                }
            }
            catch { }

            // 2007 的 Leader 没有 DimensionText 属性，通过 Annotation ObjectId 获取文字内容
            if (leader.Annotation != ObjectId.Null)
            {
                DBText textEnt = tr.GetObject(leader.Annotation, OpenMode.ForRead) as DBText;
                if (textEnt != null) return textEnt.TextString != null ? textEnt.TextString.Trim() : textEnt.TextString;
                MText mtextEnt = tr.GetObject(leader.Annotation, OpenMode.ForRead) as MText;
                if (mtextEnt != null) return mtextEnt.Contents != null ? mtextEnt.Contents.Trim() : mtextEnt.Contents;
            }
            return "";
        }

        /// <summary>
        /// 获取 PAT 引线的文字位置（MText.Location）。
        /// 若无关联 MText，回退到最后一个 vertex。
        /// </summary>
        public static Point3d GetLeaderTextPos(Leader leader, Transaction tr)
        {
            try
            {
                if (!leader.Annotation.IsNull)
                {
                    MText mt = (MText)tr.GetObject(leader.Annotation, OpenMode.ForRead);
                    return mt.Location;
                }
            }
            catch { }

            if (leader.NumVertices > 0)
                return leader.VertexAt(leader.NumVertices - 1);
            return new Point3d(0, 0, 0);
        }

        /// <summary>
        /// v4.0：设置 PAT 引线的标注文字为新编号（annotation 需已按 ForWrite 打开）。
        /// 支持 MText（Contents）与 DBText（TextString）两种 annotation。
        /// 文字未变化时返回 false（不计入修改数）。
        /// </summary>
        public static bool SetLeaderNumber(Entity annotation, string newNumber)
        {
            try
            {
                MText mt = annotation as MText;
                if (mt != null)
                {
                    if (string.Equals(mt.Contents, newNumber, StringComparison.Ordinal)) return false;
                    mt.Contents = newNumber;
                    return true;
                }
                DBText dt = annotation as DBText;
                if (dt != null)
                {
                    if (string.Equals(dt.TextString, newNumber, StringComparison.Ordinal)) return false;
                    dt.TextString = newNumber;
                    return true;
                }
            }
            catch { return false; }
            return false;
        }

        /// <summary>
        /// v4.0：在模型空间内扫描 PAT 引线，把编号（trim 后忽略大小写）等于 oldNumber
        /// 的标注文字改为 newNumber。返回实际修改条数。调用方负责事务开启与提交。
        /// 与 BZC 的文字匹配口径一致（GetLeaderNumber）。
        /// </summary>
        public static int RenameNumberInModelSpace(Transaction tr,
            BlockTableRecord modelSpace, string oldNumber, string newNumber)
        {
            if (oldNumber == null || oldNumber.Length == 0) return 0;
            if (newNumber == null || newNumber.Length == 0) return 0;
            int changed = 0;
            foreach (ObjectId entId in modelSpace)
            {
                Entity ent = (Entity)tr.GetObject(entId, OpenMode.ForRead);
                Leader leader = ent as Leader;
                if (leader == null) continue;
                if (!IsPatEntity(leader, tr)) continue;

                string number = GetLeaderNumber(leader, tr);
                if (number == null || number.Length == 0) continue;
                if (!string.Equals(number, oldNumber, StringComparison.OrdinalIgnoreCase)) continue;

                if (leader.Annotation.IsNull) continue;
                try
                {
                    Entity ann = (Entity)tr.GetObject(leader.Annotation, OpenMode.ForWrite);
                    if (SetLeaderNumber(ann, newNumber)) changed++;
                }
                catch { }
            }
            return changed;
        }
    }
}
