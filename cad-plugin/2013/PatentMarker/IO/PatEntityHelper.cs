using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PatentMarker.Styles;

namespace PatentMarker.IO
{
    /// <summary>
    /// PAT 瀹炰綋璇嗗埆宸ュ叿 鈥?缁熶竴鎵€鏈夊懡浠や腑鐨?PAT_DIM 杩囨护閫昏緫锛堜慨澶?S3锛夈€?
    /// 2007 鐗堥€氳繃 DimStyle 鍚?== "PAT_DIM" 璇嗗埆 PAT 寮曠嚎銆?
    /// </summary>
    public static class PatEntityHelper
    {
        /// <summary>
        /// 鍒ゆ柇瀹炰綋鏄惁涓?PAT 寮曠嚎锛圠eader 涓?DimStyle == PAT_DIM锛夈€?
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
        /// 璇诲彇 PAT 寮曠嚎鐨勭紪鍙锋枃瀛椼€?
        /// 浼樺厛浠庡叧鑱旂殑 MText 璇诲彇锛屽洖閫€鍒?DimensionText銆?
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

            // 2007 鐨?Leader 娌℃湁 DimensionText 灞炴€э紝閫氳繃 Annotation ObjectId 鑾峰彇鏂囧瓧鍐呭
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
        /// 鑾峰彇 PAT 寮曠嚎鐨勬枃瀛椾綅缃紙MText.Location锛夈€?
        /// 鑻ユ棤鍏宠仈 MText锛屽洖閫€鍒版渶鍚庝竴涓?vertex銆?
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
        /// v4.0锛氳缃?PAT 寮曠嚎鐨勬爣娉ㄦ枃瀛椾负鏂扮紪鍙凤紙annotation 闇€宸叉寜 ForWrite 鎵撳紑锛夈€?
        /// 鏀寔 MText锛圕ontents锛変笌 DBText锛圱extString锛変袱绉?annotation銆?
        /// 鏂囧瓧鏈彉鍖栨椂杩斿洖 false锛堜笉璁″叆淇敼鏁帮級銆?
        /// </summary>
        public static bool SetLeaderNumber(Entity annotation, string newNumber)
        {
            try
            {
                MText mt = annotation as MText;
                if (mt != null)
                {
                    if (NumberIdentity.AreEqual(mt.Contents, newNumber)) return false;
                    mt.Contents = newNumber;
                    return true;
                }
                DBText dt = annotation as DBText;
                if (dt != null)
                {
                    if (NumberIdentity.AreEqual(dt.TextString, newNumber)) return false;
                    dt.TextString = newNumber;
                    return true;
                }
            }
            catch { return false; }
            return false;
        }

        /// <summary>
        /// v4.0锛氬湪妯″瀷绌洪棿鍐呮壂鎻?PAT 寮曠嚎锛屾妸缂栧彿锛坱rim 鍚庡拷鐣ュぇ灏忓啓锛夌瓑浜?oldNumber
        /// 鐨勬爣娉ㄦ枃瀛楁敼涓?newNumber銆傝繑鍥炲疄闄呬慨鏀规潯鏁般€傝皟鐢ㄦ柟璐熻矗浜嬪姟寮€鍚笌鎻愪氦銆?
        /// 涓?BZC 鐨勬枃瀛楀尮閰嶅彛寰勪竴鑷达紙GetLeaderNumber锛夈€?
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
                if (!NumberIdentity.AreEqual(number, oldNumber)) continue;

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
