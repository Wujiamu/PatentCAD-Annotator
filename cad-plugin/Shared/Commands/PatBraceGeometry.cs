using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;

namespace PatentMarker.Commands
{
    /// <summary>
    /// Parametric definition of one two-dimensional curly brace.
    /// Top and Bottom are the two endpoints. Width is the distance from the
    /// endpoint axis to the outer lobe, and Side is the signed side of that
    /// axis (+1 or -1).
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

        // Endpoints are at x=0. The two lobes extend toward x=1 and the
        // center pinch returns toward the endpoint axis.
        private static readonly double[] ShapeX = new double[]
        {
            0.00, 0.18, 0.42, 0.70, 0.92, 1.00, 0.98, 0.82,
            0.54, 0.20, 0.54, 0.82, 0.98, 1.00, 0.92, 0.70,
            0.42, 0.18, 0.00
        };

        private static readonly double[] ShapeT = new double[]
        {
            0.00, 0.02, 0.07, 0.15, 0.25, 0.33, 0.39, 0.44,
            0.48, 0.50, 0.52, 0.56, 0.61, 0.67, 0.75, 0.85,
            0.93, 0.98, 1.00
        };

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
            double signedWidth = definition.Width * definition.Side;
            List<Point3d> points = new List<Point3d>();

            for (int i = 0; i < ShapeX.Length; i++)
            {
                double along = height * ShapeT[i];
                double offset = signedWidth * ShapeX[i];
                points.Add(new Point3d(
                    definition.Top.X + axisX * along + perpX * offset,
                    definition.Top.Y + axisY * along + perpY * offset,
                    definition.Top.Z + (definition.Bottom.Z - definition.Top.Z) * ShapeT[i]));
            }

            // Keep the endpoints exact even if the shape table changes later.
            points[0] = definition.Top;
            points[points.Count - 1] = definition.Bottom;
            return points;
        }
    }
}
