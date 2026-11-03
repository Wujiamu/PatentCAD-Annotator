using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using PatentMarker.Commands;
using Xunit;

namespace PatentMarker.RuntimeContractTests
{
    public sealed class G03RoutingTests
    {
        [Fact]
        public void G03_R001_ThreePointSteepAnglesPreservePicks()
        {
            foreach (double h in new[] { 1.0, 3.5, 10.0 })
            foreach (double angle in new[] { 75.0, 105.0, 255.0, 285.0 })
            foreach (bool spline in new[] { false, true })
                Run(h, angle, spline, 0);
        }

        [Fact]
        public void G03_R001_FreeModeSteepAnglesPreservePicks()
        {
            foreach (double h in new[] { 1.0, 3.5, 10.0 })
            foreach (double angle in new[] { 75.0, 105.0, 255.0, 285.0 })
            foreach (bool spline in new[] { false, true })
                Run(h, angle, spline, 1);
        }

        [Fact]
        public void G03_R001_FreeModeEnterUsesFinalPickAsText()
        {
            foreach (double h in new[] { 1.0, 3.5, 10.0 })
            foreach (double angle in new[] { 75.0, 105.0, 255.0, 285.0 })
            foreach (bool spline in new[] { false, true })
                Run(h, angle, spline, 2);
        }

        private static void Run(double h, double angle, bool spline, int mode)
        {
            double r = angle * Math.PI / 180.0;
            Point3d attach = new Point3d(Math.Cos(r) * 150, Math.Sin(r) * 150, 0);
            Point3d bend = new Point3d(Math.Cos(r) * 100, Math.Sin(r) * 100, 0);
            Point3d textPoint = new Point3d(0, 0, 0);
            using (SimulationFixture fixture = new SimulationFixture())
            {
                IO.PatSettingsStore.Current.TextHeight = h;
                IO.PatSettingsStore.Current.IsSplined = spline;
                IO.PatSettingsStore.Current.ThreePointMode = mode == 0;
                Palette.PatPaletteCommand.PendingNumber = "123456789";
                fixture.Editor.EnqueuePoint(PromptStatus.OK, attach);
                fixture.Editor.EnqueuePoint(PromptStatus.OK, bend);
                if (mode == 0)
                    fixture.Editor.EnqueuePoint(PromptStatus.OK, textPoint);
                else
                {
                    if (mode == 2)
                        fixture.Editor.EnqueuePoint(PromptStatus.OK, textPoint);
                    fixture.Editor.EnqueuePoint(PromptStatus.None, new Point3d());
                    fixture.Editor.EnqueuePoint(mode == 2 ? PromptStatus.None : PromptStatus.OK, textPoint);
                }
                fixture.Editor.EnqueuePoint(PromptStatus.Cancel, new Point3d());
                new PatMarkCommand().Run();
#if SIM_LEADER
                Assert.Equal(2, fixture.Database.CommittedEntities.Count);
                MText text = Assert.IsType<MText>(fixture.Database.CommittedEntities[0]);
                Leader leader = Assert.IsType<Leader>(fixture.Database.CommittedEntities[1]);
                Assert.Contains(attach, leader.Vertices);
                Assert.Contains(bend, leader.Vertices);
                Assert.Equal(textPoint, text.Location);
                Assert.Equal(spline, leader.IsSplined);
                var bounds = text.GeometricExtents;
                for (int i = 1; i < leader.NumVertices; i++)
                    Assert.False(PatTextPathRouting.Intersects(leader.VertexAt(i - 1), leader.VertexAt(i), bounds.MinPoint, bounds.MaxPoint));
#else
                Assert.Single(fixture.Database.CommittedEntities);
                MLeader leader = Assert.IsType<MLeader>(fixture.Database.CommittedEntities[0]);
                Assert.Contains(attach, leader.Vertices);
                Assert.Contains(bend, leader.Vertices);
                Assert.Equal(textPoint, leader.TextLocation);
                Assert.False(leader.EnableDogleg);
                Assert.False(leader.EnableLanding);
                Assert.False(leader.ExtendLeaderToText);
                Assert.Equal(spline ? LeaderType.SplineLeader : LeaderType.StraightLeader, leader.LeaderLineType);
                var bounds = leader.MText.GeometricExtents;
                for (int i = 1; i < leader.VerticesCount(0); i++)
                    Assert.False(PatTextPathRouting.Intersects(leader.GetVertex(0, i - 1), leader.GetVertex(0, i), bounds.MinPoint, bounds.MaxPoint));
                using (Transaction tr = fixture.Database.TransactionManager.StartTransaction())
                {
                    bool arrow, recordedSpline;
                    var chain = PatMLeaderCreator.ReadChain(leader, tr, out arrow, out recordedSpline);
                    Assert.Equal(leader.VerticesCount(0), chain.Count);
                    for (int i = 0; i < chain.Count - 1; i++) Assert.Equal(leader.GetVertex(0, i), chain[i]);
                    Assert.Equal(textPoint, chain[chain.Count - 1]);
                    Assert.Equal(spline, recordedSpline);
                }
#endif
            }
        }
    }
}
