using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace PatentMarker.Commands
{
    /// <summary>
    /// Selects the MText attachment point for a Leader + MText callout.
    /// The leader meets the vertical middle of the side facing the last
    /// user-selected leader vertex instead of MText's default top-left side.
    /// </summary>
    public static class PatLeaderTextAttachment
    {
        /// <summary>
        /// Text to the right of the final leader vertex uses its left side;
        /// text to the left uses its right side.
        /// </summary>
        public static AttachmentPoint Get(Point3d lastLeaderPoint, Point3d textPoint)
        {
            return textPoint.X >= lastLeaderPoint.X
                ? AttachmentPoint.MiddleLeft
                : AttachmentPoint.MiddleRight;
        }
    }
}
