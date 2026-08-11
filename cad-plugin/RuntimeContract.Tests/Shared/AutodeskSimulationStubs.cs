using System;
using System.Collections;
using System.Collections.Generic;

namespace Autodesk.AutoCAD.Geometry
{
    public struct Point3d : IEquatable<Point3d>
    {
        public Point3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public bool Equals(Point3d other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is Point3d && Equals((Point3d)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                return (hash * 397) ^ Z.GetHashCode();
            }
        }

        public override string ToString()
        {
            return string.Format("({0},{1},{2})", X, Y, Z);
        }
    }
}

namespace Autodesk.AutoCAD.Runtime
{
    [Flags]
    public enum CommandFlags
    {
        Modal = 0,
        UsePickSet = 1,
        Redraw = 2
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class CommandMethodAttribute : Attribute
    {
        public CommandMethodAttribute(string name, CommandFlags flags)
        {
            Name = name;
            Flags = flags;
        }

        public string Name { get; private set; }
        public CommandFlags Flags { get; private set; }
    }
}

namespace Autodesk.AutoCAD.ApplicationServices
{
    using Autodesk.AutoCAD.DatabaseServices;
    using Autodesk.AutoCAD.EditorInput;

    public static class Application
    {
        public static DocumentManager DocumentManager { get; } = new DocumentManager();
    }

    public sealed class DocumentManager
    {
        public Document MdiActiveDocument { get; set; }
    }

    public sealed class Document
    {
        public string Name { get; set; } = "simulation.dwg";
        public Editor Editor { get; set; } = new Editor();
        public Database Database { get; set; } = new Database();

        public DocumentLock LockDocument()
        {
            return new DocumentLock();
        }
    }

    public sealed class DocumentLock : IDisposable
    {
        public void Dispose() { }
    }
}

namespace Autodesk.AutoCAD.EditorInput
{
    using Autodesk.AutoCAD.Geometry;

    public enum PromptStatus
    {
        OK,
        None,
        Cancel,
        Error,
        Keyword
    }

    public sealed class PromptPointOptions
    {
        public PromptPointOptions(string message)
        {
            Message = message;
        }

        public string Message { get; private set; }
        public Point3d BasePoint { get; set; }
        public bool UseBasePoint { get; set; }
        public bool AllowNone { get; set; }
    }

    public sealed class PromptStringOptions
    {
        public PromptStringOptions(string message)
        {
            Message = message;
        }

        public string Message { get; private set; }
        public bool AllowSpaces { get; set; }
    }

    public struct PromptPointResult
    {
        public PromptPointResult(PromptStatus status, Point3d value)
        {
            Status = status;
            Value = value;
        }

        public PromptStatus Status { get; private set; }
        public Point3d Value { get; private set; }
    }

    public struct PromptStringResult
    {
        public PromptStringResult(PromptStatus status, string value)
        {
            Status = status;
            StringResult = value;
        }

        public PromptStatus Status { get; private set; }
        public string StringResult { get; private set; }
    }

    public sealed class Editor
    {
        private readonly Queue<PromptPointResult> _points = new Queue<PromptPointResult>();
        private readonly Queue<PromptStringResult> _strings = new Queue<PromptStringResult>();

        public List<string> Messages { get; } = new List<string>();
        public List<string> PointPrompts { get; } = new List<string>();

        public void EnqueuePoint(PromptStatus status, Point3d value)
        {
            _points.Enqueue(new PromptPointResult(status, value));
        }

        public void EnqueueString(PromptStatus status, string value)
        {
            _strings.Enqueue(new PromptStringResult(status, value));
        }

        public PromptPointResult GetPoint(string message)
        {
            PointPrompts.Add(message);
            return DequeuePoint();
        }

        public PromptPointResult GetPoint(PromptPointOptions options)
        {
            PointPrompts.Add(options.Message);
            return DequeuePoint();
        }

        public PromptStringResult GetString(string message)
        {
            return DequeueString();
        }

        public PromptStringResult GetString(PromptStringOptions options)
        {
            return DequeueString();
        }

        public void WriteMessage(string message)
        {
            Messages.Add(message ?? "");
        }

        private PromptPointResult DequeuePoint()
        {
            if (_points.Count == 0)
                return new PromptPointResult(PromptStatus.Cancel, new Point3d());
            return _points.Dequeue();
        }

        private PromptStringResult DequeueString()
        {
            if (_strings.Count == 0)
                return new PromptStringResult(PromptStatus.Cancel, null);
            return _strings.Dequeue();
        }
    }
}

namespace Autodesk.AutoCAD.DatabaseServices
{
    using Autodesk.AutoCAD.Geometry;

    public enum OpenMode
    {
        ForRead,
        ForWrite
    }

