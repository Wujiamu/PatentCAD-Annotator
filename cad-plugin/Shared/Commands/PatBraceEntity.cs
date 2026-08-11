using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;

namespace PatentMarker.Commands
{
    /// <summary>
    /// Database representation for a PATBRACE entity. The visible geometry
    /// is a single lightweight Polyline; the parametric definition is kept in
    /// an extension-dictionary Xrecord so the brace can be rebuilt exactly.
    /// </summary>
    public static class PatBraceEntity
    {
        public const string ExtensionKey = "PATENTMARKER_BRACE";
        private const string VersionMarker = "PATENTMARKER_BRACE_V1";

        public static Polyline CreatePolyline(
            Database db, PatBraceDefinition definition)
        {
            Polyline polyline = new Polyline();
            polyline.SetDatabaseDefaults(db);
            ApplyGeometry(polyline, definition);
            return polyline;
        }

        public static void ApplyGeometry(
            Polyline polyline, PatBraceDefinition definition)
        {
            System.Collections.Generic.List<Point3d> points =
                PatBraceGeometry.BuildPoints(definition);
            for (int i = 0; i < points.Count; i++)
            {
                polyline.AddVertexAt(i,
                    new Point2d(points[i].X, points[i].Y), 0.0, 0.0, 0.0);
            }
            polyline.Elevation = definition.Top.Z;
        }

        public static bool IsBrace(Entity entity, Transaction tr)
        {
            Polyline polyline = entity as Polyline;
            if (polyline == null || polyline.ExtensionDictionary.IsNull)
                return false;

            try
            {
                DBDictionary dictionary = (DBDictionary)tr.GetObject(
                    polyline.ExtensionDictionary, OpenMode.ForRead);
                return dictionary.Contains(ExtensionKey);
            }
            catch
            {
                return false;
            }
        }

        public static void WriteDefinition(
            Polyline polyline, PatBraceDefinition definition, Transaction tr)
        {
            if (polyline.ExtensionDictionary.IsNull)
                polyline.CreateExtensionDictionary();

            DBDictionary dictionary = (DBDictionary)tr.GetObject(
                polyline.ExtensionDictionary, OpenMode.ForWrite);
            ResultBuffer data = new ResultBuffer(
                new TypedValue(1000, VersionMarker),
                new TypedValue(1040, definition.Top.X),
                new TypedValue(1040, definition.Top.Y),
                new TypedValue(1040, definition.Top.Z),
                new TypedValue(1040, definition.Bottom.X),
                new TypedValue(1040, definition.Bottom.Y),
                new TypedValue(1040, definition.Bottom.Z),
                new TypedValue(1040, definition.Width),
                new TypedValue(1070, definition.Side));

            if (dictionary.Contains(ExtensionKey))
            {
                Xrecord existing = tr.GetObject(
                    dictionary.GetAt(ExtensionKey), OpenMode.ForWrite) as Xrecord;
                if (existing == null)
                    throw new InvalidOperationException(
                        "The brace extension entry is not an Xrecord.");
                existing.Data = data;
                return;
            }

            Xrecord record = new Xrecord();
            record.Data = data;
            dictionary.SetAt(ExtensionKey, record);
            tr.AddNewlyCreatedDBObject(record, true);
        }

        public static bool TryReadDefinition(
            Polyline polyline, Transaction tr, out PatBraceDefinition definition)
        {
            definition = null;
            if (polyline == null || polyline.ExtensionDictionary.IsNull)
                return false;

            try
            {
                DBDictionary dictionary = (DBDictionary)tr.GetObject(
                    polyline.ExtensionDictionary, OpenMode.ForRead);
                if (!dictionary.Contains(ExtensionKey))
                    return false;

                Xrecord record = (Xrecord)tr.GetObject(
                    dictionary.GetAt(ExtensionKey), OpenMode.ForRead);
                double[] numbers = new double[7];
                int numberIndex = 0;
                int side = 1;
                bool markerFound = false;
                using (ResultBuffer data = record.Data)
                {
                    if (data == null)
                        return false;
                    foreach (TypedValue value in data)
                    {
                        if (value.TypeCode == 1000 &&
                            string.Equals((string)value.Value, VersionMarker,
                                StringComparison.Ordinal))
                            markerFound = true;
                        else if (value.TypeCode == 1040 && numberIndex < numbers.Length)
                            numbers[numberIndex++] = Convert.ToDouble(value.Value);
                        else if (value.TypeCode == 1070)
                            side = Convert.ToInt32(value.Value);
                    }
                }

                if (!markerFound || numberIndex != numbers.Length)
                    return false;

                definition = PatBraceGeometry.Create(
                    new Point3d(numbers[0], numbers[1], numbers[2]),
                    new Point3d(numbers[3], numbers[4], numbers[5]),
                    numbers[6], side);
                return true;
            }
            catch
            {
                definition = null;
                return false;
            }
        }

        public static void ReplaceGeometry(
            Polyline polyline, PatBraceDefinition definition, Transaction tr)
        {
            polyline.UpgradeOpen();
            while (polyline.NumberOfVertices > 0)
                polyline.RemoveVertexAt(polyline.NumberOfVertices - 1);
            ApplyGeometry(polyline, definition);
            WriteDefinition(polyline, definition, tr);
        }
    }
}
