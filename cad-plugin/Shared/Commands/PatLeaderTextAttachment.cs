using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace PatentMarker.Commands
{
    /// <summary>
    /// Selects the MText attachment point for a Leader + MText callout.
    /// The leader meets the side and vertical level facing the first
    /// user-selected leader vertex instead of MText's default top-left side.
    /// </summary>
    public static class PatLeaderTextAttachment
    {
        /// <summary>
        /// Selects the text corner facing the first leader vertex. A point
        /// above the text uses a top attachment, a point below uses a bottom
        /// attachment, and a point on the same horizontal level keeps the
        /// existing middle attachment behavior.
        /// </summary>
        public static AttachmentPoint Get(Point3d firstLeaderPoint, Point3d textPoint)
        {
            bool useLeftSide = textPoint.X >= firstLeaderPoint.X;
            bool leaderIsAboveText = firstLeaderPoint.Y > textPoint.Y;
            bool leaderIsBelowText = firstLeaderPoint.Y < textPoint.Y;

            if (leaderIsAboveText)
            {
                return useLeftSide ? AttachmentPoint.TopLeft : AttachmentPoint.TopRight;
            }

            if (leaderIsBelowText)
            {
                return useLeftSide ? AttachmentPoint.BottomLeft : AttachmentPoint.BottomRight;
            }

            return useLeftSide ? AttachmentPoint.MiddleLeft : AttachmentPoint.MiddleRight;
        }
    }
}
