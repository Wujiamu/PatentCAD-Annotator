using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;

namespace PatentMarker.Commands
{
    /// <summary>
    /// Parametric definition of one two-dimensional curly brace.
    /// Top and Bottom are the two endpoints. Width is the distance from the
    /// endpoint axis to the sharp center tip, and Side is the signed side of
    /// that axis (+1 or -1). The outer shoulders and stems sit on the side
    /// opposite the center tip, matching the PPT right-brace silhouette.
    /// </summary>
    public sealed class PatBraceDefinition
    {
        public PatBraceDefinition(Point3d top, Point3d bottom, double width, int side)
        {
            Top = top;
            Bottom = bottom;
            Width = Math.Abs(width);
            Side = side >= 0 ? 1 : -1;
        }

        public Point3d Top { get; private set; }
        public Point3d Bottom { get; private set; }
        public double Width { get; private set; }
        public int Side { get; private set; }

        public double Height
        {
            get
            {
                double dx = Bottom.X - Top.X;
                double dy = Bottom.Y - Top.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }
        }
    }

    /// <summary>
    /// Geometry-only part of PATBRACE. It is deliberately independent of
    /// database entities so all editions can exercise the same shape rules.
    /// </summary>
    public static class PatBraceGeometry
    {
        private const double Epsilon = 0.000000001;
        private const double DefaultWidthRatio = 0.22;

        // The profile follows the PowerPoint rightBrace silhouette. The
        // selected side is used only by the center cusp; the endpoint
        // shoulders and straight stems remain on the opposite side. Keeping
        // these stations explicit avoids the old W-shaped fixed point table.
        private const double CenterT = 0.5;
        private const double StemOffsetRatio = 0.42;
        private const double ShoulderAlongRatio = 0.12;
        private const double UpperStemAlongRatio = 0.32;
        private const double LowerStemAlongRatio = 0.68;
        private const double CenterControlAlongRatio = 0.10;
        private const double CenterTipControlOffsetRatio = 0.22;
        private const double CubicKappa = 0.5522847498307936;
        private const int CurveSamples = 8;

        private struct LocalPoint
        {
            public double Along;
            public double Offset;

            public LocalPoint(double along, double offset)
            {
                Along = along;
                Offset = offset;
            }
        }

        public static PatBraceDefinition FromPoints(
            Point3d top, Point3d bottom, Point3d widthPoint)
        {
            double dx = bottom.X - top.X;
            double dy = bottom.Y - top.Y;
            double height = Math.Sqrt(dx * dx + dy * dy);
            if (height < Epsilon)
                throw new ArgumentException("Brace endpoints must be different.");

            double perpX = -dy / height;
            double perpY = dx / height;
            double midX = (top.X + bottom.X) * 0.5;
            double midY = (top.Y + bottom.Y) * 0.5;
            double projection = (widthPoint.X - midX) * perpX
                + (widthPoint.Y - midY) * perpY;

            int side = projection >= 0 ? 1 : -1;
            double width = Math.Abs(projection);
            if (width < Epsilon)
                width = Math.Max(height * DefaultWidthRatio, 1.0);

            return Create(top, bottom, width, side);
        }

        public static PatBraceDefinition Create(
            Point3d top, Point3d bottom, double width, int side)
        {
            double dx = bottom.X - top.X;
            double dy = bottom.Y - top.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < Epsilon)
                throw new ArgumentException("Brace endpoints must be different.");
            if (Math.Abs(width) < Epsilon)
                throw new ArgumentException("Brace width must be greater than zero.");
            return new PatBraceDefinition(top, bottom, Math.Abs(width), side);
        }

        public static PatBraceDefinition WithEndpoints(
            PatBraceDefinition current, Point3d top, Point3d bottom)
        {
            return Create(top, bottom, current.Width, current.Side);
        }

        public static PatBraceDefinition WithSize(
            PatBraceDefinition current, double height, double width)
        {
            if (height < Epsilon)
                throw new ArgumentException("Brace height must be greater than zero.");
            if (width < Epsilon)
                throw new ArgumentException("Brace width must be greater than zero.");

            double dx = current.Bottom.X - current.Top.X;
            double dy = current.Bottom.Y - current.Top.Y;
            double currentHeight = Math.Sqrt(dx * dx + dy * dy);
            if (currentHeight < Epsilon)
                throw new ArgumentException("Current brace endpoints are invalid.");

            Point3d bottom = new Point3d(
                current.Top.X + dx / currentHeight * height,
                current.Top.Y + dy / currentHeight * height,
                current.Top.Z + (current.Bottom.Z - current.Top.Z) * height / currentHeight);
            return Create(current.Top, bottom, width, current.Side);
        }

        public static Point3d GetWidthPoint(PatBraceDefinition definition)
        {
            double dx = definition.Bottom.X - definition.Top.X;
            double dy = definition.Bottom.Y - definition.Top.Y;
            double height = Math.Sqrt(dx * dx + dy * dy);
            double perpX = -dy / height;
            double perpY = dx / height;
            double midX = (definition.Top.X + definition.Bottom.X) * 0.5;
            double midY = (definition.Top.Y + definition.Bottom.Y) * 0.5;
            double midZ = (definition.Top.Z + definition.Bottom.Z) * 0.5;
            return new Point3d(
                midX + perpX * definition.Width * definition.Side,
                midY + perpY * definition.Width * definition.Side,
                midZ);
        }

