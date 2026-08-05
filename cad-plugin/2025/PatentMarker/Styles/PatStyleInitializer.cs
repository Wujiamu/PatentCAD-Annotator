using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using PatentMarker.I18n;
using System;
using AppAcad = Autodesk.AutoCAD.ApplicationServices.Application;

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
            var doc = IO.RuntimeHost.ActiveDocument;
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
            if (mlDict.Contains(StyleName))
            {
                // 旧版本可能已经创建过 PAT_STYLE；每次使用前同步几何和文字约束，
                // 避免旧样式继续注入自动 dogleg 或让文字跟随引线倾斜。
                ObjectId existingId = mlDict.GetAt(StyleName);
                MLeaderStyle existingStyle = (MLeaderStyle)tr.GetObject(existingId, OpenMode.ForWrite);
                existingStyle.TextAttachmentDirection = TextAttachmentDirection.AttachmentHorizontal;
                existingStyle.TextAngleType = TextAngleType.HorizontalAngle;
                existingStyle.EnableDogleg = false;
                existingStyle.EnableLanding = false;
                existingStyle.ExtendLeaderToText = false;
                existingStyle.DoglegLength = 0.0;
                existingStyle.LandingGap = 0.0;
                return;
            }

            mlDict.UpgradeOpen();
            MLeaderStyle style = new MLeaderStyle();

            // 修复 eOwnerNotSet：必须先入库（SetAt + AddNewlyCreatedDBObject）再设置属性，
            // 未入库对象设置 Name 等属性会抛 Autodesk.AutoCAD.Runtime.Exception: eOwnerNotSet。
            // 注意：2013 SDK 的 MLeaderStyle 无公开 SetDatabaseDefaults，且属性均显式赋值，
            // 故不调用（2013/2015/2025 三版保持一致）。
            mlDict.SetAt(StyleName, style);
            tr.AddNewlyCreatedDBObject(style, true);

            style.Name = StyleName;

            // 内容类型：MText
            style.ContentType = ContentType.MTextContent;

            // 文字设置
            style.TextHeight = IO.PatSettingsStore.Current.TextHeight;
            style.TextAttachmentType = TextAttachmentType.AttachmentMiddle;
            style.TextAttachmentDirection = TextAttachmentDirection.AttachmentHorizontal;
            style.TextAngleType = TextAngleType.HorizontalAngle;

            // 引线设置
            style.LeaderLineType = LeaderType.SplineLeader;  // 默认样条曲线
            // MLeader 的默认 dogleg/landing 会在用户最后一点与文字之间
            // 自动插入额外顶点，导致三点模式出现第四个“吸附点”。
            style.EnableDogleg = false;
            style.EnableLanding = false;
            style.ExtendLeaderToText = false;
            style.DoglegLength = 0.0;
            style.LandingGap = 0.0;

            // 箭头设置
            style.ArrowSize = IO.PatSettingsStore.Current.ArrowSize;
            // 默认无箭头：设置箭头符号为无（通过 ArrowSymbolId = ObjectId.Null）
            style.ArrowSymbolId = ObjectId.Null;

            // 文字样式
            ObjectId tsId = GetOrCreateTimesRoman(db, tr);
            if (!tsId.IsNull)
                style.TextStyleId = tsId;
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
