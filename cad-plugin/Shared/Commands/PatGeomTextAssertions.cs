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

        // The insertion point is not necessarily on the facing text edge.
        // Accept a middle/corner connection on that edge, with an OUTSIDE gap.
        // Horizontal gaps serve native MLeader; a ray towards the previous
        // vertex also supports the detached Leader corner attachment.
        public static bool VerifyBoundaryGap(bool readable, Point3d minimum,
            Point3d maximum, Point3d previous, Point3d endpoint,
            double textHeight, StringBuilder report)
        {
            double error = BoundaryGapError(readable, minimum, maximum,
                previous, endpoint, textHeight);
            double tolerance = Math.Max(1.0, Math.Abs(textHeight)) * 1e-4;
            return Report(report, error <= tolerance, "T3 leader-text-retract",
                "boundary-gap error=" + F(error) + " gap="
                + F(textHeight * TextGapPerHeight) + " tol=" + F(tolerance));
        }

        public static double BoundaryGapError(bool readable, Point3d minimum,
            Point3d maximum, Point3d previous, Point3d endpoint, double height)
        {
            if (!readable || !Finite(height) || height <= 0 ||
                !FinitePoint(minimum) || !FinitePoint(maximum) ||
                !FinitePoint(previous) || !FinitePoint(endpoint) ||
                maximum.X <= minimum.X || maximum.Y <= minimum.Y)
                return double.PositiveInfinity;
            double center = minimum.X + (maximum.X - minimum.X) / 2.0;
            bool left = previous.X <= center;
            double edge = left ? minimum.X : maximum.X;
            double sign = left ? -1.0 : 1.0;
            // The terminal may never fall inside or on the text box.
            if (sign * (endpoint.X - edge) <= 0)
                return double.PositiveInfinity;
            double gap = height * TextGapPerHeight;
            double best = double.PositiveInfinity;
            double[] levels = new double[] { minimum.Y,
                minimum.Y + (maximum.Y - minimum.Y) / 2.0, maximum.Y };
            foreach (double y in levels)
            {
                Point3d anchor = new Point3d(edge, y, minimum.Z);
                best = Math.Min(best, Distance(endpoint,
                    new Point3d(edge + sign * gap, y, minimum.Z)));
                double dx = previous.X - edge, dy = previous.Y - y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length > 1e-9 && sign * dx > 0)
                {
                    double safe = gap < length ? gap : length * 0.5;
                    best = Math.Min(best, Distance(endpoint, new Point3d(
                        edge + dx * safe / length, y + dy * safe / length,
                        anchor.Z)));
                }
            }
            return best;
        }

        private static bool Finite(double n)
        { return !double.IsNaN(n) && !double.IsInfinity(n); }
        private static bool FinitePoint(Point3d p)
        { return Finite(p.X) && Finite(p.Y) && Finite(p.Z); }

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
