using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PatentMarker.Styles;

namespace PatentMarker.IO
{
    /// <summary>
    /// PAT 实体识别工具 — 统一所有命令中的 PAT_STYLE 过滤逻辑。
    /// 通过 MLeaderStyle.Name == "PAT_STYLE" 识别 PAT 多重引线。
    /// </summary>
    public static class PatEntityHelper
    {
        /// <summary>
        /// 判断实体是否为 PAT 多重引线（MLeader 且 MLeaderStyle == PAT_STYLE）。
        /// </summary>
        public static bool IsPatEntity(Entity ent, Transaction tr)
        {
            MLeader mleader = ent as MLeader;
            if (mleader == null) return false;
            if (mleader.MLeaderStyle.IsNull) return false;
            try
            {
                MLeaderStyle style = (MLeaderStyle)tr.GetObject(
                    mleader.MLeaderStyle, OpenMode.ForRead);
                return style.Name == PatStyleInitializer.StyleName;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 读取 PAT 多重引线的编号文字。
        /// 从 MLeader 内嵌的 MText 读取。
        /// </summary>
        public static string GetMLeaderNumber(MLeader mleader)
        {
            try
            {
                MText mt = mleader.MText;
                if (mt != null)
                {
                    string text = mt.Contents;
                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }
            }
            catch { }
            return "";
        }

        /// <summary>
        /// 获取 PAT 多重引线的文字位置。
        /// 优先使用 TextLocation，回退到最后一个引线顶点。
        /// </summary>
        public static Point3d GetMLeaderTextPos(MLeader mleader)
        {
            try
            {
                Point3d tp = mleader.TextLocation;
                if (tp.X != 0 || tp.Y != 0 || tp.Z != 0)
                    return tp;
            }
            catch { }

            // 回退：取第一条引线的最后一个顶点
            try
            {
                if (mleader.LeaderLineCount > 0)
                {
                    return mleader.GetLastVertex(0);
                }
            }
            catch { }

            return new Point3d(0, 0, 0);
        }
    }
}
