using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PatentMarker.Styles;

namespace PatentMarker.IO
{
    /// <summary>
    /// PAT entity recognition and annotation text operations shared by all
    /// Leader + MText editions.
    /// </summary>
    public static class PatEntityHelper
    {
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

            if (leader.Annotation != ObjectId.Null)
            {
                DBText textEnt = tr.GetObject(leader.Annotation, OpenMode.ForRead) as DBText;
                if (textEnt != null) return textEnt.TextString != null ? textEnt.TextString.Trim() : textEnt.TextString;
                MText mtextEnt = tr.GetObject(leader.Annotation, OpenMode.ForRead) as MText;
                if (mtextEnt != null) return mtextEnt.Contents != null ? mtextEnt.Contents.Trim() : mtextEnt.Contents;
            }
            return "";
        }

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
