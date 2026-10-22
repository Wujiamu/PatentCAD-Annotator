using System;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AppAcad=Autodesk.AutoCAD.ApplicationServices.Application;

namespace PatentMarker.IO
{
    /// <summary>One bounded per-document observer, no timers/polling. Native
    /// edit notifications collect ids only; writes happen after the command.
    /// Verification commands and arbitrary DB mutations never resync records.</summary>
    public static class PatNativeEditTracker
    {
        private sealed class State
        {
            public Document Document;
            public Database Database;
            public Stack<string> Commands=new Stack<string>();
            public Dictionary<ObjectId,bool> Changed=new Dictionary<ObjectId,bool>();
            public bool Busy;
        }
        private static readonly Dictionary<Document,State> States=new Dictionary<Document,State>();
        private static bool started;
        public static void Start()
        {
            if(started)return;started=true;
            AppAcad.DocumentManager.DocumentCreated+=Created;
            AppAcad.DocumentManager.DocumentToBeDestroyed+=Destroyed;
            foreach(Document doc in AppAcad.DocumentManager)Attach(doc);
            PatentMarkerApp.RawLog("Native annotation tracker START; documents="+States.Count);
        }
        public static void Stop()
        {
            if(!started)return;started=false;
            AppAcad.DocumentManager.DocumentCreated-=Created;
            AppAcad.DocumentManager.DocumentToBeDestroyed-=Destroyed;
            foreach(Document doc in new List<Document>(States.Keys))Detach(doc);
        }
        private static void Created(object sender,DocumentCollectionEventArgs e){if(e!=null)Attach(e.Document);}
        private static void Destroyed(object sender,DocumentCollectionEventArgs e){if(e!=null)Detach(e.Document);}
        private static void Attach(Document doc)
        {
            if(doc==null||States.ContainsKey(doc))return;
            Database database=doc.Database;
            States.Add(doc,new State{Document=doc,Database=database});
            doc.CommandWillStart+=Beginning;doc.CommandEnded+=Finished;
            doc.CommandCancelled+=Finished;doc.CommandFailed+=Finished;
            database.ObjectModified+=Modified;
        }
        private static void Detach(Document doc)
        {
            if(doc==null||!States.ContainsKey(doc))return;
            doc.CommandWillStart-=Beginning;doc.CommandEnded-=Finished;
            doc.CommandCancelled-=Finished;doc.CommandFailed-=Finished;
            States[doc].Database.ObjectModified-=Modified;States.Remove(doc);
        }
        private static string Name(string value)
        {return (value??"").Trim().TrimStart('_','.','\'','*').ToUpperInvariant();}
        private static bool Native(string name)
        {
            return name=="GRIP_STRETCH"||name=="GRIP_MOVE"||name=="STRETCH"||name=="MOVE"||
                name=="U"||name=="UNDO"||name=="REDO"||name=="MREDO";
        }
        private static void Beginning(object sender,CommandEventArgs e)
        {State s;Document doc=sender as Document;if(doc!=null&&e!=null&&States.TryGetValue(doc,out s))s.Commands.Push(Name(e.GlobalCommandName));}
        private static void Modified(object sender,ObjectEventArgs e)
        {
            if(e==null)return;Entity entity=e.DBObject as Entity;if(entity==null||entity.ObjectId.IsNull)return;
            foreach(State s in States.Values)
                // AutoCAD can create distinct managed wrappers for the same
                // native database. DisposableWrapper.Equals compares native
                // identity; ReferenceEquals silently drops real grip events.
                if(!s.Busy&&object.Equals(s.Database,sender)&&s.Commands.Count>0&&Native(s.Commands.Peek()))
                    s.Changed[entity.ObjectId]=true;
        }
        private static void Finished(object sender,CommandEventArgs e)
        {
            Document doc=sender as Document;State s;
            if(doc==null||e==null||!States.TryGetValue(doc,out s)||s.Busy)return;
            string name=Name(e.GlobalCommandName);
            if(s.Commands.Count>0)s.Commands.Pop();
            if(!Native(name)||s.Commands.Count>0||s.Changed.Count==0)return;
            ObjectId[] ids=new ObjectId[s.Changed.Count];s.Changed.Keys.CopyTo(ids,0);s.Changed.Clear();s.Busy=true;
            bool undo=s.Database.UndoRecording;
            try
            {
                using(DocumentLock docLock=doc.LockDocument())
                {
                    // These are derived records/attachment geometry. Do not
                    // add an extra undo step or enable a disabled recorder.
                    s.Database.DisableUndoRecording(true);
                    using(Transaction tr=s.Database.TransactionManager.StartTransaction())
                    {
                        foreach(ObjectId id in ids)
                        {
                            Entity ent=tr.GetObject(id,OpenMode.ForRead) as Entity;
                            if(ent==null||ent.IsErased)continue;
                            if(PatEntityHelper.IsPatMLeader(ent,tr))
                            {
                                ent=tr.GetObject(id,OpenMode.ForWrite) as Entity;
                                Type creator=typeof(PatNativeEditTracker).Assembly.GetType("PatentMarker.Commands.PatMLeaderCreator");
                                MethodInfo sync=creator==null?null:creator.GetMethod("SynchronizeNativeEdit");
                                if(sync!=null)
                                {
                                    object result=sync.Invoke(null,new object[]{ent,tr});
                                    if(result is int&&(int)result<0)
                                        doc.Editor.WriteMessage("\n文字与拾取点重叠，已还原本次标注编辑；请调整文字或拐点位置。\n");
                                }
                            }
                        }
                        tr.Commit();
                    }
                }
                PatentMarkerApp.RawLog("Native annotation tracker synced command="+name+" targets="+ids.Length+" document="+doc.Name);
            }
            catch(System.Exception ex){PatentMarkerApp.RawLog("Native annotation tracker FAIL: "+ex.Message);}
            finally
            {
                try{s.Database.DisableUndoRecording(!undo);}
                catch(System.Exception ex){PatentMarkerApp.RawLog("Native annotation undo-state restore FAIL: "+ex.Message);}
                s.Busy=false;
            }
        }
    }
}
