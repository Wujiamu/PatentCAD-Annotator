using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PatentMarker.Commands;
using PatentMarker.Styles;
using System.Reflection;

namespace PatentMarker.IO
{
    /// <summary>
    /// PAT entity recognition and annotation text operations shared by all
    /// Leader + MText and MLeader editions. MLeader is accessed through
    /// reflection so this file remains loadable by the 2007 CLR/API profile.
    /// </summary>
    public static class PatEntityHelper
    {
        private const string StandaloneTextKey = "PATENTMARKER_TEXT";
        private const string StandaloneTextMarker = "PATENTMARKER_TEXT_V1";
        private const string MLeaderMarkerKey = "PATENTMARKER_MLEADER";
        private const string MLeaderMarkerValue = "PATENTMARKER_MLEADER_V1";

        public static bool IsPatEntity(Entity ent, Transaction tr)
        {
            if (IsPatMLeader(ent, tr)) return true;

            Leader leader = ent as Leader;
            if (leader != null)
            {
                try
                {
                    if (!leader.DimensionStyle.IsNull)
                    {
                        DimStyleTableRecord dsr = (DimStyleTableRecord)tr.GetObject(
                            leader.DimensionStyle, OpenMode.ForRead);
                        if (dsr.Name == Styles.PatStyleInitializer.DimStyleName)
                            return true;
                    }
                }
                catch { }
                // Old or host-normalized Leaders can lose the temporary
                // dimension-style identity while retaining the product link.
                return HasLeaderAnnotationLink(leader, tr);
            }

            MText text = ent as MText;
            return text != null && IsStandaloneText(text, tr);
        }

        private static bool HasLeaderAnnotationLink(Leader leader, Transaction tr)
        {
            if (leader == null || leader.ExtensionDictionary.IsNull) return false;
            try
            {
                DBDictionary dictionary = (DBDictionary)tr.GetObject(
                    leader.ExtensionDictionary, OpenMode.ForRead);
                return dictionary.Contains("PATENTMARKER_MTEXT");
            }
            catch { return false; }
        }