    public enum AttachmentPoint
    {
        TopLeft,
        TopCenter,
        TopRight,
        MiddleLeft,
        MiddleCenter,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight,
        BaseLeft,
        BaseCenter,
        BaseRight
    }

    public enum ContentType
    {
        None,
        MTextContent
    }

    public enum LeaderType
    {
        StraightLeader,
        SplineLeader
    }

    public enum TextAngleType
    {
        InsertAngle,
        HorizontalAngle,
        AlwaysRightReadingAngle
    }

    public enum TextAttachmentDirection
    {
        AttachmentHorizontal,
        AttachmentVertical
    }

    public enum TextAttachmentType
    {
        AttachmentMiddle,
        AttachmentCenter
    }

    public struct ObjectId : IEquatable<ObjectId>
    {
        public ObjectId(int value)
        {
            Value = value;
        }

        public static ObjectId Null { get { return new ObjectId(0); } }
        public int Value { get; private set; }
        public bool IsNull { get { return Value == 0; } }

        public bool Equals(ObjectId other) { return Value == other.Value; }
        public override bool Equals(object obj) { return obj is ObjectId && Equals((ObjectId)obj); }
        public override int GetHashCode() { return Value; }
        public static bool operator ==(ObjectId left, ObjectId right) { return left.Equals(right); }
        public static bool operator !=(ObjectId left, ObjectId right) { return !left.Equals(right); }
        public override string ToString() { return Value.ToString(); }
    }

    public abstract class DBObject
    {
        internal Database Database { get; set; }
        public ObjectId ObjectId { get; internal set; }
        public ObjectId ExtensionDictionary { get; internal set; }
    }

    public sealed class DBDictionary : DBObject
    {
        private readonly Dictionary<string, ObjectId> _entries =
            new Dictionary<string, ObjectId>(StringComparer.Ordinal);

        public bool Contains(string name)
        {
            return _entries.ContainsKey(name);
        }

        public ObjectId GetAt(string name)
        {
            return _entries[name];
        }

        public ObjectId SetAt(string name, DBObject value)
        {
            if (value.ObjectId.IsNull)
                Database.AllocateId(value);
            _entries[name] = value.ObjectId;
            return value.ObjectId;
        }
    }

    public sealed class Xrecord : DBObject
    {
        public ResultBuffer Data { get; set; }
    }

    public struct TypedValue
    {
        public TypedValue(int typeCode, object value)
        {
            TypeCode = (short)typeCode;
            Value = value;
        }

        public short TypeCode { get; private set; }
        public object Value { get; private set; }
    }

    public sealed class ResultBuffer : IEnumerable<TypedValue>, IDisposable
    {
        private readonly TypedValue[] _values;

        public ResultBuffer(params TypedValue[] values)
        {
            _values = values ?? new TypedValue[0];
        }

        public IEnumerator<TypedValue> GetEnumerator()
        {
            return ((IEnumerable<TypedValue>)_values).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _values.GetEnumerator();
        }

        public void Dispose() { }
    }

    public class Entity : DBObject
    {
        public bool IsErased { get; private set; }

        public virtual void SetDatabaseDefaults(Database db)
        {
            Database = db;
        }

        public void CreateExtensionDictionary()
        {
            if (ExtensionDictionary.IsNull && Database != null)
                ExtensionDictionary = Database.EnsureExtensionDictionary(this);
        }

        public virtual void Erase(bool erasing)
        {
            IsErased = true;
            if (Database != null) Database.Trace.Add("erase:" + GetType().Name);
        }
    }

    public sealed class MText : Entity
    {
        public string Contents { get; set; } = "";
        public double TextHeight { get; set; }
        public Point3d Location { get; set; }
        public double Rotation { get; set; }
        public AttachmentPoint Attachment { get; set; }
        public ObjectId TextStyleId { get; set; }
    }

    public sealed class DBText : Entity
    {
        public string TextString { get; set; } = "";
    }

    public sealed class Leader : Entity
    {
        private readonly List<Point3d> _vertices = new List<Point3d>();
        private ObjectId _annotation;

        public IReadOnlyList<Point3d> Vertices { get { return _vertices; } }
        public int NumVertices { get { return _vertices.Count; } }
        public bool IsSplined { get; set; }
        public bool HasArrowHead { get; set; }
        public double Dimasz { get; set; }
        public ObjectId DimensionStyle { get; set; }

        public ObjectId Annotation
        {
            get { return _annotation; }
            set
            {
                if (value.IsNull || Database == null || !Database.ContainsCommittedOrPending(value))
                    throw new InvalidOperationException("Leader.Annotation must refer to an already appended annotation.");
                _annotation = value;
                Database.Trace.Add("leader.annotation");
            }
        }

        public void AppendVertex(Point3d point)
        {
            _vertices.Add(point);
            if (Database != null) Database.Trace.Add("leader.vertex");
        }

