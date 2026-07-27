using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using PatentMarker.I18n;
using System;

namespace PatentMarker.Styles
{
    /// <summary>
    /// 创建 PAT_STYLE 多重引线样式 — AutoCAD 2013/2014 (.NET 4.0) 版本。
    ///
    /// 2013 引入 MLeader，使用 MLeaderStyle 管理引线外观。
    /// PAT_STYLE 确保所有 PAT 多重引线的箭头、文字、引线类型一致。
    /// </summary>
    public static class PatStyleInitializer
    {
        public const string StyleName = "PAT_STYLE";
        public const string TextStyleName = "TIMES_ROMAN";

        /// <summary>
        /// 确保 PAT_STYLE 多重引线样式存在。安全可多次调用。
        /// 创建自己的 Transaction（用于命令入口处的懒初始化）。
        /// </summary>
        public static void EnsurePatStyle()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                EnsurePatStyle(doc.Database, tr);
                tr.Commit();
            }
        }

        /// <summary>
        /// 确保 PAT_STYLE 多重引线样式存在（使用外层 Transaction）。
        /// </summary>
        public static void EnsurePatStyle(Database db, Transaction tr)
        {
            DBDictionary mlDict = (DBDictionary)tr.GetObject(
                db.MLeaderStyleDictionaryId, OpenMode.ForRead);
            if (mlDict.Contains(StyleName)) return;

            mlDict.UpgradeOpen();
            MLeaderStyle style = new MLeaderStyle();
            style.Name = StyleName;

            // 内容类型：MText
            style.ContentType = ContentType.MTextContent;

            // 文字设置
            style.TextHeight = 3.5;
            style.TextAttachmentType = TextAttachmentType.AttachmentMiddle;

            // 引线设置
            style.LeaderLineType = LeaderType.SplineLeader;  // 默认样条曲线
            style.EnableDogleg = true;
            style.DoglegLength = 8.0;

            // 箭头设置
            style.ArrowSize = 2.5;
            // 默认无箭头：设置箭头符号为无（通过 ArrowSymbolId = ObjectId.Null）
            style.ArrowSymbolId = ObjectId.Null;

            // 文字样式
            ObjectId tsId = GetOrCreateTimesRoman(db, tr);
            if (!tsId.IsNull)
                style.TextStyleId = tsId;

            mlDict.SetAt(StyleName, style);
            tr.AddNewlyCreatedDBObject(style, true);
        }

        /// <summary>
        /// 获取 PAT_STYLE 的 ObjectId（使用外层 Transaction）。
        /// 若不存在则返回当前多重引线样式作为回退。
        /// </summary>
        public static ObjectId GetPatStyleId(Database db, Transaction tr)
        {
            DBDictionary mlDict = (DBDictionary)tr.GetObject(
                db.MLeaderStyleDictionaryId, OpenMode.ForRead);
            if (mlDict.Contains(StyleName))
                return mlDict.GetAt(StyleName);
            return db.MLeaderstyle; // 回退到当前样式
        }

        /// <summary>
        /// 获取或创建 "TIMES_ROMAN" 文字样式（使用外层 Transaction）。
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
