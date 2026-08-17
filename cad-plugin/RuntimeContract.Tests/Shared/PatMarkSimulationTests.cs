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
                Assert.Equal(3, leader.Vertices.Count);
                // 引线末顶点从文字点缩进 0.4×字高（字高 4.25 → gap=1.7），不直接触及文字。
                Point3d tpt = leader.VertexAt(2);
                double ex = tpt.X - 5.0, ey = tpt.Y - 6.0;
                Assert.True(Math.Abs(Math.Sqrt(ex * ex + ey * ey) - 1.7) < 0.01,
                    "leader endpoint should be 1.7 from text point, got " + tpt);
                Assert.True(leader.HasArrowHead);
                Assert.False(leader.IsSplined);
                Assert.Equal(3.25, leader.Dimasz);
                Assert.True(leader.Annotation.IsNull);
                MText annotation = Assert.IsType<MText>(fixture.Database.CommittedEntities[0]);
                Assert.Equal("1342A", annotation.Contents);
                Assert.Equal(4.25, annotation.TextHeight);
#else
                Assert.Single(fixture.Database.CommittedEntities);
                MLeader leader = Assert.IsType<MLeader>(fixture.Database.CommittedEntities[0]);
                Assert.Equal(ContentType.MTextContent, leader.ContentType);
                Assert.False(leader.MLeaderStyle.IsNull);
                Assert.Equal(3, leader.Vertices.Count);
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
                // 末顶点从文字点缩进 0.4×字高（gap=1.7），不直接触及文字。
                Point3d mle = leader.GetLastVertex(0);
                double mx = mle.X - 5.0, my = mle.Y - 6.0;
                Assert.True(Math.Abs(Math.Sqrt(mx * mx + my * my) - 1.7) < 0.01,
                    "MLeader endpoint should be 1.7 from text, got " + mle);
                Assert.Equal(new Point3d(5, 6, 0), leader.TextLocation);
                Assert.NotNull(leader.MText);
                Assert.Equal("1342A", leader.MText.Contents);
                Assert.Equal(0.0, leader.MText.Rotation);
#endif
            }
        }

        [Fact]
        public void LowerLeftLeaderUsesBottomLeftAttachment()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                IO.PatSettingsStore.Current.ThreePointMode = true;
                fixture.QueueThreePointAnnotation("right", new Point3d(1, 1, 0),
                    new Point3d(5, 1, 0), new Point3d(8, 3, 0));

                new PatMarkCommand().Run();

#if SIM_LEADER
                MText annotation = Assert.IsType<MText>(fixture.Database.CommittedEntities[0]);
                Assert.Equal(AttachmentPoint.BottomLeft, annotation.Attachment);
#endif
            }
        }

        [Fact]
        public void LeaderOffCreatesStandaloneUnderlinedText()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                IO.PatSettingsStore.Current.HasLeader = false;
                IO.PatSettingsStore.Current.UnderlineText = true;
                Palette.PatPaletteCommand.PendingNumber = "COMP-A";
                fixture.Editor.EnqueuePoint(PromptStatus.OK, new Point3d(7, 8, 0));
                fixture.Editor.EnqueuePoint(PromptStatus.Cancel, new Point3d());

                new PatMarkCommand().Run();

                Assert.Single(fixture.Database.CommittedEntities);
                MText text = Assert.IsType<MText>(fixture.Database.CommittedEntities[0]);
                Assert.Equal("\\LCOMP-A\\l", text.Contents);
                Assert.Equal(AttachmentPoint.MiddleCenter, text.Attachment);
                Assert.Equal(new Point3d(7, 8, 0), text.Location);
                using (Transaction tr = fixture.Database.TransactionManager.StartTransaction())
                    Assert.True(IO.PatEntityHelper.IsStandaloneText(text, tr));
            }
        }

        [Fact]
        public void UnderlineSwitchAlsoFormatsLeaderText()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                IO.PatSettingsStore.Current.UnderlineText = true;
                IO.PatSettingsStore.Current.ThreePointMode = true;
                fixture.QueueThreePointAnnotation("1342B", new Point3d(1, 2, 0),
                    new Point3d(3, 4, 0), new Point3d(5, 6, 0));

                new PatMarkCommand().Run();

                MText text = Assert.IsType<MText>(fixture.Database.CommittedEntities[0]);
                Assert.Equal("\\L1342B\\l", text.Contents);
                Assert.Equal("1342B", IO.PatEntityHelper.GetTextNumber(text));
            }
        }

        [Fact]
        public void LowerRightLeaderUsesBottomRightAttachment()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                IO.PatSettingsStore.Current.ThreePointMode = true;
                fixture.QueueThreePointAnnotation("left", new Point3d(8, 1, 0),
                    new Point3d(5, 1, 0), new Point3d(2, 3, 0));

                new PatMarkCommand().Run();