        public Point3d VertexAt(int index)
        {
            return _vertices[index];
        }

        public void SetVertexAt(int index, Point3d point)
        {
            _vertices[index] = point;
        }
    }

    public sealed class MLeader : Entity
    {
        private readonly List<Point3d> _vertices = new List<Point3d>();
        private MText _mtext;
        private ObjectId _style;
        private bool _leaderStarted;

        public ContentType ContentType { get; set; }
        public ObjectId MLeaderStyle
        {
            get { return _style; }
            set { _style = value; if (Database != null) Database.Trace.Add("mleader.style"); }
        }
        public LeaderType LeaderLineType { get; set; }
        public ObjectId ArrowSymbolId { get; set; }
        public double ArrowSize { get; set; }
        public double TextHeight { get; set; }
        public bool EnableDogleg { get; set; }
        public bool EnableLanding { get; set; }
        public bool ExtendLeaderToText { get; set; }
        public double DoglegLength { get; set; }
        public double LandingGap { get; set; }
        public TextAttachmentDirection TextAttachmentDirection { get; set; }
        public TextAttachmentType TextAttachmentType { get; set; }
        public TextAngleType TextAngleType { get; set; }
        public Point3d TextLocation { get; set; }
        public int LeaderLineCount { get { return _leaderStarted ? 1 : 0; } }
        public MText MText
        {
            get { return _mtext; }
            set { _mtext = value; if (Database != null) Database.Trace.Add("mleader.mtext"); }
        }
        public IReadOnlyList<Point3d> Vertices { get { return _vertices; } }

        public int AddLeaderLine(Point3d point)
        {
            if (ContentType != ContentType.MTextContent)
                throw new InvalidOperationException("MLeader content must be MTextContent before AddLeaderLine.");
            if (_mtext == null)
                throw new InvalidOperationException("MLeader MText must be attached before AddLeaderLine.");
            if (_style.IsNull)
                throw new InvalidOperationException("MLeader style must be attached before AddLeaderLine.");
            _leaderStarted = true;
            _vertices.Add(point);
            if (Database != null) Database.Trace.Add("mleader.add-leader-line");
            return 0;
        }

        public void AddLastVertex(int lineIndex, Point3d point)
        {
            if (!_leaderStarted || lineIndex != 0)
                throw new InvalidOperationException("MLeader vertex appended before a leader line exists.");
            _vertices.Add(point);
            if (Database != null) Database.Trace.Add("mleader.vertex");
        }

        public Point3d GetLastVertex(int lineIndex)
        {
            if (!_leaderStarted || lineIndex != 0 || _vertices.Count == 0)
                throw new InvalidOperationException("MLeader has no vertices for the requested leader line.");
            return _vertices[_vertices.Count - 1];
        }
    }

    public sealed class Database
    {
        private int _nextId = 10;
        private readonly Dictionary<int, DBObject> _objects = new Dictionary<int, DBObject>();
        private readonly List<Entity> _pending = new List<Entity>();

        public Database()
        {
            BlockTableId = new ObjectId(1);
            TextStyleTableId = new ObjectId(2);
            ModelSpaceId = new ObjectId(3);
            BlockTable = new BlockTable(this);
            ModelSpace = new BlockTableRecord(this);
            TextStyleTable = new TextStyleTable(this);
            Register(BlockTableId, BlockTable);
            Register(ModelSpaceId, ModelSpace);
            Register(TextStyleTableId, TextStyleTable);
            TransactionManager = new TransactionManager(this);
        }

        public ObjectId BlockTableId { get; private set; }
        public ObjectId ModelSpaceId { get; private set; }
        public ObjectId TextStyleTableId { get; private set; }
        public ObjectId Textstyle { get; set; }
        public TransactionManager TransactionManager { get; private set; }
        public BlockTable BlockTable { get; private set; }
        public BlockTableRecord ModelSpace { get; private set; }
        public TextStyleTable TextStyleTable { get; private set; }
        public List<string> Trace { get; } = new List<string>();
        public List<Entity> CommittedEntities { get; } = new List<Entity>();
        public bool FailOnCommit { get; set; }
        public bool NormalizeAttachmentOnFirstCommit { get; set; }
        private bool _attachmentWasNormalized;

        internal ObjectId AllocateId(DBObject obj)
        {
            ObjectId id = new ObjectId(_nextId++);
            obj.ObjectId = id;
            Register(id, obj);
            return id;
        }

        internal ObjectId EnsureExtensionDictionary(DBObject owner)
        {
            DBDictionary dictionary = new DBDictionary();
            AllocateId(dictionary);
            owner.ExtensionDictionary = dictionary.ObjectId;
            return dictionary.ObjectId;
        }

        internal void Register(ObjectId id, DBObject obj)
        {
            obj.ObjectId = id;
            obj.Database = this;
            _objects[id.Value] = obj;
        }

