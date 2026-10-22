using System;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PatentMarker.Commands;
using PatentMarker.RuntimeContractTests;
using Xunit;

namespace PatentMarker.RuntimeContract.Tests
{
    public sealed class PatNativeEditTests
    {
        private static void Tracking(bool start)
        {
            // The old production assembly has no startup hook: the identical
            // event scenario must remain executable and fail, not skip.
            Type type = typeof(PatMarkCommand).Assembly.GetType("PatentMarker.IO.PatNativeEditTracker");
            if (type != null) type.GetMethod(start ? "Start" : "Stop").Invoke(null, null);
        }

#if !SIM_LEADER
        [Fact]
        public void EquivalentDatabaseWrapperEvent_StillSynchronizesRecord()
        {
            using (SimulationFixture f = new SimulationFixture())
            {
                Application.DocumentManager.MdiActiveDocument=f.Document;
                IO.PatSettingsStore.Current.ThreePointMode=true;
                f.QueueThreePointAnnotation("901",new Point3d(0,0,0),new Point3d(20,10,0),new Point3d(40,10,0));new PatMarkCommand().Run();
                MLeader ml=Assert.IsType<MLeader>(f.Database.CommittedEntities[0]);Tracking(true);
                try
                {
                    f.Document.BeginCommand("GRIP_STRETCH");ml.TextLocation=new Point3d(60,20,0);
                    f.Database.NotifyModifiedFromEquivalentWrapper(ml);f.Document.EndCommand("GRIP_STRETCH");
                    using(Transaction tr=f.Database.TransactionManager.StartTransaction())
                    {bool arrow,spline;var chain=PatMLeaderCreator.ReadChain(ml,tr,out arrow,out spline);Assert.Equal(ml.TextLocation,chain[chain.Count-1]);}
                    Assert.True(f.Database.UndoRecording);
                }
                finally{Tracking(false);}
            }
        }

        [Fact]
        public void ImpossibleNativePick_RestoresOriginalAttachmentAndRecord()
        {
            using(SimulationFixture f=new SimulationFixture())
            {
                Application.DocumentManager.MdiActiveDocument=f.Document;IO.PatSettingsStore.Current.ThreePointMode=true;
                f.QueueThreePointAnnotation("901",new Point3d(0,0,0),new Point3d(20,10,0),new Point3d(40,10,0));new PatMarkCommand().Run();
                MLeader ml=Assert.IsType<MLeader>(f.Database.CommittedEntities[0]);Tracking(true);
                try
                {
                    f.Document.BeginCommand("GRIP_STRETCH");ml.SetFirstVertex(0,new Point3d(42,9,0));f.Database.NotifyModified(ml);f.Document.EndCommand("GRIP_STRETCH");
                    Assert.Equal(new Point3d(0,0,0),ml.GetVertex(0,0));Assert.Equal(new Point3d(40,10,0),ml.TextLocation);
                    using(Transaction tr=f.Database.TransactionManager.StartTransaction())
                    {bool arrow,spline;var chain=PatMLeaderCreator.ReadChain(ml,tr,out arrow,out spline);Assert.Equal(ml.GetVertex(0,0),chain[0]);}
                    Assert.True(f.Database.UndoRecording);
                }
                finally{Tracking(false);}
            }
        }

        [Fact]
        public void CancelledGripAtOriginalPosition_PreservesRecordedChain()
        {
            using(SimulationFixture f=new SimulationFixture())
            {
                Application.DocumentManager.MdiActiveDocument=f.Document;IO.PatSettingsStore.Current.ThreePointMode=true;
                f.QueueThreePointAnnotation("901",new Point3d(0,0,0),new Point3d(20,10,0),new Point3d(40,10,0));new PatMarkCommand().Run();
                MLeader ml=Assert.IsType<MLeader>(f.Database.CommittedEntities[0]);Tracking(true);
                try
                {
                    f.Document.BeginCommand("GRIP_STRETCH");ml.TextLocation=new Point3d(60,20,0);f.Database.NotifyModified(ml);
                    ml.TextLocation=new Point3d(40,10,0);f.Database.NotifyModified(ml);f.Document.CancelCommand("GRIP_STRETCH");
                    using(Transaction tr=f.Database.TransactionManager.StartTransaction())
                    {bool arrow,spline;var chain=PatMLeaderCreator.ReadChain(ml,tr,out arrow,out spline);Assert.Equal(new Point3d(40,10,0),chain[chain.Count-1]);}
                    Assert.True(f.Database.UndoRecording);
                }
                finally{Tracking(false);}
            }
        }