#if SIM_LEADER
                MText annotation = Assert.IsType<MText>(fixture.Database.CommittedEntities[0]);
                Assert.Equal(AttachmentPoint.BottomRight, annotation.Attachment);
#endif
            }
        }

        [Fact]
        public void UpperLeftLastLeaderPointUsesTopLeftAttachment()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                IO.PatSettingsStore.Current.ThreePointMode = true;
                fixture.QueueThreePointAnnotation("upper-left", new Point3d(1, 1, 0),
                    new Point3d(5, 6, 0), new Point3d(8, 3, 0));

                new PatMarkCommand().Run();

#if SIM_LEADER
                MText annotation = Assert.IsType<MText>(fixture.Database.CommittedEntities[0]);
                Assert.Equal(AttachmentPoint.TopLeft, annotation.Attachment);
#endif
            }
        }

        [Fact]
        public void UpperRightLastLeaderPointUsesTopRightAttachment()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                IO.PatSettingsStore.Current.ThreePointMode = true;
                fixture.QueueThreePointAnnotation("upper-right", new Point3d(8, 1, 0),
                    new Point3d(5, 6, 0), new Point3d(1, 3, 0));

                new PatMarkCommand().Run();

#if SIM_LEADER
                MText annotation = Assert.IsType<MText>(fixture.Database.CommittedEntities[0]);
                Assert.Equal(AttachmentPoint.TopRight, annotation.Attachment);
#endif
            }
        }

        [Fact]
        public void PostCommitReapplyRestoresUpperQuadrantAfterLegacyNormalization()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                fixture.Database.NormalizeAttachmentOnFirstCommit = true;
                IO.PatSettingsStore.Current.ThreePointMode = true;
                fixture.QueueThreePointAnnotation("post-commit", new Point3d(1, 1, 0),
                    new Point3d(5, 6, 0), new Point3d(8, 3, 0));

                new PatMarkCommand().Run();

#if SIM_LEADER
                MText annotation = Assert.IsType<MText>(fixture.Database.CommittedEntities[0]);
                Assert.Equal(AttachmentPoint.TopLeft, annotation.Attachment);
                Leader leader = Assert.IsType<Leader>(fixture.Database.CommittedEntities[1]);
                Assert.Equal(3, leader.Vertices.Count);
                Assert.True(leader.Annotation.IsNull);
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
                Assert.Equal(AttachmentPoint.BottomLeft, annotation.Attachment);
                Leader leader = Assert.IsType<Leader>(fixture.Database.CommittedEntities[1]);
                Assert.Equal(2, leader.Vertices.Count);
                Assert.True(leader.Annotation.IsNull);
#else
                MLeader leader = Assert.IsType<MLeader>(fixture.Database.CommittedEntities[0]);
                Assert.Equal(new Point3d(2, 2, 0), leader.TextLocation);
#endif
            }
        }

        [Fact]
        public void FreeModeExplicitTextPointUsesTheFacingCorner()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                Palette.PatPaletteCommand.PendingNumber = "free-corner";
                fixture.Editor.EnqueuePoint(PromptStatus.OK, new Point3d(10, 10, 0));
                fixture.Editor.EnqueuePoint(PromptStatus.OK, new Point3d(5, 6, 0));
                fixture.Editor.EnqueuePoint(PromptStatus.None, new Point3d());
                fixture.Editor.EnqueuePoint(PromptStatus.OK, new Point3d(2, 3, 0));
                fixture.Editor.EnqueuePoint(PromptStatus.Cancel, new Point3d());

                new PatMarkCommand().Run();

#if SIM_LEADER
                MText annotation = Assert.IsType<MText>(fixture.Database.CommittedEntities[0]);
                Assert.Equal(AttachmentPoint.TopRight, annotation.Attachment);
#endif
            }
        }

        [Fact]
        public void FreeModeHorizontalTieUsesACornerInsteadOfMiddleSide()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                fixture.QueueFreeModeAnnotation("free-horizontal",
                    new Point3d(1, 2, 0), new Point3d(3, 2, 0));

                new PatMarkCommand().Run();

