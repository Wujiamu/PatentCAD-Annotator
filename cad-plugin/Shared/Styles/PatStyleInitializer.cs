using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using PatentMarker.I18n;
using System;

namespace PatentMarker.Styles
{
    /// <summary>Initializes the PAT dimension style used by Leader + MText annotations.</summary>
    public static class PatStyleInitializer
    {
        public const string StyleName = "PAT_STYLE";
        public const string DimStyleName = "PAT_DIM";
        public const string TextStyleName = "TIMES_ROMAN";

        public static void EnsurePatStyle()
        {
            EnsurePatDimStyle();
        }

        public static void EnsurePatStyle(Database db, Transaction tr)
        {
            EnsurePatDimStyle(db, tr);
        }

        public static void EnsurePatDimStyle()
        {
            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                EnsurePatDimStyle(doc.Database, tr);
                tr.Commit();
            }
        }

        public static void EnsurePatDimStyle(Database db, Transaction tr)
        {
            DimStyleTable table = (DimStyleTable)tr.GetObject(
                db.DimStyleTableId, OpenMode.ForRead);
            DimStyleTableRecord style;

            if (table.Has(DimStyleName))
            {
                style = (DimStyleTableRecord)tr.GetObject(
                    table[DimStyleName], OpenMode.ForWrite);
            }
            else
            {
                table.UpgradeOpen();
                style = new DimStyleTableRecord();
                style.Name = DimStyleName;
                table.Add(style);
                tr.AddNewlyCreatedDBObject(style, true);
            }

            try
            {
                style.Dimasz = IO.PatSettingsStore.Current.ArrowSize;
                style.Dimtxt = IO.PatSettingsStore.Current.TextHeight;
                style.Dimgap = 0.625;
                style.Dimtad = 0;

                ObjectId textStyleId = GetOrCreateTimesRoman(db, tr);
                if (!textStyleId.IsNull)
                    style.Dimtxsty = textStyleId;
            }
            catch (Exception ex)
            {
                var doc = IO.RuntimeHost.ActiveDocument;
                if (doc != null)
                    doc.Editor.WriteMessage(string.Format(
                        Strings.StyleInit_Warning, ex.Message));
            }
        }

        public static ObjectId GetPatDimStyleId(Database db, Transaction tr)
        {
            DimStyleTable table = (DimStyleTable)tr.GetObject(
                db.DimStyleTableId, OpenMode.ForRead);
            if (table.Has(DimStyleName))
                return table[DimStyleName];
            return db.Dimstyle;
        }

        public static ObjectId GetPatStyleId(Database db, Transaction tr)
        {
            return GetPatDimStyleId(db, tr);
        }

        public static ObjectId GetOrCreateTimesRoman(Database db, Transaction tr)
        {
            TextStyleTable table = (TextStyleTable)tr.GetObject(
                db.TextStyleTableId, OpenMode.ForRead);
            if (table.Has(TextStyleName))
                return table[TextStyleName];

            table.UpgradeOpen();
            TextStyleTableRecord style = new TextStyleTableRecord();
            style.Name = TextStyleName;
            style.FileName = "times.ttf";
            style.XScale = 1.0;
            ObjectId id = table.Add(style);
            tr.AddNewlyCreatedDBObject(style, true);
            return id;
        }
    }
}
