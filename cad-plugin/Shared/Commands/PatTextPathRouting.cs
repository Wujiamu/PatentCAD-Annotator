using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace PatentMarker.Commands
{
    /// <summary>Minimal deterministic detours around the actual text box.
    /// Original picks remain vertices. A pick inside the text is impossible
    /// to preserve without crossing it, and is rejected before commit.</summary>
    public static class PatTextPathRouting
    {
        public static List<Point3d> Route(IList<Point3d> picks,
            Point3d minimum, Point3d maximum, double height)
        {
            if (picks == null || picks.Count < 2 || height <= 0 ||
                double.IsNaN(height) || double.IsInfinity(height))
                throw new ArgumentException("Invalid annotation path.");
            double px = height * 0.2;
            double py = Math.Max(0.8, height * 0.4);
            Point3d lo = new Point3d(minimum.X-px,minimum.Y-py,minimum.Z);
            Point3d hi = new Point3d(maximum.X+px,maximum.Y+py,maximum.Z);
            List<Point3d> result = new List<Point3d>();
            result.Add(picks[0]);
            foreach (Point3d p in picks)
                if (Inside(p,minimum,maximum))
                    throw new ArgumentException("引线拾取点位于文字内部，请调整拐点或文字位置。");
            for(int i=1;i<picks.Count;i++)
            {
                Point3d a=picks[i-1], b=picks[i];
                if(!Intersects(a,b,lo,hi)) { result.Add(b); continue; }
                // Four-corner visibility graph: six nodes, bounded work per
                // segment, no dependence on drawing size or sampling order.
                double cx=Math.Max(1.0,height),cy=Math.Max(1.0,height);
                Point3d[] nodes=new Point3d[]{a,b,
                    new Point3d(lo.X-cx,lo.Y-cy,a.Z),new Point3d(hi.X+cx,lo.Y-cy,a.Z),
                    new Point3d(lo.X-cx,hi.Y+cy,a.Z),new Point3d(hi.X+cx,hi.Y+cy,a.Z)};
                double[] cost=new double[6];int[] parent=new int[6];bool[] done=new bool[6];
                for(int n=0;n<6;n++){cost[n]=double.PositiveInfinity;parent[n]=-1;}cost[0]=0;
                for(int n=0;n<6;n++)
                {
                    int best=-1;for(int j=0;j<6;j++)if(!done[j]&&(best<0||cost[j]<cost[best]))best=j;
                    if(best<0||double.IsInfinity(cost[best]))break;done[best]=true;if(best==1)break;
                    for(int j=0;j<6;j++)
                    {
                        if(done[j]||Intersects(nodes[best],nodes[j],lo,hi))continue;
                        double d=Distance(nodes[best],nodes[j])+cost[best];
                        if(d<cost[j]){cost[j]=d;parent[j]=best;}
                    }
                }
                if(parent[1]<0)throw new ArgumentException("引线拐点过于靠近文字，请调整拾取点。");
                List<Point3d> path=new List<Point3d>();int k=1;
                while(k!=0){path.Add(nodes[k]);k=parent[k];}path.Reverse();result.AddRange(path);
            }
            return result;
        }

        public static bool Intersects(Point3d a,Point3d b,Point3d lo,Point3d hi)
        {
            double t0=0,t1=1;
            if(!Slab(a.X,b.X-a.X,lo.X,hi.X,ref t0,ref t1)||
                !Slab(a.Y,b.Y-a.Y,lo.Y,hi.Y,ref t0,ref t1))return false;
            return t1-t0>1e-10;
        }
        private static bool Slab(double p,double d,double lo,double hi,ref double t0,ref double t1)
        {
            if(Math.Abs(d)<1e-12)return p>lo+1e-9&&p<hi-1e-9;
            double a=(lo-p)/d,b=(hi-p)/d;if(a>b){double swap=a;a=b;b=swap;}
            t0=Math.Max(t0,a);t1=Math.Min(t1,b);return t0<t1-1e-10;
        }
        private static bool Inside(Point3d p,Point3d lo,Point3d hi)
        {return p.X>lo.X+1e-9&&p.X<hi.X-1e-9&&p.Y>lo.Y+1e-9&&p.Y<hi.Y-1e-9;}
        private static double Distance(Point3d a,Point3d b)
        {double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;return Math.Sqrt(x*x+y*y+z*z);}
    }
}
