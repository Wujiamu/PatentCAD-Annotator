using System;
using PatentMarker.Diagnostics;
using PatentMarker.IO;
using Xunit;

namespace PatentMarker.Tests
{
    /// <summary>
    /// G07 / R-022 and R-026.
    /// R-022: closing a drawing that is not the active one must not reset the
    /// active drawing's runtime settings.
    /// R-026: a real failure line must land in PATDOCTOR's recent errors, and
    /// progress lines or numbers/paths that merely contain the letters must not.
    /// </summary>
    public sealed class G07SettingsAndDiagnosticsTests
    {
        [Fact]
        public void G07_ReleasingInactiveDrawing_KeepsActiveSettings()
        {
            PatSettingsStore.Activate("C:\\PatentMarkerTests\\g07-a.dwg");
            PatSettingsStore.Current.TextHeight = 7.25;
            PatSettingsStore.Current.HasArrowHead = true;

            PatSettingsStore.Activate("C:\\PatentMarkerTests\\g07-b.dwg");
            PatSettingsStore.Activate("C:\\PatentMarkerTests\\g07-a.dwg");
            Assert.Equal(7.25, PatSettingsStore.Current.TextHeight);

            // Closing B must leave A alone.
            PatSettingsStore.Release("C:\\PatentMarkerTests\\g07-b.dwg");
            Assert.Equal(7.25, PatSettingsStore.Current.TextHeight);
            Assert.True(PatSettingsStore.Current.HasArrowHead);

            PatSettingsStore.Activate("C:\\PatentMarkerTests\\g07-b.dwg");
            PatSettingsStore.Activate("");
            PatSettingsStore.ResetConfigDefaults();
        }

        [Fact]
        public void G07_ReleasingActiveDrawing_ResetsCurrent()
        {
            PatSettingsStore.Activate("C:\\PatentMarkerTests\\g07-c.dwg");
            PatSettingsStore.Current.TextHeight = 9.5;

            PatSettingsStore.Release("C:\\PatentMarkerTests\\g07-c.dwg");

            Assert.Equal(PatSettingsStore.DefaultTextHeight, PatSettingsStore.Current.TextHeight);
            PatSettingsStore.Activate("");
            PatSettingsStore.ResetConfigDefaults();
        }

        [Fact]
        public void G07_ReleasingRepeatedlyOrUnknownDrawing_IsHarmless()
        {
            PatSettingsStore.Activate("C:\\PatentMarkerTests\\g07-d.dwg");
            PatSettingsStore.Current.TextHeight = 5.5;

            PatSettingsStore.Release("C:\\PatentMarkerTests\\g07-unknown.dwg");
            PatSettingsStore.Release("C:\\PatentMarkerTests\\g07-unknown.dwg");
            Assert.Equal(5.5, PatSettingsStore.Current.TextHeight);

            PatSettingsStore.Release("C:\\PatentMarkerTests\\g07-d.dwg");
            Assert.Equal(PatSettingsStore.DefaultTextHeight, PatSettingsStore.Current.TextHeight);
            PatSettingsStore.Activate("");
            PatSettingsStore.ResetConfigDefaults();
        }

        [Fact]
        public void G07_NativeTrackerFail_IsRecordedAsRecentError()
        {
            PatDiagnostics.Reset();
            PatDiagnostics.OnRawLog("Native annotation tracker FAIL: injected");
            Assert.Equal(1, PatDiagnostics.Count);
            Assert.Contains("injected", PatDiagnostics.Snapshot()[0].Message);
            PatDiagnostics.Reset();
        }

        [Fact]
        public void G07_FailedMarker_IsRecorded()
        {
            PatDiagnostics.Reset();
            PatDiagnostics.OnRawLog("something FAILED");
            Assert.Equal(1, PatDiagnostics.Count);
            PatDiagnostics.Reset();
        }

        [Fact]
        public void G07_NormalProgressLine_IsNotAnError()
        {
            PatDiagnostics.Reset();
            PatDiagnostics.OnRawLog("PATDOCTOR START");
            PatDiagnostics.OnRawLog("Native annotation tracker START; documents=1");
            PatDiagnostics.OnRawLog("Native annotation tracker synced command=LINE targets=3 document=Drawing1.dwg");
            Assert.Equal(0, PatDiagnostics.Count);
            PatDiagnostics.Reset();
        }

        [Fact]
        public void G07_LettersInsideWordsOrPaths_AreNotErrors()
        {
            PatDiagnostics.Reset();
            // Only the new standalone-FAIL rule is exercised here; the older
            // "failed" substring rule is unchanged.
            PatDiagnostics.OnRawLog("opened C:\\work\\PATFAIL1\\drawing.dwg");
            PatDiagnostics.OnRawLog("number=30123 count=40");
            PatDiagnostics.OnRawLog("PATCHECK summary: FAIL count=0");
            Assert.Equal(1, PatDiagnostics.Count);
            PatDiagnostics.Reset();
        }
    }
}
