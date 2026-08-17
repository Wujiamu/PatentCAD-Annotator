using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Text;

namespace PatentMarker.Commands
{
    /// <summary>
    /// Selects the MText attachment point for a Leader + MText callout.
    /// The leader meets the side and vertical level facing the last
    /// user-selected leader vertex instead of MText's default top-left side.
    /// The same rule is used by both three-point and unlimited-point modes.
    /// </summary>
    public static class PatLeaderTextAttachment
    {
        private const string AnnotationLinkKey = "PATENTMARKER_MTEXT";
        private const int HardPointerIdCode = 340;

        /// <summary>引线末端与文字之间的间距 = 该值 × 字高，随字体大小同步变化。</summary>
        public const double TextGapPerHeight = 0.4;

        /// <summary>
        /// Selects the text corner facing the last leader vertex. The result
        /// is always one of the four corners. This is important when unlimited
        /// mode accepts Enter for the text point: the displayed text location
        /// can then coincide with the final dogleg, so the preceding segment
        /// direction is used to break the tie instead of falling back to the
        /// left-side midpoint.
        /// </summary>
        public static AttachmentPoint Get(Point3d lastLeaderPoint, Point3d textPoint)
        {
            bool useLeftSide = textPoint.X >= lastLeaderPoint.X;
            bool leaderIsAboveText = lastLeaderPoint.Y > textPoint.Y;
            bool leaderIsBelowText = lastLeaderPoint.Y < textPoint.Y;

            if (leaderIsAboveText)
            {
                return useLeftSide ? AttachmentPoint.TopLeft : AttachmentPoint.TopRight;
            }

            if (leaderIsBelowText)
            {
                return useLeftSide ? AttachmentPoint.BottomLeft : AttachmentPoint.BottomRight;
            }

            return useLeftSide ? AttachmentPoint.TopLeft : AttachmentPoint.TopRight;
        }

        /// <summary>
        /// Re-apply the requested MText attachment after the Leader/MText
        /// transaction has committed, then read it back from a fresh
        /// transaction. This keeps the text quadrant explicit even when an
        /// older host regenerates MText during commit.
        /// </summary>
        public static AttachmentPoint ReapplyAfterCommit(
            Database db,
            ObjectId annotationId,
            AttachmentPoint requested,
            Point3d location)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                MText text = (MText)tr.GetObject(annotationId, OpenMode.ForWrite);
                text.Attachment = requested;
                text.Location = location;
                tr.Commit();
            }