        internal DBObject Resolve(ObjectId id)
        {
            DBObject value;
            if (!_objects.TryGetValue(id.Value, out value))
                throw new KeyNotFoundException("Unknown simulation ObjectId " + id.Value);
            return value;
        }

        internal void AddPending(Entity entity)
        {
            if (entity.ObjectId.IsNull) AllocateId(entity);
            entity.Database = this;
            _pending.Add(entity);
            Trace.Add("append:" + entity.GetType().Name);
        }

        internal bool ContainsCommittedOrPending(ObjectId id)
        {
            if (id.IsNull) return false;
            foreach (Entity entity in _pending)
                if (entity.ObjectId == id) return true;
            foreach (Entity entity in CommittedEntities)
                if (entity.ObjectId == id) return true;
            return false;
        }

        internal void Commit(Transaction transaction)
        {
            if (FailOnCommit)
                throw new InvalidOperationException("Simulated transaction commit failure.");
            Trace.Add("transaction.commit");
            CommittedEntities.AddRange(_pending);
            if (NormalizeAttachmentOnFirstCommit && !_attachmentWasNormalized)
            {
                _attachmentWasNormalized = true;
                foreach (Entity entity in _pending)
                {
                    Leader leader = entity as Leader;
                    if (leader == null || leader.Vertices.Count == 0)
                        continue;

                    MText text = null;
                    foreach (Entity candidate in _pending)
                    {
                        text = candidate as MText;
                        if (text != null) break;
                    }
                    if (text == null)
                        continue;

                    text.Attachment = text.Attachment == AttachmentPoint.TopRight ||
                        text.Attachment == AttachmentPoint.MiddleRight ||
                        text.Attachment == AttachmentPoint.BottomRight
                        ? AttachmentPoint.BottomRight
                        : AttachmentPoint.BottomLeft;
                }
            }
            foreach (Entity entity in _pending)
                ModelSpace.AddCommitted(entity.ObjectId);
            _pending.Clear();
        }

        internal void Rollback(Transaction transaction)
        {
            Trace.Add("transaction.rollback");
            _pending.Clear();
        }
    }

    public sealed class TransactionManager
    {
        private readonly Database _database;
        internal TransactionManager(Database database) { _database = database; }
        public Transaction StartTransaction() { return new Transaction(_database); }
    }

    public sealed class Transaction : IDisposable
    {
        private readonly Database _database;
        private bool _committed;

        internal Transaction(Database database)
        {
            _database = database;
            _database.Trace.Add("transaction.begin");
        }

        public DBObject GetObject(ObjectId id, OpenMode mode)
        {
            return _database.Resolve(id);
        }

        public void AddNewlyCreatedDBObject(DBObject obj, bool add)
        {
            _database.Trace.Add("transaction.add:" + obj.GetType().Name);
        }

        public void Commit()
        {
            if (_committed) return;
            _database.Commit(this);
            _committed = true;
        }

        public void Dispose()
        {
            if (!_committed) _database.Rollback(this);
        }
    }

    public sealed class BlockTable : DBObject
    {
        private readonly Database _database;
        internal BlockTable(Database database) { _database = database; }
        public static string ModelSpace { get { return "*Model_Space"; } }
        public ObjectId this[string name] { get { return new ObjectId(3); } }
    }

    public sealed class BlockTableRecord : DBObject, IEnumerable<ObjectId>
    {
        private readonly Database _database;
        private readonly List<ObjectId> _entityIds = new List<ObjectId>();
        internal BlockTableRecord(Database database) { _database = database; }
        public static string ModelSpace { get { return "*Model_Space"; } }
        public void AppendEntity(Entity entity) { _database.AddPending(entity); }

        internal void AddCommitted(ObjectId id)
        {
            if (!_entityIds.Contains(id)) _entityIds.Add(id);
        }

        public IEnumerator<ObjectId> GetEnumerator()
        {
            return _entityIds.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public sealed class TextStyleTable : DBObject
    {
        private readonly Database _database;
        private readonly Dictionary<string, ObjectId> _styles = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
        internal TextStyleTable(Database database) { _database = database; }
        public bool Has(string name) { return _styles.ContainsKey(name); }
        public ObjectId this[string name] { get { return _styles[name]; } }
        public void UpgradeOpen() { }
        public ObjectId Add(TextStyleTableRecord record)
        {
            ObjectId id = _database.AllocateId(record);
            _styles[record.Name] = id;
            return id;
        }
    }

    public sealed class TextStyleTableRecord : DBObject
    {
        public string Name { get; set; }
        public string FileName { get; set; }
    }

    public sealed class DimStyleTableRecord : DBObject
    {
        public string Name { get; set; }
        public double Dimasz { get; set; }
    }
}