        [Fact]
        public void StoppedTracker_DoesNotHandleLaterNativeEdits()
        {
            using(SimulationFixture f=new SimulationFixture())
            {
                Application.DocumentManager.MdiActiveDocument=f.Document;IO.PatSettingsStore.Current.ThreePointMode=true;
                f.QueueThreePointAnnotation("901",new Point3d(0,0,0),new Point3d(20,10,0),new Point3d(40,10,0));new PatMarkCommand().Run();
                MLeader ml=Assert.IsType<MLeader>(f.Database.CommittedEntities[0]);Tracking(true);Tracking(false);
                f.Document.BeginCommand("MOVE");ml.TextLocation=new Point3d(60,20,0);f.Database.NotifyModified(ml);f.Document.EndCommand("MOVE");
                using(Transaction tr=f.Database.TransactionManager.StartTransaction())
                {bool arrow,spline;var chain=PatMLeaderCreator.ReadChain(ml,tr,out arrow,out spline);Assert.Equal(new Point3d(40,10,0),chain[chain.Count-1]);}
            }
        }

        [Fact]
        public void NativeTextGrip_SynchronizesOnlyTheEditedDocument()
        {
            using (SimulationFixture a = new SimulationFixture())
            using (SimulationFixture b = new SimulationFixture())
            {
                Application.DocumentManager.MdiActiveDocument = a.Document;
                IO.PatSettingsStore.Current.ThreePointMode=true;
                a.QueueThreePointAnnotation("901", new Point3d(0,0,0),new Point3d(20,10,0),new Point3d(40,10,0));
                IO.RuntimeHost.SetActiveDocumentOverride(delegate { return a.Document; });
                new PatMarkCommand().Run();
                MLeader ml = Assert.IsType<MLeader>(a.Database.CommittedEntities[0]);
                Application.DocumentManager.MdiActiveDocument = b.Document;
                Tracking(true);
                try
                {
                    a.Document.BeginCommand("GRIP_STRETCH");
                    ml.TextLocation = new Point3d(55,25,0);
                    a.Database.NotifyModified(ml);
                    a.Document.EndCommand("GRIP_STRETCH");
                    using (Transaction tr = a.Database.TransactionManager.StartTransaction())
                    {
                        bool arrow, spline;
                        List<Point3d> chain = PatMLeaderCreator.ReadChain(ml,tr,out arrow,out spline);
                        Assert.Equal(ml.TextLocation,chain[chain.Count-1]);
                    }
                    Assert.Empty(b.Database.CommittedEntities);
                    Assert.True(a.Database.UndoRecording);
                }
                finally { Tracking(false); }
            }
        }

        [Fact]
        public void NativeUndoRedo_ResyncsWithoutChangingUndoRecordingState()
        {
            using (SimulationFixture f = new SimulationFixture())
            {
                Application.DocumentManager.MdiActiveDocument=f.Document;
                IO.PatSettingsStore.Current.ThreePointMode=true;
                f.QueueThreePointAnnotation("901",new Point3d(0,0,0),new Point3d(20,10,0),new Point3d(40,10,0));
                new PatMarkCommand().Run();
                MLeader ml=Assert.IsType<MLeader>(f.Database.CommittedEntities[0]);
                Tracking(true);
                try
                {
                    foreach(string command in new[]{"GRIP_STRETCH","U","REDO"})
                    {
                        f.Document.BeginCommand(command);
                        ml.TextLocation=command=="U"?new Point3d(40,10,0):new Point3d(60,20,0);
                        f.Database.NotifyModified(ml);
                        f.Document.EndCommand(command);
                        using(Transaction tr=f.Database.TransactionManager.StartTransaction())
                        {
                            bool arrow,spline;var chain=PatMLeaderCreator.ReadChain(ml,tr,out arrow,out spline);
                            Assert.Equal(ml.TextLocation,chain[chain.Count-1]);
                        }
                        Assert.True(f.Database.UndoRecording);
                    }
                    f.Database.DisableUndoRecording(true);
                    f.Document.BeginCommand("MOVE");ml.TextLocation=new Point3d(70,20,0);f.Database.NotifyModified(ml);f.Document.EndCommand("MOVE");
                    Assert.False(f.Database.UndoRecording);
                }
                finally { Tracking(false); }
            }
        }

