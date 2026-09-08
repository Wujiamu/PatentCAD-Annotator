using System;
using System.IO;
using PatentMarker.IO;
using Xunit;

namespace PatentMarker.Tests
{
    public class PatSettingsTests
    {
        [Fact]
        public void Apply_UsesConfigValuesAndResetRestoresDefaults()
        {
            PatSettingsStore.ResetConfigDefaults();
            var config = new PatConfig
            {
                PatStyle = new PatStyleConfig { TextHeight = 4.25 },
                Align = new AlignConfig { MarginToFrame = 7.5 }
            };

            PatSettingsStore.Apply(config);

            Assert.Equal(4.25, PatSettingsStore.Current.TextHeight);
            Assert.Equal(7.5, PatSettingsStore.Current.MarginToFrame);

            PatSettingsStore.ResetConfigDefaults();
            Assert.Equal(PatSettingsStore.DefaultTextHeight, PatSettingsStore.Current.TextHeight);
            Assert.Equal(PatSettingsStore.DefaultMarginToFrame, PatSettingsStore.Current.MarginToFrame);
        }

        [Fact]
        public void Activate_IsolatesRuntimeSettingsByDrawing()
        {
            PatSettingsStore.Activate("C:\\PatentMarkerTests\\drawing-a.dwg");
            PatSettingsStore.Current.HasArrowHead = true;
            PatSettingsStore.Current.ThreePointMode = true;

            PatSettingsStore.Activate("C:\\PatentMarkerTests\\drawing-b.dwg");
            Assert.False(PatSettingsStore.Current.HasArrowHead);
            Assert.True(PatSettingsStore.Current.ThreePointMode);

            PatSettingsStore.Activate("C:\\PatentMarkerTests\\drawing-a.dwg");
            Assert.True(PatSettingsStore.Current.HasArrowHead);
            Assert.True(PatSettingsStore.Current.ThreePointMode);

            PatSettingsStore.Activate("");
            PatSettingsStore.ResetConfigDefaults();
        }

        [Fact]
        public void DictFileVisibility_RoundTripsExplicitChoice()
        {
            string path = Path.Combine(Path.GetTempPath(), "PatentMarkerVisibility_" + Guid.NewGuid().ToString("N") + ".dict.json");
            File.WriteAllText(path, "{}");
            try
            {
                Assert.True(DictFileVisibility.TrySetVisible(path, false, out string error), error);
                Assert.False(DictFileVisibility.IsVisible(path));

                Assert.True(DictFileVisibility.TrySetVisible(path, true, out error), error);
                Assert.True(DictFileVisibility.IsVisible(path));
            }
            finally
            {
                try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
                try { File.Delete(path); } catch { }
            }
        }
    }
}
