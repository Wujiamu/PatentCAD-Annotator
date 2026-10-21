using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PatentMarker.Commands;
using Xunit;
using PatentMarker.RuntimeContractTests;

namespace PatentMarker.RuntimeContract.Tests
{
    public class PatGeomVerifyTests
    {
        [Fact]
        public void FacingBoundaryGap_RejectsTouchInsideWrongSideAndWrongLevel()
        {
            foreach (double h in new double[] { 1, 3.5, 20 })
            foreach (double x in new double[] { 0, 10000000 })
            foreach (bool left in new bool[] { true, false })
            {
                Point3d min = new Point3d(x, 10, 7), max = new Point3d(x + 4*h, 10+h, 7);
                Point3d prev = new Point3d(left ? x-10*h : x+14*h, 10+h/2, 7);
                double edge = left ? min.X : max.X, sign = left ? -1 : 1;
                foreach(double y in new double[]{min.Y,(min.Y+max.Y)/2,max.Y})
                {
                    Assert.True(PatGeomTextAssertions.VerifyBoundaryGap(true,min,max,prev,new Point3d(edge+sign*.4*h,y,7),h,new StringBuilder()));
                    Assert.False(PatGeomTextAssertions.VerifyBoundaryGap(true,min,max,prev,new Point3d(edge,y,7),h,new StringBuilder()));
                    Assert.False(PatGeomTextAssertions.VerifyBoundaryGap(true,min,max,prev,new Point3d(edge-sign*.4*h,y,7),h,new StringBuilder()));
                }
                Assert.False(PatGeomTextAssertions.VerifyBoundaryGap(true,min,max,prev,new Point3d(edge+sign*.4*h,10+2*h,7),h,new StringBuilder()));
                Assert.False(PatGeomTextAssertions.VerifyBoundaryGap(true,min,max,prev,new Point3d(edge+sign*.4*h,10+h/2,8),h,new StringBuilder()));
            }
        }

#if !SIM_LEADER
        [Fact]
        public void FourUserCreationCases_KeepFacingBoundaryGapAndPickedText()
        {
            for(int c=0;c<4;c++)
            using(SimulationFixture fixture = new SimulationFixture())
            {
                IO.PatSettingsStore.Current.TextHeight=3.5;
                IO.PatSettingsStore.Current.HasArrowHead=false;
                IO.PatSettingsStore.Current.UnderlineText=false;
                IO.PatSettingsStore.Current.ThreePointMode=true;
                IO.PatSettingsStore.Current.IsSplined=c>=2;
                Point3d pick=new Point3d(c%2==0?40:65,c<2?110:150,0);
                Point3d dog=new Point3d(c%2==0?20:80,c<2?110:150,0);
                fixture.QueueThreePointAnnotation("901",new Point3d(c%2==0?0:60,c<2?100:140,0),dog,pick);
                new PatMarkCommand().Run();
                MLeader ml=Assert.IsType<MLeader>(fixture.Database.CommittedEntities[0]);
                Assert.Equal(pick,ml.TextLocation); Assert.Equal(pick,ml.MText.Location);
                var box=ml.MText.GeometricExtents;
                var report=new StringBuilder();
                Assert.True(PatGeomTextAssertions.VerifyBoundaryGap(true,box.MinPoint,box.MaxPoint,dog,ml.GetLastVertex(0),3.5,report),"case="+c+" "+report);
            }
        }
#endif
        [Fact]
        public void LegacyLeader_ReportIncludesObjectHeader()
        {
            WithFailureEnvironment((fixture, directory) =>
            {
                using (Transaction tr = fixture.Database.TransactionManager.StartTransaction())
                {
                    MText text = new MText { Location = new Point3d(20, 10, 0), TextHeight = 3.5 };
                    fixture.Database.ModelSpace.AppendEntity(text);
                    tr.AddNewlyCreatedDBObject(text, true);
                    Leader leader = new Leader();
                    leader.AppendVertex(new Point3d(0, 0, 0));
                    leader.AppendVertex(new Point3d(10, 10, 0));
                    leader.AppendVertex(new Point3d(18.6, 10, 0));
                    fixture.Database.ModelSpace.AppendEntity(leader);
                    tr.AddNewlyCreatedDBObject(leader, true);
                    PatLeaderTextAttachment.LinkText(leader, text, tr);
                    tr.Commit();
                }
                Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_BRACES", "0");
                new PatGeomVerifyCommand().Run();
                string report = File.ReadAllText(Path.Combine(directory, "PatentMarker-geom-verify.txt"));
                Assert.Contains("\nLEADER ", report);
                Assert.Contains("RESULT: PASS", report);
            });
        }

#if !SIM_LEADER
        [Fact]
        public void ActualPreviousVertex_UsesIndexAtLargeCoordinatesAndRepeatedVertices()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                IO.PatSettingsStore.Current.ThreePointMode = true;
                fixture.QueueThreePointAnnotation("901", new Point3d(10000000, 0, 0),
                    new Point3d(10000010, 10, 0), new Point3d(10000020, 10, 0));
                new PatMarkCommand().Run();
                MLeader leader = Assert.IsType<MLeader>(fixture.Database.CommittedEntities[0]);
                Point3d previous, last;
                Assert.True(IO.PatEntityHelper.TryReadMLeaderActualPreviousVertex(leader, out previous, out last));
                Assert.Equal(new Point3d(10000010, 10, 0), previous);
                // A previous vertex may even have the same coordinates as the
                // endpoint: topology, rather than coordinate search, decides.
                IList<Point3d> vertices = (IList<Point3d>)leader.Vertices;
                vertices[0] = last;
                vertices[1] = last;
                leader.AddLastVertex(0, new Point3d(10000030, 20, 0));
                Assert.True(IO.PatEntityHelper.TryReadMLeaderActualPreviousVertex(leader, out previous, out last));
                Assert.Equal(vertices[2], previous);
            }
        }