        /// <summary>
        /// Recognizes a Plan-F MLeader without statically referencing the
        /// MLeader type (which does not exist in the 2007 SDK).
        /// </summary>
        public static bool IsPatMLeader(Entity ent, Transaction tr)
        {
            if (ent == null || ent.GetType().FullName !=
                "Autodesk.AutoCAD.DatabaseServices.MLeader") return false;
            if (ent.ExtensionDictionary.IsNull) return false;
            try
            {
                DBDictionary dictionary = (DBDictionary)tr.GetObject(
                    ent.ExtensionDictionary, OpenMode.ForRead);
                if (!dictionary.Contains(MLeaderMarkerKey)) return false;
                Xrecord record = (Xrecord)tr.GetObject(
                    dictionary.GetAt(MLeaderMarkerKey), OpenMode.ForRead);
                using (ResultBuffer data = record.Data)
                {
                    if (data == null) return false;
                    foreach (TypedValue value in data)
                    {
                        if (value.TypeCode == 1 && value.Value is string &&
                            (string)value.Value == MLeaderMarkerValue)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Reads the Plan-F MLeader point chain without referencing the
        /// MLeader type. The chain is attach, zero or more doglegs, then the
        /// recorded text point as its final element.
        /// </summary>
        public static bool TryReadMLeaderChain(
            Entity mleader,
            Transaction tr,
            out List<Point3d> chain,
            out bool hasArrow,
            out bool isSplined)
        {
            chain = new List<Point3d>();
            hasArrow = false;
            isSplined = false;
            if (mleader == null || mleader.ExtensionDictionary.IsNull) return false;
            try
            {
                DBDictionary dictionary = (DBDictionary)tr.GetObject(
                    mleader.ExtensionDictionary, OpenMode.ForRead);
                if (!dictionary.Contains(MLeaderMarkerKey)) return false;
                Xrecord record = (Xrecord)tr.GetObject(
                    dictionary.GetAt(MLeaderMarkerKey), OpenMode.ForRead);
                using (ResultBuffer data = record.Data)
                {
                    if (data == null) return false;
                    foreach (TypedValue value in data)
                    {
                        if (value.TypeCode == 70)
                            hasArrow = Convert.ToInt32(value.Value) == 1;
                        else if (value.TypeCode == 71)
                            isSplined = Convert.ToInt32(value.Value) == 1;
                        else if (value.TypeCode == 10 && value.Value is Point3d)
                            chain.Add((Point3d)value.Value);
                    }
                }
                return chain.Count > 0;
            }
            catch { return false; }
        }

        /// <summary>Reads GetLastVertex(0) from a Plan-F MLeader via reflection.</summary>
        public static bool TryGetMLeaderLastVertex(Entity mleader, out Point3d point)
        {
            point = new Point3d(0, 0, 0);
            if (mleader == null) return false;
            try
            {
                MethodInfo method = mleader.GetType().GetMethod(
                    "GetLastVertex", new Type[] { typeof(int) });
                if (method == null) return false;
                object value = method.Invoke(mleader, new object[] { 0 });
                if (!(value is Point3d)) return false;
                point = (Point3d)value;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Reads MLeader.TextLocation via reflection.</summary>
        /// <summary>Reads the actual penultimate and final MLeader vertices via reflection.</summary>
        public static bool TryReadMLeaderActualPreviousVertex(
            Entity mleader, out Point3d previous, out Point3d last)
        {
            previous = new Point3d(0, 0, 0);
            last = new Point3d(0, 0, 0);
            if (mleader == null) return false;
            try
            {
                Type type = mleader.GetType();
                MethodInfo getLast = type.GetMethod(
                    "GetLastVertex", new Type[] { typeof(int) });
                MethodInfo getVertex = type.GetMethod(
                    "GetVertex", new Type[] { typeof(int), typeof(int) });
                MethodInfo getCount = type.GetMethod(
                    "VerticesCount", new Type[] { typeof(int) });
                if (getLast == null || getVertex == null || getCount == null) return false;
                object lastValue = getLast.Invoke(mleader, new object[] { 0 });
                if (!(lastValue is Point3d)) return false;
                last = (Point3d)lastValue;

                int count = (int)getCount.Invoke(mleader, new object[] { 0 });
                if (count < 2) return false;
                object value = getVertex.Invoke(mleader, new object[] { 0, count - 2 });
                if (!(value is Point3d)) return false;
                previous = (Point3d)value;
                return true;
            }
            catch { return false; }
        }


        public static bool TryGetMLeaderTextLocation(Entity mleader, out Point3d point)
        {
            point = new Point3d(0, 0, 0);
            if (mleader == null) return false;
            try
            {
                PropertyInfo property = mleader.GetType().GetProperty("TextLocation");
                if (property == null || !property.CanRead) return false;
                object value = property.GetValue(mleader, null);
                if (!(value is Point3d)) return false;
                point = (Point3d)value;
                return true;
            }
            catch { return false; }
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

        /// <summary>Reads the text carried by a Plan-F MLeader.</summary>
        public static string GetMLeaderNumber(Entity mleader)
        {
            MText text = GetMLeaderText(mleader);
            return GetTextNumber(text);
        }

        private static MText GetMLeaderText(Entity mleader)
        {
            if (mleader == null) return null;
            try
            {
                PropertyInfo property = mleader.GetType().GetProperty("MText");
                if (property == null || !property.CanRead) return null;
                return property.GetValue(mleader, null) as MText;
            }
            catch { return null; }
        }

        private static bool SetMLeaderNumber(Entity mleader, string newNumber)
        {
            try
            {
                PropertyInfo property = mleader.GetType().GetProperty("MText");
                MText text = GetMLeaderText(mleader);
                if (property == null || text == null) return false;
                bool changed = SetTextNumber(text, newNumber);
                // Older hosts return a mutable MText instance; hosts that
                // return a value copy still receive it through the setter.
                if (changed && property.CanWrite)
                    property.SetValue(mleader, text, null);
                return changed;
            }
            catch { return false; }
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
                if (IsPatMLeader(ent, tr))
                {
                    string mleaderNumber = GetMLeaderNumber(ent);
                    if (!NumberIdentity.AreEqual(mleaderNumber, oldNumber)) continue;
                    try
                    {
                        Entity writableMLeader = (Entity)tr.GetObject(
                            ent.ObjectId, OpenMode.ForWrite);
                        if (SetMLeaderNumber(writableMLeader, newNumber)) changed++;
                    }
                    catch { }
                    continue;
                }

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
