using Autodesk.AutoCAD.Geometry;
using System;
using System.Globalization;
using System.Text;

namespace PatentMarker.Commands
{
    /// <summary>
    /// Pure annotation-text assertions used by PATGEOMVERIFY and unit tests.
    /// Rotation is compared in radians, with one normalization step for values
    /// such as 2*pi. Retract expectations reproduce the documented branch:
    /// 0.4*H when the segment is long enough, otherwise its midpoint; a
    /// zero-length segment or zero text height returns the text point.
    /// </summary>
    public static class PatGeomTextAssertions
    {
        public const double TextGapPerHeight = 0.4;
        private const double RotationTolerance = 1e-9;
        private const double DirectionTolerance = 1e-9;
        private const double ShapeToleranceRatio = 1e-6;

        public static double NormalizeRadians(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return value;
            double twoPi = Math.PI * 2.0;
            double normalized = value % twoPi;
            if (normalized > Math.PI) normalized -= twoPi;
            if (normalized < -Math.PI) normalized += twoPi;
            return normalized;
        }

        public static bool VerifyRotation(
            bool readable, double rotationRadians, StringBuilder report)
        {
            if (!readable)
                return Report(report, false, "T1 text-horizontal",
                    "Rotation unavailable");
            double normalized = NormalizeRadians(rotationRadians);
            if (double.IsNaN(normalized) || double.IsInfinity(normalized))
                return Report(report, false, "T1 text-horizontal",
                    "non-finite rotation=" + rotationRadians.ToString("R", CultureInfo.InvariantCulture));
            bool pass = Math.Abs(normalized) <= RotationTolerance;
            return Report(report, pass, "T1 text-horizontal",
                "rotation=" + normalized.ToString("R", CultureInfo.InvariantCulture)
                + " rad");
        }

        public static bool VerifyDirection(
            bool readable, Vector3d direction, StringBuilder report)
        {
            if (!readable)
                return Report(report, false, "T2 text-x-direction",
                    "Direction unavailable");
            double length = Math.Sqrt(
                direction.X * direction.X
                + direction.Y * direction.Y
                + direction.Z * direction.Z);
            if (length <= DirectionTolerance || double.IsNaN(length) || double.IsInfinity(length))
                return Report(report, false, "T2 text-x-direction",
                    "zero/invalid direction");
            double x = direction.X / length;
            double y = direction.Y / length;
            double z = direction.Z / length;
            bool pass = x >= 1.0 - DirectionTolerance
                && Math.Abs(y) <= DirectionTolerance
                && Math.Abs(z) <= DirectionTolerance;
            return Report(report, pass, "T2 text-x-direction",
                "unit=(" + F(x) + "," + F(y) + "," + F(z) + ")");
        }

        public static bool VerifyRetract(
            bool readable,
            Point3d previous,
            Point3d textPoint,
            Point3d actualEndpoint,
            double textHeight,
            StringBuilder report)
        {
            if (!readable)
                return Report(report, false, "T3 leader-text-retract",
                    "leader endpoint chain unavailable");
            if (double.IsNaN(textHeight) || double.IsInfinity(textHeight))
                return Report(report, false, "T3 leader-text-retract",
                    "text height unavailable");
            Point3d expected = ExpectedRetract(previous, textPoint, textHeight);
            double error = Distance(expected, actualEndpoint);
            double tolerance = Math.Max(1.0, Math.Abs(textHeight)) * ShapeToleranceRatio;
            bool pass = error <= tolerance;
            return Report(report, pass, "T3 leader-text-retract",
                "actual=" + Point(actualEndpoint) + " expected=" + Point(expected)
                + " error=" + F(error) + " tol=" + F(tolerance));
        }

        public static bool VerifyPointMatch(
            bool readable,
            string id,
            Point3d expected,
            Point3d actual,
            double referenceLength,
            StringBuilder report)
        {
            if (!readable)
                return Report(report, false, id, "location unavailable");
            if (double.IsNaN(referenceLength) || double.IsInfinity(referenceLength))
                return Report(report, false, id, "text height unavailable");
            double error = Distance(expected, actual);
            double tolerance = Math.Max(1.0, Math.Abs(referenceLength)) * ShapeToleranceRatio;
            bool pass = error <= tolerance;
            return Report(report, pass, id,
                "actual=" + Point(actual) + " expected=" + Point(expected)
                + " error=" + F(error) + " tol=" + F(tolerance));
        }

        public static Point3d ExpectedRetract(
            Point3d previous, Point3d textPoint, double textHeight)
        {
            double dx = textPoint.X - previous.X;
            double dy = textPoint.Y - previous.Y;
            double dz = textPoint.Z - previous.Z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double gap = TextGapPerHeight * (textHeight < 0.0 ? 0.0 : textHeight);
            if (length < 1e-9 || gap < 1e-9) return textPoint;
            double safeGap = gap < length ? gap : length * 0.5;
            double t = safeGap / length;
            return new Point3d(
                textPoint.X - dx * t,
                textPoint.Y - dy * t,
                textPoint.Z - dz * t);
        }

        private static bool Report(
            StringBuilder report, bool pass, string id, string detail)
        {
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  [{0}] {1}: {2}", pass ? "PASS" : "FAIL", id, detail));
            return pass;
        }

        private static double Distance(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static string Point(Point3d point)
        {
            return "(" + F(point.X) + "," + F(point.Y) + "," + F(point.Z) + ")";
        }

        private static string F(double value)
        {
            return value.ToString("F4", CultureInfo.InvariantCulture);
        }
    }
}