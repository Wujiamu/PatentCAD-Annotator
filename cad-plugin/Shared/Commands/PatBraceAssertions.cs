using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PatentMarker.Commands
{
    /// <summary>
    /// Independent geometric assertions for one PATBRACE polyline.
    /// The reference is ECMA-376 DrawingML preset shape "rightBrace":
    /// endpoints on one axis, straight stems at half width, one center cusp at
    /// full width, symmetric quarter-ellipse corners, and no profile crossing
    /// the endpoint axis or the selected width.
    ///
    /// The checks intentionally do not call PatBraceGeometry. A failing check
    /// must not be silenced by changing this expected shape; update the
    /// DrawingML cross-check and the contract test together.
    /// </summary>
    public static class PatBraceAssertions
    {
        private const double AxisToleranceRatio = 1e-6;
        private const double ShapeToleranceRatio = 1e-5;
        private const double EndpointTolerance = 1e-9;
        private const double StemRatio = 0.5;
        private const double CuspRatio = 0.5;
        private const double CornerAlongRatio = 8333.0 / 100000.0;
        private const double MinCornerTurnDegrees = 30.0;
        private const double MaxCornerTurnDegrees = 150.0;
        private const double CornerReferenceTolerance = 0.01;
        private const int CornerReferenceSamples = 257;

        public static bool Verify(
            Polyline polyline,
            PatBraceDefinition definition,
            StringBuilder report)
        {
            if (report == null) throw new ArgumentNullException("report");
            bool ok = true;
            if (definition == null)
            {
                Fail(report, "B0a definition-readable", "no recorded parameters");
                return false;
            }

            List<Point3d> vertices = ReadVertices(polyline);
            if (vertices.Count < 2)
            {
                Fail(report, "B0b polyline-readable", vertices.Count + " vertices");
                return false;
            }
            // Vertices alone do not describe a lightweight polyline: closing
            // it adds an axis segment and a bulge replaces a chord by an arc.
            if (polyline.Closed)
            {
                Fail(report, "B0b polyline-readable", "brace must be open");
                return false;
            }
            for (int i = 0; i < vertices.Count; i++)
            {
                Point3d vertex = vertices[i];
                if (!IsFinite(vertex.X) || !IsFinite(vertex.Y) || !IsFinite(vertex.Z)
                    || !IsFinite(polyline.GetBulgeAt(i)) || polyline.GetBulgeAt(i) != 0.0)
                {
                    Fail(report, "B0b polyline-readable", "non-finite vertex or nonzero bulge at " + i);
                    return false;
                }
            }

            double height = definition.Height;
            double width = definition.Width;
            if (!IsFinite(height) || height <= 1e-9 ||
                !IsFinite(width) || width <= 1e-9)
            {
                Fail(report, "B0a definition-readable",
                    "height=" + F(height) + " width=" + F(width));
                return false;
            }

            double zTol = Math.Max(Math.Abs(definition.Top.Z), 1.0) * AxisToleranceRatio;
            bool planar = Math.Abs(definition.Bottom.Z - definition.Top.Z) <= zTol;
            for (int i = 0; i < vertices.Count && planar; i++)
                if (Math.Abs(vertices[i].Z - definition.Top.Z) > zTol) planar = false;
            bool planarPass = Report(report, ref ok, planar, "B0c planar-contract",
                planar ? "constant Z=" + F(definition.Top.Z)
                       : "non-constant Z is unsupported by this verifier");
            if (!planarPass) return false;

            double dx = definition.Bottom.X - definition.Top.X;
            double dy = definition.Bottom.Y - definition.Top.Y;
            double axisX = dx / height;
            double axisY = dy / height;
            double perpX = -axisY;
            double perpY = axisX;

            double[] alongs = new double[vertices.Count];
            double[] offsets = new double[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                double vx = vertices[i].X - definition.Top.X;
                double vy = vertices[i].Y - definition.Top.Y;
                alongs[i] = vx * axisX + vy * axisY;
                offsets[i] = (vx * perpX + vy * perpY) * definition.Side;
            }

            double axisTol = Math.Max(width, 1.0) * AxisToleranceRatio;
            double shapeTolW = Math.Max(width, 1.0) * ShapeToleranceRatio;
            double shapeTolH = Math.Max(height, 1.0) * ShapeToleranceRatio;

            double minOffset = offsets[0];
            double maxOffset = offsets[0];
            for (int i = 1; i < offsets.Length; i++)
            {
                if (offsets[i] < minOffset) minOffset = offsets[i];
                if (offsets[i] > maxOffset) maxOffset = offsets[i];
            }

            Report(report, ref ok, minOffset >= -axisTol,
                "B1 no-axis-crossing", "min=" + F(minOffset) + " tol=" + F(axisTol));
            Report(report, ref ok, maxOffset <= width + axisTol,
                "B4 within-selected-width", "max=" + F(maxOffset) + " width=" + F(width));
            Report(report, ref ok, maxOffset >= width - shapeTolW,
                "B2 tip-at-full-width", "max=" + F(maxOffset) + " tol=" + F(shapeTolW));

            double firstError = Distance(vertices[0], definition.Top);
            double lastError = Distance(vertices[vertices.Count - 1], definition.Bottom);
            Report(report, ref ok,
                firstError <= EndpointTolerance && lastError <= EndpointTolerance,
                "B5 endpoints-exact",
                "topErr=" + E(firstError) + " bottomErr=" + E(lastError));

            int tipIndex = -1;
            double bestCuspDistance = double.MaxValue;
            for (int i = 0; i < offsets.Length; i++)
            {
                double distance = Math.Abs(offsets[i] - width);
                if (distance < bestCuspDistance)
                {
                    bestCuspDistance = distance;
                    tipIndex = i;
                }
            }
            double cuspAlong = tipIndex >= 0 ? alongs[tipIndex] : double.NaN;
            Report(report, ref ok,
                tipIndex >= 0 && Math.Abs(cuspAlong - height * CuspRatio) <= shapeTolH,
                "B6 cusp-at-half-height",
                "along=" + F(cuspAlong) + " expected=" + F(height * CuspRatio));

            // B3: two straight stems must be represented by at least four
            // on-half-width vertices, distributed on both sides of the cusp.
            int stemCount = 0, stemUpper = 0, stemLower = 0;
            double stemWorst = 0.0;
            for (int i = 0; i < offsets.Length; i++)
            {
                double error = Math.Abs(offsets[i] - width * StemRatio);
                if (error > shapeTolW) continue;
                stemCount++;
                if (alongs[i] < height * CuspRatio) stemUpper++;
                else stemLower++;
                if (error > stemWorst) stemWorst = error;
            }
            Report(report, ref ok,
                stemCount >= 4 && stemUpper >= 2 && stemLower >= 2,
                "B3 stems-at-half-width",
                "count=" + stemCount + " upper=" + stemUpper + " lower=" + stemLower
                + " worst=" + F(stemWorst));

            double worstMirror = 0.0;
            bool symmetric = true;
            for (int i = 0; i < offsets.Length; i++)
            {
                double mirrorAlong = height - alongs[i];
                double best = double.MaxValue;
                for (int j = 0; j < offsets.Length; j++)
                {
                    if (Math.Abs(alongs[j] - mirrorAlong) > shapeTolH) continue;
                    double error = Math.Abs(offsets[j] - offsets[i]);
                    if (error < best) best = error;
                }
                if (best == double.MaxValue) { symmetric = false; break; }
                if (best > worstMirror) worstMirror = best;
                if (best > shapeTolW) { symmetric = false; break; }
            }
            Report(report, ref ok, symmetric, "B7 mirror-symmetry",
                "worstOffsetError=" + F(worstMirror) + " tol=" + F(shapeTolW));

            bool monotonic = true;
            for (int i = 1; i < alongs.Length; i++)
                if (alongs[i] < alongs[i - 1] - shapeTolH) { monotonic = false; break; }
            bool nondecreasingBefore = true;
            bool nonincreasingAfter = true;
            for (int i = 1; i <= tipIndex; i++)
                if (offsets[i] < offsets[i - 1] - axisTol) nondecreasingBefore = false;
            for (int i = tipIndex + 1; i < offsets.Length; i++)
                if (offsets[i] > offsets[i - 1] + axisTol) nonincreasingAfter = false;
            int cuspCount = 0;
            for (int i = 0; i < offsets.Length; i++)
                if (Math.Abs(offsets[i] - maxOffset) <= shapeTolW) cuspCount++;
            bool noSelfIntersection = !HasSelfIntersection(alongs, offsets, axisTol);
            Report(report, ref ok,
                monotonic && nondecreasingBefore && nonincreasingAfter
                && cuspCount == 1 && noSelfIntersection,
                "B8 single-cusp-no-self-intersection",
                "monotonic=" + monotonic + " rising=" + nondecreasingBefore
                + " falling=" + nonincreasingAfter + " cusps=" + cuspCount
                + " selfIntersection=" + (!noSelfIntersection));

            int topShoulder = FindFirstOffset(offsets, width * StemRatio, shapeTolW);
            int upperStemEnd = FindLastOffsetBefore(offsets, tipIndex, width * StemRatio, shapeTolW);
            int lowerStemStart = FindFirstOffsetAfter(offsets, tipIndex, width * StemRatio, shapeTolW);
            int bottomShoulder = FindLastOffset(offsets, width * StemRatio, shapeTolW);
            bool curvesPass = VerifyCornerCurves(report, ref ok, alongs, offsets,
                topShoulder, upperStemEnd, tipIndex, lowerStemStart, bottomShoulder,
                height, width, shapeTolW, axisTol);
            if (!curvesPass) return false;
            return ok;
        }

        private static bool VerifyCornerCurves(
            StringBuilder report, ref bool ok,
            double[] alongs, double[] offsets,
            int topShoulder, int upperStemEnd, int tipIndex,
            int lowerStemStart, int bottomShoulder,
            double height, double width, double shapeTolW, double axisTol)
        {
            double expected = Math.Min(width, height) * CornerAlongRatio;
            bool indicesValid = topShoulder > 0 && upperStemEnd >= topShoulder
                && tipIndex > upperStemEnd && lowerStemStart > tipIndex
                && bottomShoulder >= lowerStemStart && bottomShoulder < alongs.Length - 1;
            if (!indicesValid)
            {
                return Report(report, ref ok, false, "B9 corner-curves",
                    "corner landmarks missing: top=" + topShoulder
                    + " upperStem=" + upperStemEnd + " tip=" + tipIndex
                    + " lowerStem=" + lowerStemStart + " bottom=" + bottomShoulder);
            }

            double extentTol = Math.Max(shapeTolW, expected * 0.02);
            double[] extents = new double[]
            {
                alongs[topShoulder] - alongs[0],
                alongs[tipIndex] - alongs[upperStemEnd],
                alongs[lowerStemStart] - alongs[tipIndex],
                alongs[alongs.Length - 1] - alongs[bottomShoulder]
            };
            int[][] ranges = new int[][]
            {
                new int[] { 0, topShoulder },
                new int[] { upperStemEnd, tipIndex },
                new int[] { tipIndex, lowerStemStart },
                new int[] { bottomShoulder, alongs.Length - 1 }
            };
            bool extentsPass = true;
            bool turnsPass = true;
            bool referencePass = true;
            double worstExtent = 0.0;
            double worstTurn = 0.0;
            double worstReference = 0.0;
            double[] turns = new double[4];
            double stemHalf = width * StemRatio;
            for (int i = 0; i < extents.Length; i++)
            {
                if (Math.Abs(extents[i] - expected) > extentTol) extentsPass = false;
                if (Math.Abs(extents[i] - expected) > worstExtent)
                    worstExtent = Math.Abs(extents[i] - expected);
                double turn = TangentTurnDegrees(
                    alongs, offsets, ranges[i][0], ranges[i][1],
                    expected, stemHalf);
                turns[i] = turn;
                if (turn < MinCornerTurnDegrees || turn > MaxCornerTurnDegrees)
                    turnsPass = false;
                if (Math.Abs(turn - 90.0) > worstTurn)
                    worstTurn = Math.Abs(turn - 90.0);
                double referenceError = ReferenceCurveError(
                    alongs, offsets, ranges[i][0], ranges[i][1], i,
                    expected, stemHalf, height);
                if (referenceError > CornerReferenceTolerance) referencePass = false;
                if (referenceError > worstReference) worstReference = referenceError;
            }
            bool pass = extentsPass && turnsPass && referencePass;
            return Report(report, ref ok, pass, "B9 corner-curves",
                "expectedAlong=" + F(expected)
                + " extents=" + F(extents[0]) + "," + F(extents[1])
                + "," + F(extents[2]) + "," + F(extents[3])
                + " worstExtentErr=" + F(worstExtent)
                + " turns=" + F(turns[0]) + "," + F(turns[1])
                + "," + F(turns[2]) + "," + F(turns[3])
                + " refErr=" + F(worstReference));
        }

        private static double ReferenceCurveError(
            double[] alongs, double[] offsets, int start, int end, int section,
            double expectedAlong, double stemHalf, double height)
        {
            double worst = 0.0;
            for (int i = start; i <= end && i < alongs.Length; i++)
            {
                double x;
                double y;
                if (section == 0)
                {
                    x = alongs[i] / expectedAlong;
                    y = offsets[i] / stemHalf;
                }
                else if (section == 1)
                {
                    x = (alongs[i] - (height * CuspRatio - expectedAlong)) / expectedAlong;
                    y = (offsets[i] - stemHalf) / stemHalf;
                }
                else if (section == 2)
                {
                    x = (alongs[i] - height * CuspRatio) / expectedAlong;
                    y = (offsets[i] - stemHalf) / stemHalf;
                }
                else
                {
                    x = (alongs[i] - (height - expectedAlong)) / expectedAlong;
                    y = offsets[i] / stemHalf;
                }

                double best = double.MaxValue;
                for (int sample = 0; sample < CornerReferenceSamples; sample++)
                {
                    double theta = (Math.PI * 0.5) * sample / (CornerReferenceSamples - 1);
                    double rx;
                    double ry;
                    if (section == 0)
                    {
                        rx = 1.0 - Math.Cos(theta);
                        ry = Math.Sin(theta);
                    }
                    else if (section == 1)
                    {
                        rx = Math.Sin(theta);
                        ry = 1.0 - Math.Cos(theta);
                    }
                    else if (section == 2)
                    {
                        rx = 1.0 - Math.Cos(theta);
                        ry = 1.0 - Math.Sin(theta);
                    }
                    else
                    {
                        rx = Math.Sin(theta);
                        ry = Math.Cos(theta);
                    }
                    double dx = x - rx;
                    double dy = y - ry;
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance < best) best = distance;
                }
                if (best > worst) worst = best;
            }
            return worst;
        }

        private static List<Point3d> ReadVertices(Polyline polyline)
        {
            List<Point3d> vertices = new List<Point3d>();
            try
            {
                int count = polyline.NumberOfVertices;
                for (int i = 0; i < count; i++)
                    vertices.Add(polyline.GetPoint3dAt(i));
            }
            catch
            {
                return new List<Point3d>();
            }
            return vertices;
        }

        private static int FindFirstOffset(double[] offsets, double target, double tol)
        {
            for (int i = 0; i < offsets.Length; i++)
                if (Math.Abs(offsets[i] - target) <= tol) return i;
            return -1;
        }

        private static int FindLastOffset(double[] offsets, double target, double tol)
        {
            for (int i = offsets.Length - 1; i >= 0; i--)
                if (Math.Abs(offsets[i] - target) <= tol) return i;
            return -1;
        }

        private static int FindLastOffsetBefore(
            double[] offsets, int before, double target, double tol)
        {
            for (int i = Math.Min(before - 1, offsets.Length - 1); i >= 0; i--)
                if (Math.Abs(offsets[i] - target) <= tol) return i;
            return -1;
        }

        private static int FindFirstOffsetAfter(
            double[] offsets, int after, double target, double tol)
        {
            for (int i = after + 1; i < offsets.Length; i++)
                if (Math.Abs(offsets[i] - target) <= tol) return i;
            return -1;
        }

        private static double TangentTurnDegrees(
            double[] alongs, double[] offsets, int start, int end,
            double alongScale, double offsetScale)
        {
            if (start < 0 || end <= start || end >= alongs.Length) return 0.0;
            if (alongScale <= 1e-12 || offsetScale <= 1e-12) return 0.0;
            double ax = (alongs[start + 1] - alongs[start]) / alongScale;
            double ay = (offsets[start + 1] - offsets[start]) / offsetScale;
            double bx = (alongs[end] - alongs[end - 1]) / alongScale;
            double by = (offsets[end] - offsets[end - 1]) / offsetScale;
            double al = Math.Sqrt(ax * ax + ay * ay);
            double bl = Math.Sqrt(bx * bx + by * by);
            if (al <= 1e-12 || bl <= 1e-12) return 0.0;
            double cosine = (ax * bx + ay * by) / (al * bl);
            if (cosine > 1.0) cosine = 1.0;
            if (cosine < -1.0) cosine = -1.0;
            return Math.Acos(cosine) * 180.0 / Math.PI;
        }

        private static bool HasSelfIntersection(double[] alongs, double[] offsets, double tol)
        {
            int count = alongs.Length;
            for (int i = 0; i < count - 1; i++)
            {
                for (int j = i + 2; j < count - 1; j++)
                {
                    if (i == 0 && j == count - 2) continue;
                    if (SegmentsProperlyIntersect(
                        alongs[i], offsets[i], alongs[i + 1], offsets[i + 1],
                        alongs[j], offsets[j], alongs[j + 1], offsets[j + 1], tol))
                        return true;
                }
            }
            return false;
        }

        private static bool SegmentsProperlyIntersect(
            double ax, double ay, double bx, double by,
            double cx, double cy, double dx, double dy, double tol)
        {
            double d1 = Cross(cx, cy, dx, dy, ax, ay);
            double d2 = Cross(cx, cy, dx, dy, bx, by);
            double d3 = Cross(ax, ay, bx, by, cx, cy);
            double d4 = Cross(ax, ay, bx, by, dx, dy);
            return ((d1 > tol && d2 < -tol) || (d1 < -tol && d2 > tol))
                && ((d3 > tol && d4 < -tol) || (d3 < -tol && d4 > tol));
        }

        private static double Cross(
            double ax, double ay, double bx, double by, double px, double py)
        {
            return (bx - ax) * (py - ay) - (by - ay) * (px - ax);
        }

        private static double Distance(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static bool Report(
            StringBuilder report, ref bool ok, bool pass, string id, string detail)
        {
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  [{0}] {1}: {2}", pass ? "PASS" : "FAIL", id, detail));
            if (!pass) ok = false;
            return pass;
        }

        private static void Fail(StringBuilder report, string id, string detail)
        {
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  [FAIL] {0}: {1}", id, detail));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string F(double value)
        {
            return value.ToString("F4", CultureInfo.InvariantCulture);
        }

        private static string E(double value)
        {
            return value.ToString("E3", CultureInfo.InvariantCulture);
        }
    }
}
