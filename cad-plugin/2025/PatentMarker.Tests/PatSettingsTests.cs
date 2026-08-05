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
            Assert.False(PatSettingsStore.Current.ThreePointMode);

            PatSettingsStore.Activate("C:\\PatentMarkerTests\\drawing-a.dwg");
            Assert.True(PatSettingsStore.Current.HasArrowHead);
            Assert.True(PatSettingsStore.Current.ThreePointMode);

            PatSettingsStore.Activate("");
            PatSettingsStore.ResetConfigDefaults();
        }
    }
}
