using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using System;

namespace PatentMarker.Styles
{
    /// <summary>
    /// 创建 PAT_DIM 标注样式 — AutoCAD 2007 (.NET 2.0) 版本。
    ///
    /// 2007 无 MLeaderStyle，Leader 继承 Dimension，外观由 DimStyleTableRecord 控制。
    /// PAT_DIM 样式确保所有 PAT 引线的箭头大小、文字高度一致。
    /// </summary>
    public static class PatStyleInitializer
    {
        public const string DimStyleName = "PAT_DIM";
        public const string TextStyleName = "TIMES_ROMAN";

        /// <summary>
        /// 确保 PAT_DIM 标注样式存在。安全可多次调用。
        /// 创建自己的 Transaction（用于命令入口处的懒初始化）。
        /// </summary>
        public static void EnsurePatDimStyle()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                EnsurePatDimStyle(doc.Database, tr);
                tr.Commit();
            }
        }

        /// <summary>
        /// 确保 PAT_DIM 标注样式存在（使用外层 Transaction，修复 B1/B2 嵌套事务问题）。
        /// </summary>
        public static void EnsurePatDimStyle(Database db, Transaction tr)
        {
            DimStyleTable dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (dst.Has(DimStyleName)) return;

            dst.UpgradeOpen();
            DimStyleTableRecord dsr = new DimStyleTableRecord();
            dsr.Name = DimStyleName;
            ObjectId dimId = dst.Add(dsr);
            tr.AddNewlyCreatedDBObject(dsr, true);

            try
            {
                dsr.Dimasz = 2.5;       // 箭头大小
                dsr.Dimtxt = 3.5;       // 文字高度
                dsr.Dimgap = 0.625;     // 文字与引线间距
                dsr.Dimtad = 0;         // 文字垂直居中

                // 设置文字样式
                ObjectId tsId = GetOrCreateTimesRoman(db, tr);
                if (!tsId.IsNull)
                    dsr.Dimtxsty = tsId;
            }
            catch (Exception ex)
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                    doc.Editor.WriteMessage("\nPatentMarker: 警告 - 无法完全配置 PAT_DIM: " + ex.Message + "\n");
            }
        }

        /// <summary>
        /// 获取 PAT_DIM 的 ObjectId（使用外层 Transaction，修复 B2）。
        /// 若不存在则返回当前标注样式作为回退。
        /// </summary>
        public static ObjectId GetPatDimStyleId(Database db, Transaction tr)
        {
            DimStyleTable dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (dst.Has(DimStyleName))
                return dst[DimStyleName];
            return db.Dimstyle; // 回退到当前标注样式
        }

        /// <summary>
        /// 获取或创建 "TIMES_ROMAN" 文字样式（使用外层 Transaction，修复 B1）。
        /// </summary>
        public static ObjectId GetOrCreateTimesRoman(Database db, Transaction tr)
        {
            TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);

            if (tst.Has(TextStyleName))
                return tst[TextStyleName];

            tst.UpgradeOpen();
            TextStyleTableRecord tsr = new TextStyleTableRecord();
            tsr.Name = TextStyleName;
            tsr.FileName = "times.ttf";
            tsr.XScale = 1.0;
            ObjectId id = tst.Add(tsr);
            tr.AddNewlyCreatedDBObject(tsr, true);
            return id;
        }
    }
}