#if SIM_LEADER
                MText annotation = Assert.IsType<MText>(fixture.Database.CommittedEntities[0]);
                Assert.Equal(AttachmentPoint.TopLeft, annotation.Attachment);
#endif
            }
        }

        [Fact]
        public void ThreePointConfirmAfterCompletedAnnotationExitsCommand()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                IO.PatSettingsStore.Current.ThreePointMode = true;
                fixture.QueueThreePointCancelAfterFirstAnnotation("confirm-three",
                    new Point3d(1, 1, 0), new Point3d(2, 2, 0), new Point3d(3, 3, 0));

                new PatMarkCommand().Run();

                Assert.Equal(2, fixture.Database.CommittedEntities.Count);
                Assert.Equal(4, fixture.Editor.PointPrompts.Count);
                Assert.Equal(4, fixture.Editor.PointPromptAllowsNone.Count);
                Assert.All(fixture.Editor.PointPromptAllowsNone, Assert.True);
            }
        }

        [Fact]
        public void ThreePointCancelAfterCompletedAnnotationExitsCommand()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                IO.PatSettingsStore.Current.ThreePointMode = true;
                Palette.PatPaletteCommand.PendingNumber = "cancel-three";
                fixture.Editor.EnqueuePoint(PromptStatus.OK, new Point3d(1, 1, 0));
                fixture.Editor.EnqueuePoint(PromptStatus.OK, new Point3d(2, 2, 0));
                fixture.Editor.EnqueuePoint(PromptStatus.OK, new Point3d(3, 3, 0));
                fixture.Editor.EnqueuePoint(PromptStatus.Cancel, new Point3d());

                new PatMarkCommand().Run();

                Assert.Equal(2, fixture.Database.CommittedEntities.Count);
                Assert.Equal(4, fixture.Editor.PointPrompts.Count);
                Assert.Equal(4, fixture.Editor.PointPromptAllowsNone.Count);
                Assert.All(fixture.Editor.PointPromptAllowsNone, Assert.True);
            }
        }

        [Fact]
        public void FreeModeCancelDuringDoglegCollectionExitsCommand()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                fixture.QueueFreeModeCancelDuringDoglegs("cancel-free",
                    new Point3d(1, 1, 0), new Point3d(2, 2, 0));

                new PatMarkCommand().Run();

                Assert.Empty(fixture.Database.CommittedEntities);
                Assert.Equal(3, fixture.Editor.PointPrompts.Count);
                Assert.Equal(3, fixture.Editor.PointPromptAllowsNone.Count);
                Assert.All(fixture.Editor.PointPromptAllowsNone, Assert.True);
            }
        }

        [Fact]
        public void FreeModeConfirmAfterAnnotationExitsCommand()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                fixture.QueueFreeModeConfirmAfterAnnotation("confirm-free",
                    new Point3d(1, 1, 0), new Point3d(2, 2, 0));

                new PatMarkCommand().Run();

#if SIM_LEADER
                Assert.Equal(2, fixture.Database.CommittedEntities.Count);
#else
                Assert.Single(fixture.Database.CommittedEntities);
#endif
                Assert.Equal(5, fixture.Editor.PointPrompts.Count);
                Assert.Equal(5, fixture.Editor.PointPromptAllowsNone.Count);
                Assert.All(fixture.Editor.PointPromptAllowsNone, Assert.True);
            }
        }

        [Fact]
        public void FreeModeCancelAfterAnnotationExitsCommand()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                Palette.PatPaletteCommand.PendingNumber = "cancel-free-after";
                fixture.Editor.EnqueuePoint(PromptStatus.OK, new Point3d(1, 1, 0));
                fixture.Editor.EnqueuePoint(PromptStatus.OK, new Point3d(2, 2, 0));
                fixture.Editor.EnqueuePoint(PromptStatus.None, new Point3d());
                fixture.Editor.EnqueuePoint(PromptStatus.None, new Point3d());
                fixture.Editor.EnqueuePoint(PromptStatus.Cancel, new Point3d());

                new PatMarkCommand().Run();

#if SIM_LEADER
                Assert.Equal(2, fixture.Database.CommittedEntities.Count);
#else
                Assert.Single(fixture.Database.CommittedEntities);
#endif
                Assert.Equal(5, fixture.Editor.PointPrompts.Count);
                Assert.Equal(5, fixture.Editor.PointPromptAllowsNone.Count);
                Assert.All(fixture.Editor.PointPromptAllowsNone, Assert.True);
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
