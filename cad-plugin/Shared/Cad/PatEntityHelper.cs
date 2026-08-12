using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PatentMarker.Commands;
using PatentMarker.Styles;

namespace PatentMarker.IO
{
    /// <summary>
    /// PAT entity recognition and annotation text operations shared by all
    /// Leader + MText editions.
    /// </summary>
    public static class PatEntityHelper
    {
        private const string StandaloneTextKey = "PATENTMARKER_TEXT";
        private const string StandaloneTextMarker = "PATENTMARKER_TEXT_V1";

        public static bool IsPatEntity(Entity ent, Transaction tr)
        {
            Leader leader = ent as Leader;
            if (leader != null)
            {
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

            MText text = ent as MText;
            return text != null && IsStandaloneText(text, tr);
        }

        /// <summary>
        /// Returns true for a standalone PAT MText created when the leader
        /// switch is off.  The marker is deliberately kept in an extension
        /// dictionary so ordinary drawing text is never mistaken for PAT text.
        /// </summary>
        public static bool IsStandaloneText(MText text, Transaction tr)
        {
            if (text == null || text.ExtensionDictionary.IsNull) return false;
            try
            {
                DBDictionary dictionary = (DBDictionary)tr.GetObject(
                    text.ExtensionDictionary, OpenMode.ForRead);
                if (!dictionary.Contains(StandaloneTextKey)) return false;
                Xrecord record = (Xrecord)tr.GetObject(
                    dictionary.GetAt(StandaloneTextKey), OpenMode.ForRead);
                using (ResultBuffer data = record.Data)
                {
                    if (data == null) return false;
                    foreach (TypedValue value in data)
                    {
                        if (value.TypeCode == 1 &&
                            value.Value is string &&
                            (string)value.Value == StandaloneTextMarker)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>Marks a newly-created MText as a standalone PAT annotation.</summary>
        public static void MarkStandaloneText(MText text, Transaction tr)
        {
            if (text.ExtensionDictionary.IsNull)
                text.CreateExtensionDictionary();

            DBDictionary dictionary = (DBDictionary)tr.GetObject(
                text.ExtensionDictionary, OpenMode.ForWrite);
            if (dictionary.Contains(StandaloneTextKey)) return;

            Xrecord record = new Xrecord();
            record.Data = new ResultBuffer(new TypedValue(1, StandaloneTextMarker));
            dictionary.SetAt(StandaloneTextKey, record);
            tr.AddNewlyCreatedDBObject(record, true);
        }

        /// <summary>
        /// Returns the MText contents with the optional AutoCAD underline
        /// wrapper removed, keeping dictionary matching independent of style.
        /// </summary>
        public static string GetTextNumber(MText text)
        {
            if (text == null) return "";
            return UnformatText(text.Contents);
        }

        public static string FormatText(string number, bool underline)
        {
            string value = number ?? "";
            return underline ? "\\L" + value + "\\l" : value;
        }

        public static bool IsUnderlined(string contents)
        {
            if (contents == null) return false;
            string value = contents.Trim();
            return value.Length >= 4 &&
                value.StartsWith("\\L", StringComparison.OrdinalIgnoreCase) &&
                value.EndsWith("\\l", StringComparison.OrdinalIgnoreCase);
        }

        private static string UnformatText(string contents)
        {
            string value = contents ?? "";
            value = value.Trim();
            if (IsUnderlined(value))
                value = value.Substring(2, value.Length - 4);
            return value.Trim();
        }

        private static bool SetTextNumber(MText text, string newNumber)
        {
            string current = GetTextNumber(text);
            if (NumberIdentity.AreEqual(current, newNumber)) return false;
            text.Contents = FormatText(newNumber, IsUnderlined(text.Contents));
            return true;
        }

        public static string GetLeaderNumber(Leader leader, Transaction tr)
        {
            ObjectId annotationId = PatLeaderTextAttachment.GetAnnotationId(leader, tr);
            try
            {
                if (!annotationId.IsNull)
                {
                    MText mt = (MText)tr.GetObject(annotationId, OpenMode.ForRead);
                    string text = GetTextNumber(mt);
                    if (text != null && text.Length > 0) return text;
                }
            }
            catch { }

            if (!annotationId.IsNull)
            {
                DBText textEnt = tr.GetObject(annotationId, OpenMode.ForRead) as DBText;
                if (textEnt != null) return textEnt.TextString != null ? textEnt.TextString.Trim() : textEnt.TextString;
                MText mtextEnt = tr.GetObject(annotationId, OpenMode.ForRead) as MText;
                if (mtextEnt != null) return GetTextNumber(mtextEnt);
            }
            return "";
        }

        public static Point3d GetLeaderTextPos(Leader leader, Transaction tr)
        {
            ObjectId annotationId = PatLeaderTextAttachment.GetAnnotationId(leader, tr);
            try
            {
                if (!annotationId.IsNull)
                {
                    MText mt = (MText)tr.GetObject(annotationId, OpenMode.ForRead);
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
                    return SetTextNumber(mt, newNumber);
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
                if (leader == null)
                {
                    MText standaloneText = ent as MText;
                    if (standaloneText == null || !IsStandaloneText(standaloneText, tr))
                        continue;
                    string standaloneNumber = GetTextNumber(standaloneText);
                    if (!NumberIdentity.AreEqual(standaloneNumber, oldNumber)) continue;
                    try
                    {
                        MText writableText = (MText)tr.GetObject(
                            standaloneText.ObjectId, OpenMode.ForWrite);
                        if (SetTextNumber(writableText, newNumber)) changed++;
                    }
                    catch { }
                    continue;
                }
                if (!IsPatEntity(leader, tr)) continue;

                string number = GetLeaderNumber(leader, tr);
                if (number == null || number.Length == 0) continue;
                if (!NumberIdentity.AreEqual(number, oldNumber)) continue;

                ObjectId annotationId = PatLeaderTextAttachment.GetAnnotationId(leader, tr);
                if (annotationId.IsNull) continue;
                try
                {
                    Entity ann = (Entity)tr.GetObject(annotationId, OpenMode.ForWrite);
                    if (SetLeaderNumber(ann, newNumber)) changed++;
                }
                catch { }
            }
            return changed;
        }
    }
}
