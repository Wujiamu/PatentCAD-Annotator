using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using PatentMarker.Commands;
using Xunit;

namespace PatentMarker.RuntimeContractTests
{
    public sealed class PatMarkSimulationTests
    {
        [Fact]
        public void ThreePointInputCreatesOneEditionSpecificAnnotation()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                IO.PatSettingsStore.Current.ThreePointMode = true;
                IO.PatSettingsStore.Current.HasArrowHead = true;
                IO.PatSettingsStore.Current.ArrowSize = 3.25;
                IO.PatSettingsStore.Current.TextHeight = 4.25;
                IO.PatSettingsStore.Current.IsSplined = false;
                fixture.QueueThreePointAnnotation("1342A", new Point3d(1, 2, 0), new Point3d(3, 4, 0), new Point3d(5, 6, 0));

                new PatMarkCommand().Run();

                Assert.Contains("transaction.commit", fixture.Database.Trace);
#if SIM_LEADER
                Assert.Equal(2, fixture.Database.CommittedEntities.Count);
                Leader leader = Assert.IsType<Leader>(fixture.Database.CommittedEntities[1]);
                Assert.Equal(2, leader.Vertices.Count);
                Assert.True(leader.HasArrowHead);
                Assert.False(leader.IsSplined);
                Assert.Equal(3.25, leader.Dimasz);
                Assert.False(leader.Annotation.IsNull);
                MText annotation = Assert.IsType<MText>(fixture.Database.CommittedEntities[0]);
                Assert.Equal("1342A", annotation.Contents);
                Assert.Equal(4.25, annotation.TextHeight);
#else
                Assert.Single(fixture.Database.CommittedEntities);
                MLeader leader = Assert.IsType<MLeader>(fixture.Database.CommittedEntities[0]);
                Assert.Equal(ContentType.MTextContent, leader.ContentType);
                Assert.False(leader.MLeaderStyle.IsNull);
                Assert.Equal(2, leader.Vertices.Count);
                Assert.Equal(LeaderType.StraightLeader, leader.LeaderLineType);
                Assert.Equal(3.25, leader.ArrowSize);
                Assert.Equal(4.25, leader.TextHeight);
                Assert.False(leader.EnableDogleg);
                Assert.False(leader.EnableLanding);
                Assert.False(leader.ExtendLeaderToText);
                Assert.Equal(TextAttachmentDirection.AttachmentVertical, leader.TextAttachmentDirection);
                Assert.Equal(TextAttachmentType.AttachmentCenter, leader.TextAttachmentType);
                Assert.Equal(TextAngleType.HorizontalAngle, leader.TextAngleType);
                Assert.Equal(1, leader.LeaderLineCount);
                Assert.Equal(new Point3d(3, 4, 0), leader.GetLastVertex(0));
                Assert.Equal(new Point3d(5, 6, 0), leader.TextLocation);
                Assert.NotNull(leader.MText);
                Assert.Equal("1342A", leader.MText.Contents);
                Assert.Equal(0.0, leader.MText.Rotation);
#endif
            }
        }

        [Fact]
        public void TextToTheRightUsesMiddleLeftAttachment()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                IO.PatSettingsStore.Current.ThreePointMode = true;
                fixture.QueueThreePointAnnotation("right", new Point3d(1, 1, 0),
                    new Point3d(5, 1, 0), new Point3d(8, 3, 0));

                new PatMarkCommand().Run();

#if SIM_LEADER
                MText annotation = Assert.IsType<MText>(fixture.Database.CommittedEntities[0]);
                Assert.Equal(AttachmentPoint.MiddleLeft, annotation.Attachment);
#endif
            }
        }

        [Fact]
        public void TextToTheLeftUsesMiddleRightAttachment()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                IO.PatSettingsStore.Current.ThreePointMode = true;
                fixture.QueueThreePointAnnotation("left", new Point3d(8, 1, 0),
                    new Point3d(5, 1, 0), new Point3d(2, 3, 0));

                new PatMarkCommand().Run();

#if SIM_LEADER
                MText annotation = Assert.IsType<MText>(fixture.Database.CommittedEntities[0]);
                Assert.Equal(AttachmentPoint.MiddleRight, annotation.Attachment);
#endif
            }
        }

        [Fact]
        public void CancellationBeforeTextDoesNotCommitPartialAnnotation()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                IO.PatSettingsStore.Current.ThreePointMode = true;
                Palette.PatPaletteCommand.PendingNumber = "200";
                fixture.Editor.EnqueuePoint(PromptStatus.OK, new Point3d(1, 2, 0));
                fixture.Editor.EnqueuePoint(PromptStatus.Cancel, new Point3d());
                fixture.Editor.EnqueuePoint(PromptStatus.Cancel, new Point3d());

                new PatMarkCommand().Run();

                Assert.Empty(fixture.Database.CommittedEntities);
                Assert.DoesNotContain("transaction.commit", fixture.Database.Trace);
            }
        }

        [Fact]
        public void FreeModeUsesLastDoglegWhenTextPointIsNone()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                fixture.QueueFreeModeAnnotation("88", new Point3d(1, 1, 0), new Point3d(2, 2, 0));

                new PatMarkCommand().Run();

#if SIM_LEADER
                Assert.Equal(2, fixture.Database.CommittedEntities.Count);
#else
                Assert.Single(fixture.Database.CommittedEntities);
#endif
#if SIM_LEADER
                MText annotation = Assert.IsType<MText>(fixture.Database.CommittedEntities[0]);
                Assert.Equal(new Point3d(2, 2, 0), annotation.Location);
#else
                MLeader leader = Assert.IsType<MLeader>(fixture.Database.CommittedEntities[0]);
                Assert.Equal(new Point3d(2, 2, 0), leader.TextLocation);
#endif
            }
        }

        [Fact]
        public void TransactionFailureRollsBackTheSimulatedEntitySet()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                fixture.Database.FailOnCommit = true;
                IO.PatSettingsStore.Current.ThreePointMode = true;
                fixture.QueueThreePointAnnotation("rollback", new Point3d(1, 1, 0), new Point3d(2, 2, 0), new Point3d(3, 3, 0));

                new PatMarkCommand().Run();

                Assert.Empty(fixture.Database.CommittedEntities);
                Assert.Contains("transaction.rollback", fixture.Database.Trace);
            }
        }

        [Fact]
        public void DrawingSettingsRemainIsolatedAcrossHostSwitches()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                Document second = new Document
                {
                    Name = "D:\\other\\drawing-a.dwg",
                    Editor = new Editor(),
                    Database = new Database()
                };

                IO.PatSettingsStore.Activate(fixture.Document.Name);
                IO.PatSettingsStore.Current.HasArrowHead = true;

                IO.RuntimeHost.SetActiveDocumentOverride(delegate { return second; });
                IO.PatSettingsStore.Activate(second.Name);
                Assert.False(IO.PatSettingsStore.Current.HasArrowHead);

                IO.RuntimeHost.SetActiveDocumentOverride(delegate { return fixture.Document; });
                IO.PatSettingsStore.Activate(fixture.Document.Name);
                Assert.True(IO.PatSettingsStore.Current.HasArrowHead);
            }
        }
    }
}