        public static List<Point3d> BuildPoints(PatBraceDefinition definition)
        {
            double dx = definition.Bottom.X - definition.Top.X;
            double dy = definition.Bottom.Y - definition.Top.Y;
            double height = Math.Sqrt(dx * dx + dy * dy);
            if (height < Epsilon)
                throw new ArgumentException("Brace endpoints must be different.");

            double axisX = dx / height;
            double axisY = dy / height;
            double perpX = -axisY;
            double perpY = axisX;
            List<Point3d> points = new List<Point3d>();

            double tipOffset = definition.Width;
            double stemOffset = -tipOffset * StemOffsetRatio;
            double shoulderAlong = height * ShoulderAlongRatio;
            double centerAlong = height * CenterT;
            double upperStemAlong = height * UpperStemAlongRatio;
            double lowerStemAlong = height * LowerStemAlongRatio;
            double bottomShoulderAlong = height * (1.0 - ShoulderAlongRatio);

            LocalPoint top = new LocalPoint(0.0, 0.0);
            LocalPoint topShoulder = new LocalPoint(shoulderAlong, stemOffset);
            LocalPoint upperStem = new LocalPoint(upperStemAlong, stemOffset);
            LocalPoint tip = new LocalPoint(centerAlong, tipOffset);
            LocalPoint lowerStem = new LocalPoint(lowerStemAlong, stemOffset);
            LocalPoint bottomShoulder = new LocalPoint(bottomShoulderAlong, stemOffset);
            LocalPoint bottom = new LocalPoint(height, 0.0);

            // Endpoint shoulder: leave the endpoint on the axis, sweep to
            // the opposite-side shoulder, then keep a straight outer stem.
            AppendCubic(points, top,
                new LocalPoint(0.0, stemOffset * CubicKappa),
                new LocalPoint(shoulderAlong * (1.0 - CubicKappa),
                    stemOffset),
                topShoulder, axisX, axisY, perpX, perpY,
                definition, height, false);

            AddLocalPoint(points, upperStem, axisX, axisY, perpX, perpY,
                definition, height);

            // The two center curves meet at one shared vertex. Their control
            // points leave that vertex in different directions, so the
            // Polyline preserves a genuinely sharp folded corner rather
            // than drawing a rounded arc through it.
            double upperCenterSpan = centerAlong - upperStemAlong;
            double lowerCenterSpan = lowerStemAlong - centerAlong;
            AppendCubic(points, upperStem,
                new LocalPoint(upperStemAlong
                    + upperCenterSpan * (1.0 - CenterControlAlongRatio),
                    stemOffset),
                new LocalPoint(
                    centerAlong - upperCenterSpan * CenterControlAlongRatio,
                    tipOffset - (tipOffset - stemOffset)
                        * CenterTipControlOffsetRatio),
                tip, axisX, axisY, perpX, perpY,
                definition, height, true);
            AppendCubic(points, tip,
                new LocalPoint(
                    centerAlong + lowerCenterSpan * CenterControlAlongRatio,
                    tipOffset - (tipOffset - stemOffset)
                        * CenterTipControlOffsetRatio),
                new LocalPoint(lowerStemAlong
                    - lowerCenterSpan * (1.0 - CenterControlAlongRatio),
                    stemOffset),
                lowerStem, axisX, axisY, perpX, perpY,
                definition, height, true);

            AddLocalPoint(points, bottomShoulder, axisX, axisY, perpX, perpY,
                definition, height);

            // Mirror the endpoint shoulder at the bottom.
            AppendCubic(points, bottomShoulder,
                new LocalPoint(bottomShoulderAlong
                    + (height - bottomShoulderAlong) * CubicKappa,
                    stemOffset),
                new LocalPoint(height,
                    stemOffset * (1.0 - CubicKappa)),
                bottom, axisX, axisY, perpX, perpY,
                definition, height, true);

            // Keep the endpoints exact even if the shape table changes later.
            points[0] = definition.Top;
            points[points.Count - 1] = definition.Bottom;
            return points;
        }

        private static void AppendCubic(
            List<Point3d> points,
            LocalPoint p0,
            LocalPoint p1,
            LocalPoint p2,
            LocalPoint p3,
            double axisX,
            double axisY,
            double perpX,
            double perpY,
            PatBraceDefinition definition,
            double height,
            bool skipFirst)
        {
            int start = skipFirst ? 1 : 0;
            for (int i = start; i <= CurveSamples; i++)
            {
                double t = (double)i / (double)CurveSamples;
                double oneMinusT = 1.0 - t;
                double b0 = oneMinusT * oneMinusT * oneMinusT;
                double b1 = 3.0 * oneMinusT * oneMinusT * t;
                double b2 = 3.0 * oneMinusT * t * t;
                double b3 = t * t * t;
                LocalPoint point = new LocalPoint(
                    b0 * p0.Along + b1 * p1.Along
                        + b2 * p2.Along + b3 * p3.Along,
                    b0 * p0.Offset + b1 * p1.Offset
                        + b2 * p2.Offset + b3 * p3.Offset);
                AddLocalPoint(points, point, axisX, axisY, perpX, perpY,
                    definition, height);
            }
        }

        private static void AddLocalPoint(
            List<Point3d> points,
            LocalPoint point,
            double axisX,
            double axisY,
            double perpX,
            double perpY,
            PatBraceDefinition definition,
            double height)
        {
            double t = point.Along / height;
            double signedOffset = point.Offset * definition.Side;
            points.Add(new Point3d(
                definition.Top.X + axisX * point.Along
                    + perpX * signedOffset,
                definition.Top.Y + axisY * point.Along
                    + perpY * signedOffset,
                definition.Top.Z
                    + (definition.Bottom.Z - definition.Top.Z) * t));
        }
    }
}