            using (Transaction verify = db.TransactionManager.StartTransaction())
            {
                MText text = (MText)verify.GetObject(annotationId, OpenMode.ForRead);
                return text.Attachment;
            }
        }

        /// <summary>
        /// Computes the retracted leader endpoint: the point on the last segment
        /// (from the preceding vertex toward the text) that stops just short of
        /// the text by a gap proportional to the text height. Keeps the leader
        /// from visually touching the text while the text stays anchored at its
        /// picked location.
        /// </summary>
        public static Point3d Retract(Point3d previous, Point3d textPoint, double textHeight)
        {
            double dx = textPoint.X - previous.X;
            double dy = textPoint.Y - previous.Y;
            double dz = textPoint.Z - previous.Z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double gap = TextGapPerHeight * (textHeight < 0 ? 0 : textHeight);
            if (length < 1e-9 || gap < 1e-9) return textPoint;
            // 段太短时最多回落到中点，避免越过前一点。
            double safeGap = gap < length ? gap : length * 0.5;
            double t = safeGap / length;
            return new Point3d(
                textPoint.X - dx * t,
                textPoint.Y - dy * t,
                textPoint.Z - dz * t);
        }

        /// <summary>
        /// Appends the retracted leader endpoint as the final Leader vertex,
        /// keeping a text-height-proportional gap from the text. A separate
        /// MText must not be assigned to Leader.Annotation: AutoCAD then creates
        /// a hook line and an extra text-side grip.
        /// </summary>
        public static void AppendTextEndpoint(Leader leader, Point3d textPoint, double textHeight)
        {
            Point3d retracted = textPoint;
            if (leader.NumVertices > 0)
                retracted = Retract(leader.VertexAt(leader.NumVertices - 1), textPoint, textHeight);
            if (leader.NumVertices == 0 || !SamePoint(
                leader.VertexAt(leader.NumVertices - 1), retracted))
            {
                leader.AppendVertex(retracted);
            }
        }

        /// <summary>
        /// Keeps the detached Leader endpoint retracted when PATALIGN moves the
        /// independently stored MText.
        /// </summary>
        public static void SetTextEndpoint(Leader leader, Point3d textPoint, double textHeight)
        {
            if (leader.NumVertices == 0) return;
            // 单顶点时以该顶点为参照，否则以倒数第二个稳定顶点为参照，
            // 让末顶点在跟随文字对齐位置的同时保持缩进间距。
            Point3d previous = leader.NumVertices == 1
                ? leader.VertexAt(0)
                : leader.VertexAt(leader.NumVertices - 2);
            leader.SetVertexAt(leader.NumVertices - 1,
                Retract(previous, textPoint, textHeight));
        }

        /// <summary>
        /// Stores the Leader/MText relationship without using AutoCAD's native
        /// annotation association, whose hook geometry is not user-controlled.
        /// </summary>
        public static void LinkText(Leader leader, MText text, Transaction tr)
        {
            if (leader.ExtensionDictionary.IsNull)
                leader.CreateExtensionDictionary();

            DBDictionary dictionary = (DBDictionary)tr.GetObject(
                leader.ExtensionDictionary, OpenMode.ForWrite);
            Xrecord record = new Xrecord();
            record.Data = new ResultBuffer(
                new TypedValue(HardPointerIdCode, text.ObjectId));
            dictionary.SetAt(AnnotationLinkKey, record);
            tr.AddNewlyCreatedDBObject(record, true);
        }

        /// <summary>
        /// Returns the linked MText for both new detached annotations and old
        /// drawings that still use Leader.Annotation.
        /// </summary>
        public static ObjectId GetAnnotationId(Leader leader, Transaction tr)
        {
            if (!leader.Annotation.IsNull)
                return leader.Annotation;

            try
            {
                if (leader.ExtensionDictionary.IsNull)
                    return ObjectId.Null;

                DBDictionary dictionary = (DBDictionary)tr.GetObject(
                    leader.ExtensionDictionary, OpenMode.ForRead);
                if (!dictionary.Contains(AnnotationLinkKey))
                    return ObjectId.Null;

                Xrecord record = (Xrecord)tr.GetObject(
                    dictionary.GetAt(AnnotationLinkKey), OpenMode.ForRead);
                using (ResultBuffer data = record.Data)
                {
                    if (data == null) return ObjectId.Null;
                    foreach (TypedValue value in data)
                    {
                        if (value.TypeCode == HardPointerIdCode &&
                            value.Value is ObjectId)
                        {
                            return (ObjectId)value.Value;
                        }
                    }
                }
            }
            catch
            {
                // A stale or damaged link must not break PATCHECK or cleanup.
            }

            return ObjectId.Null;
        }

        /// <summary>
        /// Reads the committed Leader geometry for diagnostics without changing
        /// the entity. This is intentionally tolerant: a logging failure must
        /// never turn a successfully created annotation into a command error.
        /// </summary>
        public static string DescribeLeader(Database db, ObjectId leaderId)
        {
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Leader leader = (Leader)tr.GetObject(leaderId, OpenMode.ForRead);
                    StringBuilder result = new StringBuilder();
                    result.Append("Leader state: NumVertices=").Append(leader.NumVertices);
                    result.Append(", vertices=[");
                    for (int i = 0; i < leader.NumVertices; i++)
                    {
                        if (i > 0) result.Append("; ");
                        result.Append(leader.VertexAt(i));
                    }
                    result.Append("], nativeAnnotation=").Append(leader.Annotation);
                    result.Append(", linkedAnnotation=").Append(
                        GetAnnotationId(leader, tr));
                    return result.ToString();
                }
            }
            catch (Exception ex)
            {
                return "Leader state read failed: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static bool SamePoint(Point3d left, Point3d right)
        {
            return Math.Abs(left.X - right.X) < 0.000000001 &&
                Math.Abs(left.Y - right.Y) < 0.000000001 &&
                Math.Abs(left.Z - right.Z) < 0.000000001;
        }
    }
}