#endif

        [Fact]
        public void RequiredFailure_ReportsFailWithoutEscapingCommand()
        {
            WithFailureEnvironment((fixture, directory) =>
            {
                Assert.Null(Record.Exception(() => new PatGeomVerifyCommand().Run()));
                string report = File.ReadAllText(Path.Combine(directory,
                    "PatentMarker-geom-verify.txt"));
                Assert.Contains("required targets missing", report);
                Assert.Contains("RESULT: FAIL", report);
                Assert.DoesNotContain("RESULT: PASS", report);
            });
        }

        [Fact]
        public void ReportWriteFailure_DoesNotEscapeCommand()
        {
            WithFailureEnvironment((fixture, directory) =>
            {
                string blocked = Path.Combine(directory, "existing-file");
                File.WriteAllText(blocked, "sentinel");
                Environment.SetEnvironmentVariable("PATGEOM_REPORT_DIR", blocked);
                Assert.Null(Record.Exception(() => new PatGeomVerifyCommand().Run()));
                Assert.Contains(fixture.Editor.Messages,
                    message => message.Contains("report write failed"));
                Assert.Equal("sentinel", File.ReadAllText(blocked));
                Assert.False(File.Exists(Path.Combine(directory,
                    "PatentMarker-geom-verify.txt")));
            });
        }

        [Fact]
        public void UnexpectedFailure_ReplacesStalePassAndDoesNotEscapeCommand()
        {
            WithFailureEnvironment((fixture, directory) =>
            {
                string path = Path.Combine(directory, "PatentMarker-geom-verify.txt");
                File.WriteAllText(path, "RUN_ID: old-run\nRESULT: PASS\n");
                fixture.Document.Database = null;
                try
                {
                    Assert.Null(Record.Exception(() => new PatGeomVerifyCommand().Run()));
                    Assert.Contains(fixture.Editor.Messages,
                        message => message.Contains("verification aborted"));
                    string report = File.ReadAllText(path);
                    Assert.Contains("RUN_ID: command-failure-boundary", report);
                    Assert.Contains("RESULT: FAIL", report);
                    Assert.DoesNotContain("RESULT: PASS", report);
                }
                finally { fixture.Document.Database = fixture.Database; }
            });
        }

        private static void WithFailureEnvironment(Action<SimulationFixture, string> verify)
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "pm-geom-boundary-" + Guid.NewGuid().ToString("N"));
            string[] names = { "PATGEOM_REPORT_DIR", "PATGEOM_RUN_ID",
                "PATGEOM_REQUIRE_BRACES", "PATGEOM_REQUIRE_TEXTS", "PATGEOM_STRICT" };
            string[] original = new string[names.Length];
            for (int i = 0; i < names.Length; i++)
                original[i] = Environment.GetEnvironmentVariable(names[i]);
            Directory.CreateDirectory(directory);
            try
            {
                Environment.SetEnvironmentVariable(names[0], directory);
                Environment.SetEnvironmentVariable(names[1], "command-failure-boundary");
                Environment.SetEnvironmentVariable(names[2], "1");
                Environment.SetEnvironmentVariable(names[3], "1");
                Environment.SetEnvironmentVariable(names[4], "1");
                using (SimulationFixture fixture = new SimulationFixture())
                    verify(fixture, directory);
            }
            finally
            {
                for (int i = 0; i < names.Length; i++)
                    Environment.SetEnvironmentVariable(names[i], original[i]);
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void MovedTextWithoutChainUpdate_IsRejectedByT3()
        {
            string reportDir = Path.Combine(Path.GetTempPath(),
                "pm-geom-moved-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(reportDir);
            string oldReportDir = Environment.GetEnvironmentVariable("PATGEOM_REPORT_DIR");
            string oldBraces = Environment.GetEnvironmentVariable("PATGEOM_REQUIRE_BRACES");
            string oldTexts = Environment.GetEnvironmentVariable("PATGEOM_REQUIRE_TEXTS");
            string oldStrict = Environment.GetEnvironmentVariable("PATGEOM_STRICT");
            try
            {
                using (SimulationFixture fixture = new SimulationFixture())
                {
                    IO.PatSettingsStore.Current.ThreePointMode = true;
                    fixture.QueueThreePointAnnotation("901",
                        new Point3d(0, 0, 0),
                        new Point3d(10, 10, 0),
                        new Point3d(20, 10, 0));
                    new PatMarkCommand().Run();
#if SIM_LEADER
                    MText text = null;
                    foreach (Entity entity in fixture.Database.CommittedEntities)
                    {
                        text = entity as MText;
                        if (text != null) break;
                    }
                    Assert.NotNull(text);
#else
                    MLeader leader = null;
                    foreach (Entity entity in fixture.Database.CommittedEntities)
                    {
                        leader = entity as MLeader;
                        if (leader != null) break;
                    }
                    Assert.NotNull(leader);
                    MText text = leader.MText;
                    leader.TextLocation = new Point3d(2000, 1000, 0);
#endif
                    text.Location = new Point3d(2000, 1000, 0);

                    Environment.SetEnvironmentVariable("PATGEOM_REPORT_DIR", reportDir);
                    Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_BRACES", "0");
                    Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_TEXTS", "1");
                    Environment.SetEnvironmentVariable("PATGEOM_STRICT", "1");
                    Assert.Null(Record.Exception(() => new PatGeomVerifyCommand().Run()));
                }

                string report = File.ReadAllText(
                    Path.Combine(reportDir, "PatentMarker-geom-verify.txt"));
                Assert.Contains("RESULT: FAIL", report);
#if SIM_LEADER
                Assert.Contains("[FAIL] T3e detached-corner-ray", report);
#else
                Assert.Contains("[FAIL] T3a recorded-text-anchor", report);
#endif
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATGEOM_REPORT_DIR", oldReportDir);
                Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_BRACES", oldBraces);
                Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_TEXTS", oldTexts);
                Environment.SetEnvironmentVariable("PATGEOM_STRICT", oldStrict);
                try { Directory.Delete(reportDir, true); } catch { }
            }
        }

        [Fact]
        public void MovedPreviousVertexWithoutChainUpdate_IsRejectedByT3()
        {
            string reportDir = Path.Combine(Path.GetTempPath(),
                "pm-geom-prev-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(reportDir);
            string oldReportDir = Environment.GetEnvironmentVariable("PATGEOM_REPORT_DIR");
            string oldBraces = Environment.GetEnvironmentVariable("PATGEOM_REQUIRE_BRACES");
            string oldTexts = Environment.GetEnvironmentVariable("PATGEOM_REQUIRE_TEXTS");
            string oldStrict = Environment.GetEnvironmentVariable("PATGEOM_STRICT");
            try
            {
                using (SimulationFixture fixture = new SimulationFixture())
                {
                    IO.PatSettingsStore.Current.ThreePointMode = true;
                    fixture.QueueThreePointAnnotation("901",
                        new Point3d(0, 0, 0),
                        new Point3d(10, 10, 0),
                        new Point3d(20, 10, 0));
                    new PatMarkCommand().Run();
#if SIM_LEADER
                    Leader leader = null;
                    foreach (Entity entity in fixture.Database.CommittedEntities)
                    {
                        leader = entity as Leader;
                        if (leader != null) break;
                    }
                    Assert.NotNull(leader);
                    leader.SetVertexAt(leader.NumVertices - 2,
                        new Point3d(10, 100, 0));
#else
                    MLeader leader = null;
                    foreach (Entity entity in fixture.Database.CommittedEntities)
                    {
                        leader = entity as MLeader;
                        if (leader != null) break;
                    }
                    Assert.NotNull(leader);
                    IList<Point3d> vertices = (IList<Point3d>)leader.Vertices;
                    vertices[vertices.Count - 2] = new Point3d(10, 100, 0);
#endif

                    Environment.SetEnvironmentVariable("PATGEOM_REPORT_DIR", reportDir);
                    Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_BRACES", "0");
                    Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_TEXTS", "1");
                    Environment.SetEnvironmentVariable("PATGEOM_STRICT", "1");
                    Assert.Null(Record.Exception(() => new PatGeomVerifyCommand().Run()));
                }

                string report = File.ReadAllText(
                    Path.Combine(reportDir, "PatentMarker-geom-verify.txt"));
                Assert.Contains("RESULT: FAIL", report);
#if SIM_LEADER
                Assert.Contains("[FAIL] T3e detached-corner-ray", report);
#else
                Assert.Contains("[FAIL] T3c actual-previous-anchor", report);
#endif
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATGEOM_REPORT_DIR", oldReportDir);
                Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_BRACES", oldBraces);
                Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_TEXTS", oldTexts);
                Environment.SetEnvironmentVariable("PATGEOM_STRICT", oldStrict);
                try { Directory.Delete(reportDir, true); } catch { }
            }
        }

        [Fact]
        public void EmptyDrawing_IsSkipNotPass_AndStrictRunFails()
        {
            string reportDir = Path.Combine(Path.GetTempPath(),
                "pm-geom-empty-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(reportDir);
            string oldReportDir = Environment.GetEnvironmentVariable("PATGEOM_REPORT_DIR");
            string oldBraces = Environment.GetEnvironmentVariable("PATGEOM_REQUIRE_BRACES");
            string oldTexts = Environment.GetEnvironmentVariable("PATGEOM_REQUIRE_TEXTS");
            string oldStrict = Environment.GetEnvironmentVariable("PATGEOM_STRICT");
            try
            {
                using (SimulationFixture fixture = new SimulationFixture())
                {
                    Environment.SetEnvironmentVariable("PATGEOM_REPORT_DIR", reportDir);
                    Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_BRACES", "0");
                    Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_TEXTS", "0");
                    Environment.SetEnvironmentVariable("PATGEOM_STRICT", "0");
                    new PatGeomVerifyCommand().Run();
                }
                string report = File.ReadAllText(
                    Path.Combine(reportDir, "PatentMarker-geom-verify.txt"));
                Assert.Contains("RESULT: SKIP", report);
                Assert.DoesNotContain("RESULT: PASS", report);

                using (SimulationFixture strictFixture = new SimulationFixture())
                {
                    Environment.SetEnvironmentVariable("PATGEOM_STRICT", "1");
                    Assert.Null(Record.Exception(() => new PatGeomVerifyCommand().Run()));
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATGEOM_REPORT_DIR", oldReportDir);
                Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_BRACES", oldBraces);
                Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_TEXTS", oldTexts);
                Environment.SetEnvironmentVariable("PATGEOM_STRICT", oldStrict);
                try { Directory.Delete(reportDir, true); } catch { }
            }
        }

        [Fact]
        public void ProductionGeometry_PassesBraceAssertions()
        {
            var cases = new[]
            {
                new { Top = new Point3d(0,0,0), Bottom = new Point3d(0,100,0), Width = 50.0, Side = 1 },
                new { Top = new Point3d(0,0,0), Bottom = new Point3d(100,0,0), Width = 30.0, Side = -1 },
                new { Top = new Point3d(10,20,2), Bottom = new Point3d(80,90,2), Width = 25.0, Side = 1 },
                new { Top = new Point3d(0,0,0), Bottom = new Point3d(0,20,0), Width = 100.0, Side = -1 },
                new { Top = new Point3d(0,0,7.5), Bottom = new Point3d(0,100,7.5), Width = 40.0, Side = 1 }
            };
            foreach (var item in cases)
            {
                PatBraceDefinition definition = PatBraceGeometry.Create(
                    item.Top, item.Bottom, item.Width, item.Side);
                Polyline polyline = MakePolyline(PatBraceGeometry.BuildPoints(definition),
                    definition.Top.Z);
                var report = new StringBuilder();
                bool ok = PatBraceAssertions.Verify(polyline, definition, report);
                Assert.True(ok, report.ToString());
                Assert.DoesNotContain("[FAIL]", report.ToString());
            }
        }

        [Fact]
        public void HistoricalSharpPolygon_IsRejected()
        {
            PatBraceDefinition definition = PatBraceGeometry.Create(
                new Point3d(0,0,0), new Point3d(0,100,0), 50, 1);
            Polyline polyline = MakePolyline(new[]
            {
                new Point3d(0,0,0),
                new Point3d(-25,4,0),
                new Point3d(-50,50,0),
                new Point3d(-25,96,0),
                new Point3d(0,100,0)
            }, 0);
            var report = new StringBuilder();
            Assert.False(PatBraceAssertions.Verify(polyline, definition, report),
                report.ToString());
            Assert.Contains("[FAIL] B9 corner-curves", report.ToString());
        }

        [Fact]
        public void ProductionGeometry_WithDifferentCurveSamples_PassesReferenceCheck()
        {
            PatBraceDefinition[] definitions =
            {
                PatBraceGeometry.Create(new Point3d(0,0,0), new Point3d(0,100,0), 50, 1),
                PatBraceGeometry.Create(new Point3d(0,0,0), new Point3d(0,100,0), 100, 1),
                PatBraceGeometry.Create(new Point3d(0,0,0), new Point3d(0,100,0), 20, -1),
                PatBraceGeometry.Create(new Point3d(0,0,0), new Point3d(0,100,0), 30, 1)
            };
            foreach (PatBraceDefinition definition in definitions)
            {
                foreach (int samples in new[] { 2, 4, 16, 32 })
                {
                    Polyline polyline = MakePolyline(
                        PatBraceGeometry.BuildPoints(definition, samples),
                        definition.Top.Z);
                    var report = new StringBuilder();
                    Assert.True(PatBraceAssertions.Verify(polyline, definition, report),
                        "width=" + definition.Width + " side=" + definition.Side
                        + " samples=" + samples + "\n" + report);
                }
            }
        }

        [Fact]
        public void SharpElbowWithFakeEndTangents_IsRejectedByReferenceCurve()
        {
            PatBraceDefinition definition = PatBraceGeometry.Create(
                new Point3d(0,0,0), new Point3d(0,100,0), 50, 1);
            double[][] pairs =
            {
                new double[] { 0, 0 },
                new double[] { 0.001, 24.99 },
                new double[] { 4.1665, 25 },
                new double[] { 45.8335, 25 },
                new double[] { 49.999, 25.01 },
                new double[] { 50, 50 },
                new double[] { 50.001, 25.01 },
                new double[] { 54.1665, 25 },
                new double[] { 95.8335, 25 },
                new double[] { 99.999, 24.99 },
                new double[] { 100, 0 }
            };
            var points = new List<Point3d>();
            foreach (double[] pair in pairs)
                points.Add(LocalPoint(definition, pair[0], pair[1]));
            Polyline polyline = MakePolyline(points, definition.Top.Z);
            var report = new StringBuilder();
            Assert.False(PatBraceAssertions.Verify(polyline, definition, report),
                report.ToString());
            Assert.Contains("[FAIL] B9 corner-curves", report.ToString());
        }

        [Fact]
        public void SharpCornerLandmarks_AreRejectedByCurvatureCheck()
        {
            PatBraceDefinition definition = PatBraceGeometry.Create(
                new Point3d(0,0,0), new Point3d(0,100,0), 50, 1);
            double height = definition.Height;
            double width = definition.Width;
            double corner = Math.Min(width, height) * (8333.0 / 100000.0);
            Polyline polyline = MakePolyline(new[]
            {
                LocalPoint(definition, 0.0, 0.0),
                LocalPoint(definition, corner, width * 0.5),
                LocalPoint(definition, height * 0.5 - corner, width * 0.5),
                LocalPoint(definition, height * 0.5, width),
                LocalPoint(definition, height * 0.5 + corner, width * 0.5),
                LocalPoint(definition, height - corner, width * 0.5),
                LocalPoint(definition, height, 0.0)
            }, definition.Top.Z);
            var report = new StringBuilder();
            Assert.False(PatBraceAssertions.Verify(polyline, definition, report),
                report.ToString());
            Assert.Contains("[FAIL] B9 corner-curves", report.ToString());
        }

        [Fact]
        public void MirroredStem_IsRejectedByAxisRule()
        {
            PatBraceDefinition definition = PatBraceGeometry.Create(
                new Point3d(0,0,0), new Point3d(0,100,0), 50, 1);
            double height = definition.Height;
            double width = definition.Width;
            Polyline polyline = MakePolyline(new[]
            {
                LocalPoint(definition, 0.0, 0.0),
                LocalPoint(definition, 4.0, -width * 0.42),
                LocalPoint(definition, height * 0.5, width),
                LocalPoint(definition, height - 4.0, -width * 0.42),
                LocalPoint(definition, height, 0.0)
            }, definition.Top.Z);
            var report = new StringBuilder();
            Assert.False(PatBraceAssertions.Verify(polyline, definition, report),
                report.ToString());
            Assert.Contains("[FAIL] B1 no-axis-crossing", report.ToString());
        }

        [Fact]
        public void DoubleCuspProfile_IsRejectedByStructureCheck()
        {
            PatBraceDefinition definition = PatBraceGeometry.Create(
                new Point3d(0,0,0), new Point3d(0,100,0), 50, 1);
            Polyline polyline = MakePolyline(new[]
            {
                LocalPoint(definition, 0, 0),
                LocalPoint(definition, 20, 25),
                LocalPoint(definition, 40, 5),
                LocalPoint(definition, 50, 50),
                LocalPoint(definition, 60, 5),
                LocalPoint(definition, 80, 25),
                LocalPoint(definition, 100, 0)
            }, definition.Top.Z);
            var report = new StringBuilder();
            Assert.False(PatBraceAssertions.Verify(polyline, definition, report),
                report.ToString());
            Assert.Contains("[FAIL] B8 single-cusp-no-self-intersection",
                report.ToString());
        }

        [Fact]
        public void EndpointDrift_IsRejectedByExactEndpointCheck()
        {
            PatBraceDefinition definition = PatBraceGeometry.Create(
                new Point3d(0,0,0), new Point3d(0,100,0), 50, 1);
            Polyline polyline = MakePolyline(PatBraceGeometry.BuildPoints(definition), 0);
            polyline.SetPointAt(0, new Point2d(0.01, 0.0));
            var report = new StringBuilder();
            Assert.False(PatBraceAssertions.Verify(polyline, definition, report),
                report.ToString());
            Assert.Contains("[FAIL] B5 endpoints-exact", report.ToString());
        }

        [Fact]
        public void NonPlanarPolyline_IsRejectedByPlanarContract()
        {
            PatBraceDefinition definition = PatBraceGeometry.Create(
                new Point3d(0,0,0), new Point3d(0,100,0), 50, 1);
            Polyline polyline = MakePolyline(
                PatBraceGeometry.BuildPoints(definition), 0);
            polyline.SetVertexZ(1, 0.5);
            var report = new StringBuilder();
            Assert.False(PatBraceAssertions.Verify(polyline, definition, report),
                report.ToString());
            Assert.Contains("[FAIL] B0c planar-contract", report.ToString());
        }

        [Fact]
        public void Rotation_UsesRadiansAndRejectsNonFinite()
        {
            var report = new StringBuilder();
            Assert.True(PatGeomTextAssertions.VerifyRotation(true, 0.0, report), report.ToString());
            Assert.True(PatGeomTextAssertions.VerifyRotation(true, Math.PI * 2.0, report), report.ToString());
            Assert.False(PatGeomTextAssertions.VerifyRotation(true, Math.PI / 6.0, report), report.ToString());
            Assert.False(PatGeomTextAssertions.VerifyRotation(true, double.NaN, report), report.ToString());
            Assert.False(PatGeomTextAssertions.VerifyRotation(false, 0.0, report), report.ToString());
        }

        [Fact]
        public void Direction_RequiresPositiveWcsXAxis()
        {
            var report = new StringBuilder();
            Assert.True(PatGeomTextAssertions.VerifyDirection(
                true, new Vector3d(1, 0, 0), report), report.ToString());
            Assert.False(PatGeomTextAssertions.VerifyDirection(
                true, new Vector3d(0, 1, 0), report), report.ToString());
            Assert.False(PatGeomTextAssertions.VerifyDirection(
                false, new Vector3d(1, 0, 0), report), report.ToString());
        }

        [Fact]
        public void Retract_HandlesLongShortAndDegenerateSegments()
        {
            var report = new StringBuilder();
            Point3d previous = new Point3d(0, 0, 0);
            Point3d text = new Point3d(10, 0, 0);
            Point3d expected = new Point3d(8, 0, 0);
            Assert.True(PatGeomTextAssertions.VerifyRetract(
                true, previous, text, expected, 5.0, report), report.ToString());
            Assert.False(PatGeomTextAssertions.VerifyRetract(
                true, previous, text, text, 5.0, report), report.ToString());

            Point3d shortText = new Point3d(1, 0, 0);
            Point3d shortExpected = new Point3d(0.5, 0, 0);
            Assert.True(PatGeomTextAssertions.VerifyRetract(
                true, previous, shortText, shortExpected, 5.0, report), report.ToString());

            Point3d same = new Point3d(3, 4, 0);
            Assert.Equal(same, PatGeomTextAssertions.ExpectedRetract(same, same, 5.0));
            Assert.Equal(text, PatGeomTextAssertions.ExpectedRetract(previous, text, 0.0));
            Assert.False(PatGeomTextAssertions.VerifyRetract(
                true, previous, text, expected, double.NaN, report), report.ToString());
        }

        [Fact]
        public void Summary_DistinguishesPassSkipAndRequiredFailures()
        {
            PatGeomVerifySummary.Result pass = PatGeomVerifySummary.Evaluate(
                1, 1, 0, 0, 1, 1, 0, 0, 1, 1, true);
            Assert.Equal(PatGeomVerifySummary.Pass, pass.Status);
            Assert.False(pass.RequiresFailureExit);

            PatGeomVerifySummary.Result dailyEmpty = PatGeomVerifySummary.Evaluate(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false);
            Assert.Equal(PatGeomVerifySummary.Skip, dailyEmpty.Status);
            Assert.False(dailyEmpty.RequiresFailureExit);

            PatGeomVerifySummary.Result strictEmpty = PatGeomVerifySummary.Evaluate(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, true);
            Assert.Equal(PatGeomVerifySummary.Skip, strictEmpty.Status);
            Assert.True(strictEmpty.RequiresFailureExit);

            PatGeomVerifySummary.Result requiredMissing = PatGeomVerifySummary.Evaluate(
                0, 0, 0, 0, 0, 0, 0, 0, 1, 1, true);
            Assert.Equal(PatGeomVerifySummary.Fail, requiredMissing.Status);
            Assert.True(requiredMissing.RequiresFailureExit);

            PatGeomVerifySummary.Result unreadable = PatGeomVerifySummary.Evaluate(
                1, 0, 0, 1, 1, 1, 0, 0, 0, 0, false);
            Assert.Equal(PatGeomVerifySummary.Fail, unreadable.Status);
            Assert.True(unreadable.RequiresFailureExit);
        }

        [Fact]
        public void ProductMarkerHelpers_DoNotClaimOrdinaryEntities()
        {
            var db = new Database();
            Transaction tr = db.TransactionManager.StartTransaction();
            MText ordinary = new MText();
            db.ModelSpace.AppendEntity(ordinary);
            Assert.False(IO.PatEntityHelper.IsStandaloneText(ordinary, tr));

            MText product = new MText();
            db.ModelSpace.AppendEntity(product);
            IO.PatEntityHelper.MarkStandaloneText(product, tr);
            Assert.True(IO.PatEntityHelper.IsStandaloneText(product, tr));

            MLeader ordinaryLeader = new MLeader();
            db.ModelSpace.AppendEntity(ordinaryLeader);
            Assert.False(IO.PatEntityHelper.IsPatMLeader(ordinaryLeader, tr));

            MLeader productLeader = new MLeader();
            db.ModelSpace.AppendEntity(productLeader);
            productLeader.CreateExtensionDictionary();
            DBDictionary dictionary = (DBDictionary)tr.GetObject(
                productLeader.ExtensionDictionary, OpenMode.ForWrite);
            Xrecord record = new Xrecord();
            record.Data = new ResultBuffer(new TypedValue(1, "PATENTMARKER_MLEADER_V1"));
            dictionary.SetAt("PATENTMARKER_MLEADER", record);
            tr.AddNewlyCreatedDBObject(record, true);
            Assert.True(IO.PatEntityHelper.IsPatMLeader(productLeader, tr));
            tr.Dispose();
        }

        [Fact]
        public void CommandLevelRun_GeneratesPassReportForBraceAndAnnotation()
        {
            string reportDir = Path.Combine(Path.GetTempPath(),
                "pm-geom-command-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(reportDir);
            string oldReportDir = Environment.GetEnvironmentVariable("PATGEOM_REPORT_DIR");
            string oldRunId = Environment.GetEnvironmentVariable("PATGEOM_RUN_ID");
            string oldBraces = Environment.GetEnvironmentVariable("PATGEOM_REQUIRE_BRACES");
            string oldTexts = Environment.GetEnvironmentVariable("PATGEOM_REQUIRE_TEXTS");
            string oldStrict = Environment.GetEnvironmentVariable("PATGEOM_STRICT");
            try
            {
                using (SimulationFixture fixture = new SimulationFixture())
                {
                    IO.PatSettingsStore.Current.ThreePointMode = true;
                    fixture.QueueThreePointAnnotation("901",
                        new Point3d(0, 0, 0),
                        new Point3d(10, 10, 0),
                        new Point3d(20, 10, 0));
                    new PatMarkCommand().Run();

                    using (Transaction tr = fixture.Database.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(
                            fixture.Database.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                            bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        PatBraceDefinition definition = PatBraceGeometry.Create(
                            new Point3d(100, 0, 0), new Point3d(100, 100, 0), 40, 1);
                        Polyline brace = PatBraceEntity.CreatePolyline(
                            fixture.Database, definition);
                        ms.AppendEntity(brace);
                        tr.AddNewlyCreatedDBObject(brace, true);
                        PatBraceEntity.WriteDefinition(brace, definition, tr);
                        tr.Commit();
                    }

                    Environment.SetEnvironmentVariable("PATGEOM_REPORT_DIR", reportDir);
                    Environment.SetEnvironmentVariable("PATGEOM_RUN_ID", "command-test-run");
                    Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_BRACES", "1");
                    Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_TEXTS", "1");
                    Environment.SetEnvironmentVariable("PATGEOM_STRICT", "1");
                    try
                    {
                        new PatGeomVerifyCommand().Run();
                    }
                    catch (Exception commandError)
                    {
                        string debugPath = Path.Combine(
                            reportDir, "PatentMarker-geom-verify.txt");
                        string debugReport = File.Exists(debugPath)
                            ? File.ReadAllText(debugPath) : "(report missing)";
                        Assert.True(false, commandError.Message + "\n" + debugReport);
                    }
                }

                string report = File.ReadAllText(
                    Path.Combine(reportDir, "PatentMarker-geom-verify.txt"));
                Assert.Contains("RUN_ID: command-test-run", report);
                Assert.Contains("RESULT: PASS", report);
                Assert.DoesNotContain("[FAIL]", report);
                Assert.Contains("[PASS] B9 corner-curves", report);
                Assert.Contains("[PASS] T1 text-horizontal", report);
                Assert.Contains("[PASS] T3 leader-text-retract", report);
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATGEOM_REPORT_DIR", oldReportDir);
                Environment.SetEnvironmentVariable("PATGEOM_RUN_ID", oldRunId);
                Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_BRACES", oldBraces);
                Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_TEXTS", oldTexts);
                Environment.SetEnvironmentVariable("PATGEOM_STRICT", oldStrict);
                try { Directory.Delete(reportDir, true); } catch { }
            }
        }

        private static Polyline MakePolyline(IEnumerable<Point3d> points, double elevation)
        {
            Polyline polyline = new Polyline();
            foreach (Point3d point in points)
            {
                polyline.AddVertexAt(polyline.NumberOfVertices,
                    new Point2d(point.X, point.Y), 0.0, 0.0, 0.0);
            }
            polyline.Elevation = elevation;
            return polyline;
        }

        private static Point3d LocalPoint(
            PatBraceDefinition definition, double along, double offset)
        {
            double dx = definition.Bottom.X - definition.Top.X;
            double dy = definition.Bottom.Y - definition.Top.Y;
            double height = Math.Sqrt(dx * dx + dy * dy);
            double axisX = dx / height;
            double axisY = dy / height;
            double perpX = -axisY;
            double perpY = axisX;
            double signed = offset * definition.Side;
            return new Point3d(
                definition.Top.X + axisX * along + perpX * signed,
                definition.Top.Y + axisY * along + perpY * signed,
                definition.Top.Z);
        }
    }
}
