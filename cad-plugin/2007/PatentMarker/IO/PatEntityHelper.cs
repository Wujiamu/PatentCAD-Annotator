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
    }
}
