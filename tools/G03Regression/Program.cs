using System;
using System.Collections;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using PatentMarker.Commands;

namespace Autodesk.AutoCAD.Geometry
{
    public struct Point3d : IEquatable<Point3d>
    {
        public double X, Y, Z;
        public Point3d(double x,double y,double z) { X=x; Y=y; Z=z; }
        public bool Equals(Point3d p) { return X==p.X && Y==p.Y && Z==p.Z; }
        public override string ToString() { return string.Format("({0},{1},{2})",X,Y,Z); }
        public static Vector3d operator -(Point3d a,Point3d b) { return new Vector3d(a.X-b.X,a.Y-b.Y,a.Z-b.Z); }
        public static Point3d operator +(Point3d a,Vector3d b) { return new Point3d(a.X+b.X,a.Y+b.Y,a.Z+b.Z); }
    }
    public struct Vector3d
    {
        public double X,Y,Z;
        public Vector3d(double x,double y,double z) { X=x; Y=y; Z=z; }
        public double Length { get { return Math.Sqrt(X*X+Y*Y+Z*Z); } }
        public Vector3d GetNormal() { return new Vector3d(X/Length,Y/Length,Z/Length); }
        public double DotProduct(Vector3d b) { return X*b.X+Y*b.Y+Z*b.Z; }
        public static Vector3d operator *(Vector3d a,double b) { return new Vector3d(a.X*b,a.Y*b,a.Z*b); }
    }
}
namespace Autodesk.AutoCAD.DatabaseServices
{
    public enum OpenMode { ForRead, ForWrite }
    public enum AttachmentPoint { TopLeft, TopRight, BottomLeft, BottomRight }
    public struct ObjectId
    {
        public int Id;
        public ObjectId(int id) { Id=id; }
        public bool IsNull { get { return Id==0; } }
        public static ObjectId Null { get { return new ObjectId(0); } }
    }
    public class Entity { public ObjectId ObjectId; }
    public struct Extents3d
    {
        public Point3d MinPoint, MaxPoint;
        public Extents3d(Point3d lo,Point3d hi) { MinPoint=lo; MaxPoint=hi; }
    }
    public class MText : Entity
    {
        private Point3d location;
        public Action<Point3d> LocationChanged;
        public Point3d Location { get { return location; } set { location=value; if(LocationChanged!=null) LocationChanged(value); } }
        public double TextHeight;
        private double actualWidth;
        public bool FailActualWidth;
        public int FailAttachmentCount;
        public double ActualWidth { get { if(FailActualWidth) throw new InvalidOperationException("width unavailable"); return actualWidth; } set { actualWidth=value; } }
        public string Contents="";
        public Extents3d GeometricExtents
        {
            get
            {
                bool right=Attachment==AttachmentPoint.TopRight || Attachment==AttachmentPoint.BottomRight;
                bool top=Attachment==AttachmentPoint.TopLeft || Attachment==AttachmentPoint.TopRight;
                return new Extents3d(new Point3d(Location.X-(right?actualWidth:0), Location.Y-(top?TextHeight:0),Location.Z),
                    new Point3d(Location.X+(right?0:actualWidth),Location.Y+(top?0:TextHeight),Location.Z));
            }
        }
        private AttachmentPoint attachment;
        public AttachmentPoint Attachment
        {
            get { return attachment; }
            set { if(FailAttachmentCount>0) { FailAttachmentCount--; throw new InvalidOperationException("attachment failure"); } attachment=value; Location=new Point3d(999,999,999); }
        }
    }
    public class Leader : Entity
    {
        public ObjectId Annotation, ExtensionDictionary;
        public readonly List<Point3d> Points=new List<Point3d>();
        public int FailAtSet = -1;
        public int NumVertices { get { return Points.Count; } }
        public Point3d VertexAt(int n) { return Points[n]; }
        public void SetVertexAt(int n,Point3d p) { if(n==FailAtSet) throw new InvalidOperationException("vertex failure"); Points[n]=p; }
        public void AppendVertex(Point3d p) { Points.Add(p); }
    }
    public class MLeader : Entity
    {
        public MText MText;
        public double TextHeight;
        public Point3d TextLocation;
        public int VerticesCount(int n) { return 2; }
        public Point3d GetVertex(int a,int b) { return new Point3d(); }
    }
    public class DBDictionary
    {
        public ObjectId Entry;
        public bool Contains(string key) { return !Entry.IsNull; }
        public ObjectId GetAt(string key) { return Entry; }
    }
    public struct TypedValue
    {
        public int TypeCode;
        public object Value;
        public TypedValue(int code,object val) { TypeCode=code; Value=val; }
    }
    public class ResultBuffer : IEnumerable<TypedValue>, IDisposable
    {
        public readonly List<TypedValue> Values=new List<TypedValue>();
        public IEnumerator<TypedValue> GetEnumerator() { return Values.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        public void Dispose() {}
    }
    public class Xrecord { public ResultBuffer Data; }
    public class Database
    {
        public TransactionManager TransactionManager;
        public Database(Transaction tr) { TransactionManager=new TransactionManager(tr); }
    }
    public class TransactionManager
    {
        readonly Transaction tr;
        public TransactionManager(Transaction t) { tr=t; }
        public Transaction StartTransaction() { return tr; }
    }
    public class Transaction : IDisposable
    {
        readonly Dictionary<int,object> objects=new Dictionary<int,object>();
        int next=1;
        public readonly Dictionary<int,int> Reads=new Dictionary<int,int>();
        public ObjectId Add(object value)
        {
            ObjectId id=new ObjectId(next++);
            objects.Add(id.Id,value);
            Entity ent=value as Entity;
            if(ent!=null) ent.ObjectId=id;
            return id;
        }
        public object GetObject(ObjectId id,OpenMode mode) { if(!Reads.ContainsKey(id.Id)) Reads[id.Id]=0; Reads[id.Id]++; return objects[id.Id]; }
        public void Commit() { }
        public void Dispose() { }
    }
}
namespace Autodesk.AutoCAD.EditorInput
{
    public class SelectedObject { public ObjectId ObjectId; }
    public class SelectionSet : List<SelectedObject> {}
    public enum PromptStatus { OK, Cancel }
    public class PromptPointOptions
    {
        public PromptPointOptions(string text) { }
        public Point3d BasePoint;
        public bool UseBasePoint;
    }
    public class PromptPointResult
    {
        public PromptStatus Status=PromptStatus.OK;
        public Point3d Value;
        public PromptPointResult(Point3d p) { Value=p; }
    }
    public class Keywords
    {
        public string Default;
        public void Add(string s) { }
    }
    public class PromptKeywordOptions
    {
        public Keywords Keywords=new Keywords();
        public PromptKeywordOptions(string text) { }
    }
    public class PromptResult { public PromptStatus Status=PromptStatus.OK; public string StringResult; }
    public class Editor
    {
        public readonly Queue<Point3d> Points=new Queue<Point3d>();
        public string Keyword;
        public PromptPointResult GetPoint(string text) { return new PromptPointResult(Points.Dequeue()); }
        public PromptPointResult GetPoint(PromptPointOptions o) { return new PromptPointResult(Points.Dequeue()); }
        public PromptPointResult GetCorner(string text,Point3d p) { return new PromptPointResult(Points.Dequeue()); }
        public PromptResult GetKeywords(PromptKeywordOptions options) { return new PromptResult { StringResult=Keyword }; }
        public void WriteMessage(string s) { }
    }
}
namespace Autodesk.AutoCAD.ApplicationServices
{
    public class Document { public Database Database; }
}
namespace PatentMarker.I18n
{
    public static class Strings
    {
        public const string PatAlign_Skipped="skipped {0}", PatAlign_PromptLineP1="line1", PatAlign_PromptLineP2="line2", PatAlign_Degenerate="degenerate", PatAlign_PromptFrameCorner1="corner1", PatAlign_PromptFrameCorner2="corner2", PatAlign_SidePrompt="side", PatAlign_KwLeft="left", PatAlign_KwRight="right", PatAlign_KwTop="top", PatAlign_KwBottom="bottom", PatAlign_Result="aligned {0} failed {1}", PatAlign_ResultOverflow="aligned {0} failed {1} columns {2}";
    }
}
namespace PatentMarker
{
    public static class PatentMarkerApp
    {
        public static readonly List<string> Logs=new List<string>();
        public static void RawLog(string message) { Logs.Add(message); }
    }
}
namespace PatentMarker.IO
{
    public static class RuntimeHost { public static Autodesk.AutoCAD.ApplicationServices.Document ActiveDocument; }
    public class Settings { public double MarginToFrame=5; }
    public static class PatSettingsStore { public static Settings Current=new Settings(); }
    public static class PatEntityHelper
    {
        public static bool IsPatEntity(Entity e,Transaction tr) { return e is Leader; }
        public static bool IsStandaloneText(MText t,Transaction tr) { return false; }
        public static Point3d GetLeaderTextPos(Leader l,Transaction tr)
        {
            ObjectId id=PatLeaderTextAttachment.GetAnnotationId(l,tr);
            return id.IsNull ? l.VertexAt(l.NumVertices-1) : ((MText)tr.GetObject(id,OpenMode.ForRead)).Location;
        }
    }
}
namespace PatentMarker.Commands
{
    public static class PatMLeaderCreator
    {
        public static bool IsPatMLeader(MLeader ml,Transaction tr) { return false; }
        public static void RefreshTextAttachment(MLeader ml,Point3d p,Point3d q) {}
        public static void UpdateChainTextPoint(MLeader ml,Transaction tr,Point3d p) {}
    }
}
class Program
{
    static int failures, checks;
    static void Check(bool condition,string detail)
    {
        checks++;
        if(!condition) { failures++; Console.WriteLine("FAIL "+detail); }
    }
    static bool Near(double a,double b) { return Math.Abs(a-b)<1e-7; }
    static void Try(string name,Action action)
    {
        try { action(); }
        catch(Exception ex) { failures++; Console.WriteLine("FAIL "+name+": "+ex.GetType().Name+" "+ex.Message); }
    }
    static (Point3d lo,Point3d hi) Bounds(Point3d previous,double h)
    {
        AttachmentPoint a=PatLeaderTextAttachment.Get(previous,new Point3d(0,0,0));
        bool right=a==AttachmentPoint.TopRight || a==AttachmentPoint.BottomRight;
        bool top=a==AttachmentPoint.TopLeft || a==AttachmentPoint.TopRight;
        return (new Point3d(right?-2*h:0,top?-h:0,0),new Point3d(right?0:2*h,top?0:h,0));
    }
    static void Route()
    {
        foreach(double h in new[]{1.0,3.5,10.0})
        foreach(double distance in new[]{100.0,10000.0})
        foreach(double degrees in new[]{0.0,45.0,75.0,90.0,105.0,135.0,180.0,225.0,255.0,270.0,285.0,315.0})
        {
            double radians=degrees*Math.PI/180.0;
            Point3d a=new Point3d(distance*Math.Cos(radians),distance*Math.Sin(radians),0);
            Point3d end=PatLeaderTextAttachment.Retract(a,new Point3d(0,0,0),h);
            var box=Bounds(a,h);
            string label=string.Format("R-001 h={0} d={1} angle={2}",h,distance,degrees);
            Try(label,delegate {
                var path=PatTextPathRouting.Route(new[]{a,end},box.lo,box.hi,h);
                Check(path[0].Equals(a) && path[path.Count-1].Equals(end),label+" preserves vertices");
                for(int i=1;i<path.Count;i++)
                    Check(!PatTextPathRouting.Intersects(path[i-1],path[i],box.lo,box.hi),label+" avoids actual text");
            });
        }
        var min=new Point3d(0,0,0); var max=new Point3d(10,10,0);
        Try("R-001 rejects actual user pick",delegate {
            bool rejected=false;
            try { PatTextPathRouting.Route(new[]{new Point3d(-10,-10,0),new Point3d(5,5,0),new Point3d(-1,0,0)},min,max,3.5); }
            catch(ArgumentException) { rejected=true; }
            Check(rejected,"R-001 user pick inside text must be rejected");
        });
        Try("R-001 crossing text",delegate {
            Point3d pick=new Point3d(20,20,0), generated=new Point3d(-0.3,-0.3,0);
            var path=PatTextPathRouting.Route(new[]{pick,generated},min,max,3.5);
            Check(path.Count>2,"R-001 crossing text detoured");
            for(int i=1;i<path.Count;i++) Check(!PatTextPathRouting.Intersects(path[i-1],path[i],min,max),"R-001 detour avoids actual text");
        });
    }
    static (Transaction tr,SelectionSet selection,Leader leader,MText text) Create(bool native,double height,double width)
    {
        Transaction tr=new Transaction();
        MText text=new MText { TextHeight=height, ActualWidth=width, Contents="123456789ABC" };
        text.Attachment=AttachmentPoint.BottomLeft;
        text.Location=new Point3d(10,10,0);
        ObjectId textId=tr.Add(text);
        Leader leader=new Leader();
        leader.Points.Add(new Point3d(-20,-20,0));
        leader.Points.Add(new Point3d(0,0,0));
        leader.Points.Add(new Point3d(8,8,0));
        ObjectId leaderId=tr.Add(leader);
        if(native)
        {
            leader.Annotation=textId;
            text.LocationChanged=delegate(Point3d p) {
                leader.SetVertexAt(leader.NumVertices-1,
                    PatLeaderTextAttachment.Retract(leader.VertexAt(leader.NumVertices-2),p,text.TextHeight));
            };
            text.Location=text.Location;
        }
        else
        {
            ResultBuffer buffer=new ResultBuffer();
            buffer.Values.Add(new TypedValue(340,textId));
            Xrecord record=new Xrecord { Data=buffer };
            ObjectId recordId=tr.Add(record);
            leader.ExtensionDictionary=tr.Add(new DBDictionary { Entry=recordId });
        }
        SelectionSet selection=new SelectionSet { new SelectedObject { ObjectId=leaderId } };
        return (tr,selection,leader,text);
    }
    static object Collect(bool modern,Transaction tr,SelectionSet s)
    { return modern ? Align10.Collect(tr,s) : Align07.Collect(tr,s); }
    static double Height(bool modern,object t)
    { return modern ? Align10.Height(t) : Align07.Height(t); }
    static double Width(bool modern,Transaction tr,object t)
    { return modern ? Align10.Width(tr,t) : Align07.Width(tr,t); }
    static bool Move(bool modern,Transaction tr,object t,Point3d p)
    { return modern ? Align10.Move(tr,t,p) : Align07.Move(tr,t,p); }
    static void Alignment()
    {
        foreach(bool modern in new[]{false,true})
        foreach(bool native in new[]{false,true})
        foreach(Point3d position in new[]{new Point3d(-10,-10,0),new Point3d(10,-10,0),new Point3d(-10,10,0),new Point3d(10,10,0),new Point3d(0,10,0),new Point3d(0,-10,0),new Point3d(-10,0,0),new Point3d(10,0,0)})
        {
            string label="R-015 modern="+modern+" native="+native+" target="+position;
            Try(label,delegate {
                var v=Create(native,3.5,20);
                if(native) v.leader.SetVertexAt(0,new Point3d(-3*position.X,-3*position.Y,0));
                object t=Collect(modern,v.tr,v.selection);
                Check(Move(modern,v.tr,t,position),label+" move");
                var expected=PatLeaderTextAttachment.Get(new Point3d(0,0,0),position);
                Check(v.text.Attachment==expected,label+" attachment expected "+expected+" actual "+v.text.Attachment);
                Check(v.text.Location.Equals(position),label+" preserves location");
                if(!native)
                {
                    Point3d end=PatLeaderTextAttachment.Retract(new Point3d(0,0,0),position,3.5);
                    Point3d actual=v.leader.VertexAt(v.leader.NumVertices-1);
                    Check(Near(actual.X,end.X)&&Near(actual.Y,end.Y),label+" endpoint");
                }
            });
        }
    }
    static void AlignmentCrossing()
    {
        foreach(bool modern in new[]{false,true})
        {
            string label="R-015 crossing modern="+modern;
            Try(label,delegate {
                var v=Create(false,3.5,20);
                object t=Collect(modern,v.tr,v.selection);
                Point3d picked=v.leader.VertexAt(0), stable=v.leader.VertexAt(1);
                Point3d target=new Point3d(-10,-10,0);
                Check(Move(modern,v.tr,t,target),label+" move succeeds");
                Check(v.leader.Points.Contains(picked) && v.leader.Points.Contains(stable),label+" preserves picks");
                Check(v.text.Location.Equals(target),label+" preserves location");
                var box=v.text.GeometricExtents;
                for(int i=1;i<v.leader.NumVertices;i++)
                    Check(!PatTextPathRouting.Intersects(v.leader.VertexAt(i-1),v.leader.VertexAt(i),box.MinPoint,box.MaxPoint),label+" avoids text");
                Point3d previous=v.leader.VertexAt(v.leader.NumVertices-2);
                Point3d expected=PatLeaderTextAttachment.Retract(previous,target,3.5);
                Point3d actual=v.leader.VertexAt(v.leader.NumVertices-1);
                Check(Near(actual.X,expected.X)&&Near(actual.Y,expected.Y),label+" terminal gap");
            });
            Try("R-015 native crossing modern="+modern,delegate {
                var v=Create(true,3.5,20);
                object t=Collect(modern,v.tr,v.selection);
                Point3d oldLocation=v.text.Location, oldEndpoint=v.leader.VertexAt(2);
                AttachmentPoint oldAttachment=v.text.Attachment;
                bool ok=Move(modern,v.tr,t,new Point3d(-10,-10,0));
                Check(!ok,"R-015 native crossing is rejected");
                Check(v.text.Location.Equals(oldLocation) && v.text.Attachment==oldAttachment && v.leader.VertexAt(2).Equals(oldEndpoint),"R-015 native rejected move restores association and location");
            });
            Try("R-015 impossible pick modern="+modern,delegate {
                var v=Create(false,3.5,20);
                v.leader.SetVertexAt(0,new Point3d(-15,-11,0));
                object t=Collect(modern,v.tr,v.selection);
                Point3d oldLocation=v.text.Location, oldEndpoint=v.leader.VertexAt(2);
                AttachmentPoint oldAttachment=v.text.Attachment;
                bool ok=Move(modern,v.tr,t,new Point3d(-10,-10,0));
                Check(!ok,"R-015 reject pick inside moved text");
                Check(v.text.Location.Equals(oldLocation) && v.text.Attachment==oldAttachment && v.leader.VertexAt(2).Equals(oldEndpoint),"R-015 rejected move leaves original entity unchanged");
            });
        }
    }
    static (SelectionSet selection,MText first,MText second) LayoutItems(Transaction tr)
    {
        SelectionSet selection=new SelectionSet();
        MText[] texts=new MText[2];
        for(int i=0;i<2;i++)
        {
            MText text=new MText { TextHeight=i==0?10:1, ActualWidth=i==0?60:2, Contents=i==0?"1234567890":"123" };
            text.Attachment=AttachmentPoint.BottomLeft;
            text.Location=new Point3d(10+10*i,50-10*i,0);
            ObjectId textId=tr.Add(text); texts[i]=text;
            Leader leader=new Leader();
            leader.Points.Add(new Point3d(10+10*i,120,0));
            leader.Points.Add(new Point3d(10+10*i,80,0));
            leader.Points.Add(new Point3d(10+10*i,51-10*i,0));
            ObjectId leaderId=tr.Add(leader);
            ResultBuffer buffer=new ResultBuffer();buffer.Values.Add(new TypedValue(340,textId));
            ObjectId rec=tr.Add(new Xrecord { Data=buffer });
            leader.ExtensionDictionary=tr.Add(new DBDictionary { Entry=rec });
            selection.Add(new SelectedObject { ObjectId=leaderId });
        }
        return (selection,texts[0],texts[1]);
    }
    static void Layout(bool frame)
    {
        foreach(bool modern in new[]{false,true})
        {
            Try("R-020 "+(frame?"frame":"line")+" modern="+modern,delegate {
                Transaction tr=new Transaction();
                var items=LayoutItems(tr);
                PatentMarker.IO.RuntimeHost.ActiveDocument=new Autodesk.AutoCAD.ApplicationServices.Document { Database=new Database(tr) };
                Editor ed=new Editor();
                ed.Points.Enqueue(new Point3d(0,0,0));
                ed.Points.Enqueue(frame?new Point3d(100,20,0):new Point3d(40,0,0));
                ed.Keyword=PatentMarker.I18n.Strings.PatAlign_KwLeft;
                if(modern) { if(frame) new Align10().DoFrame(ed,items.selection); else new Align10().DoLine(ed,items.selection); }
                else { if(frame) new Align07().DoFrame(ed,items.selection); else new Align07().DoLine(ed,items.selection); }
                if(frame)
                {
                    Check(Near(items.first.Location.X,-5) && Near(items.first.Location.Y,12.5),"R-020 frame first column uses actual height");
                    Check(Near(items.second.Location.X,-85) && Near(items.second.Location.Y,12.5),"R-020 frame next column clears prior width plus gap");
                }
                else
                {
                    Check(Near(items.first.Location.X,30) && Near(items.first.Location.Y,0),"R-020 line first uses actual width");
                    Check(Near(items.second.Location.X,76) && Near(items.second.Location.Y,0),"R-020 line second uses actual widths and largest height");
                }
            });
        }
    }
    static void FrameMultipleWidths()
    {
        foreach(bool modern in new[]{false,true})
        foreach(bool right in new[]{false,true})
        {
            string label="R-020 multi-column modern="+modern+" right="+right;
            Try(label,delegate {
                Transaction tr=new Transaction();SelectionSet selection=new SelectionSet();
                MText[] texts=new MText[3];double[] widths={60,2,30};double[] heights={10,1,3.5};
                for(int i=0;i<3;i++)
                {
                    MText text=new MText { TextHeight=heights[i], ActualWidth=widths[i], Contents="123456789" };
                    text.Attachment=AttachmentPoint.BottomLeft;text.Location=new Point3d(10+i*10,50-i*10,0);
                    ObjectId textId=tr.Add(text);texts[i]=text;
                    Leader leader=new Leader();leader.Points.Add(new Point3d(10+i*10,120,0));leader.Points.Add(new Point3d(10+i*10,80,0));leader.Points.Add(new Point3d(10+i*10,51-i*10,0));
                    ObjectId leaderId=tr.Add(leader);ResultBuffer data=new ResultBuffer();data.Values.Add(new TypedValue(340,textId));
                    leader.ExtensionDictionary=tr.Add(new DBDictionary { Entry=tr.Add(new Xrecord { Data=data }) });
                    selection.Add(new SelectedObject { ObjectId=leaderId });
                }
                PatentMarker.IO.RuntimeHost.ActiveDocument=new Autodesk.AutoCAD.ApplicationServices.Document { Database=new Database(tr) };
                Editor ed=new Editor();ed.Points.Enqueue(new Point3d(0,0,0));ed.Points.Enqueue(new Point3d(100,20,0));
                ed.Keyword=right?PatentMarker.I18n.Strings.PatAlign_KwRight:PatentMarker.I18n.Strings.PatAlign_KwLeft;
                if(modern) new Align10().DoFrame(ed,selection); else new Align07().DoFrame(ed,selection);
                double[] offsets={0,80,102};
                for(int i=0;i<3;i++)
                {
                    double x=right?105+offsets[i]:-5-offsets[i];
                    Check(Near(texts[i].Location.X,x),label+" column="+i+" expected "+x+" actual "+texts[i].Location.X);
                    Check(Near(texts[i].Location.Y,12.5),label+" same row "+i);
                }
            });
        }
    }
    static void FailureSemantics()
    {
        foreach(bool modern in new[]{false,true})
        {
            Try("R-015 geometry failure modern="+modern,delegate {
                var v=Create(false,3.5,20);
                object t=Collect(modern,v.tr,v.selection);
                v.leader.FailAtSet=1;
                bool threw=false;
                try { Move(modern,v.tr,t,new Point3d(-10,-10,0)); }
                catch(InvalidOperationException) { threw=true; }
                Check(threw,"R-015 geometry mutation error escapes caller transaction");
            });
            Try("R-015 restore failure modern="+modern,delegate {
                var v=Create(false,3.5,20);
                object t=Collect(modern,v.tr,v.selection);
                v.text.FailAttachmentCount=2;
                bool threw=false;
                try { Move(modern,v.tr,t,new Point3d(-10,-10,0)); }
                catch(InvalidOperationException) { threw=true; }
                Check(threw,"R-015 failed text restoration escapes caller transaction");
            });
            Try("R-015 incomplete leader modern="+modern,delegate {
                var v=Create(false,3.5,20);
                object t=Collect(modern,v.tr,v.selection);
                v.leader.Points.RemoveAt(2);v.leader.Points.RemoveAt(1);
                Point3d old=v.text.Location;
                Check(!Move(modern,v.tr,t,new Point3d(-10,-10,0)),"R-015 incomplete leader rejected");
                Check(v.text.Location.Equals(old),"R-015 incomplete leader unchanged");
            });
        }
    }
    static void Measurement()
    {
        foreach(bool modern in new[]{false,true})
        foreach(bool native in new[]{false,true})
        foreach(double height in new[]{1.0,3.5,10.0})
        {
            string label="R-020 modern="+modern+" native="+native+" h="+height;
            Try(label,delegate {
                var v=Create(native,height,37.5);
                object t=Collect(modern,v.tr,v.selection);
                Check(Near(Height(modern,t),height),label+" height expected "+height+" got "+Height(modern,t));
                double width=Width(modern,v.tr,t);
                Check(Near(width,37.5),label+" width expected 37.5 got "+width);
            });
        }
    }
    static void MeasurementFallbacks()
    {
        foreach(bool modern in new[]{false,true})
        foreach(bool native in new[]{false,true})
        {
            Try("R-020 fallback modern="+modern+" native="+native,delegate {
                var v=Create(native,10,0);
                v.text.FailActualWidth=true;
                PatentMarker.PatentMarkerApp.Logs.Clear();
                object t=Collect(modern,v.tr,v.selection);
                int reads=v.tr.Reads[v.text.ObjectId.Id];
                double width=Width(modern,v.tr,t);
                Check(Near(Height(modern,t),10),"R-020 available height retained on width failure");
                Check(Near(width,84),"R-020 width estimated from actual text on read failure");
                Check(v.tr.Reads[v.text.ObjectId.Id]==reads,"R-020 width calculation does not reopen linked text");
                Check(PatentMarker.PatentMarkerApp.Logs.Count>0,"R-020 fallback diagnostic recorded");
            });
        }
    }
    static int Main(string[] args)
    {
        string which=args.Length==0?"all":args[0];
        if(which=="all"||which=="route") Route();
        if(which=="all"||which=="align") Alignment();
        if(which=="all"||which=="measure") Measurement();
        if(which=="all"||which=="crossing") AlignmentCrossing();
        if(which=="all"||which=="failure") FailureSemantics();
        if(which=="all"||which=="fallback") MeasurementFallbacks();
        if(which=="all"||which=="line") Layout(false);
        if(which=="all"||which=="frame") Layout(true);
        if(which=="all"||which=="frame_multi") FrameMultipleWidths();
        if(which!="all"&&which!="route"&&which!="align"&&which!="measure"&&which!="crossing"&&which!="failure"&&which!="fallback"&&which!="line"&&which!="frame"&&which!="frame_multi") return 2;
        Console.WriteLine("SUMMARY selector="+which+" checks="+checks+" failed="+failures);
        return failures==0 && checks>0 ? 0 : 1;
    }
}