        [Fact]
        public void VerificationAndUntrackedMutation_DoNotHealTamperedRecords()
        {
            using(SimulationFixture f=new SimulationFixture())
            {
                Application.DocumentManager.MdiActiveDocument=f.Document;
                IO.PatSettingsStore.Current.ThreePointMode=true;
                f.QueueThreePointAnnotation("901",new Point3d(0,0,0),new Point3d(20,10,0),new Point3d(40,10,0));new PatMarkCommand().Run();
                MLeader ml=Assert.IsType<MLeader>(f.Database.CommittedEntities[0]);Tracking(true);
                try
                {
                    foreach(string command in new[]{"PATGEOMVERIFY","PATMLVERIFY","UNRELATED"})
                    {
                        f.Document.BeginCommand(command);ml.TextLocation=new Point3d(70,20,0);f.Database.NotifyModified(ml);f.Document.EndCommand(command);
                        using(Transaction tr=f.Database.TransactionManager.StartTransaction())
                        {bool arrow,spline;var chain=PatMLeaderCreator.ReadChain(ml,tr,out arrow,out spline);Assert.NotEqual(ml.TextLocation,chain[chain.Count-1]);}
                    }
                }
                finally {Tracking(false);}
            }
        }
#endif

        [Fact]
        public void PickInsideText_IsRejectedWithoutPartialAnnotation()
        {
            using(SimulationFixture f=new SimulationFixture())
            {
                IO.PatSettingsStore.Current.ThreePointMode=true;
#if SIM_LEADER
                Point3d attach=new Point3d(60,8,0),bend=new Point3d(80,20,0);
#else
                Point3d attach=new Point3d(67,8,0),bend=new Point3d(80,10,0);
#endif
                f.QueueThreePointAnnotation("901",attach,bend,new Point3d(65,10,0));new PatMarkCommand().Run();
                Assert.Empty(f.Database.CommittedEntities);
            }
        }

        [Fact]
        public void CrossingPickedChain_IsRoutedOutsideTextKeepingPickedBend()
        {
            foreach(bool spline in new[]{false,true})
            using(SimulationFixture f=new SimulationFixture())
            {
                IO.PatSettingsStore.Current.IsSplined=spline;
                IO.PatSettingsStore.Current.ThreePointMode=true;
                Point3d attach=new Point3d(50,0,0),bend=new Point3d(80,10,0),pick=new Point3d(65,10,0);
#if SIM_LEADER
                bend=new Point3d(80,20,0);
#endif
                f.QueueThreePointAnnotation("901",attach,bend,pick);new PatMarkCommand().Run();
                var vertices=new List<Point3d>();MText text;
#if SIM_LEADER
                text=Assert.IsType<MText>(f.Database.CommittedEntities[0]);Leader leader=Assert.IsType<Leader>(f.Database.CommittedEntities[1]);
                for(int i=0;i<leader.NumVertices;i++)vertices.Add(leader.VertexAt(i));
#else
                MLeader ml=Assert.IsType<MLeader>(f.Database.CommittedEntities[0]);text=ml.MText;
                for(int i=0;i<ml.VerticesCount(0);i++)vertices.Add(ml.GetVertex(0,i));
#endif
                Assert.Equal(pick,text.Location);Assert.Contains(bend,vertices);
                for(int i=1;i<vertices.Count;i++)
                    for(int s=1;s<100;s++)
                    {
                        double t=s/100.0;Point3d a=vertices[i-1],b=vertices[i];double x=a.X+(b.X-a.X)*t,y=a.Y+(b.Y-a.Y)*t;
                        Extents3d box=text.GeometricExtents;
                        Assert.False(x>box.MinPoint.X&&x<box.MaxPoint.X&&y>box.MinPoint.Y&&y<box.MaxPoint.Y,"Picked segment enters actual text box");
                    }
            }
        }
    }
}
