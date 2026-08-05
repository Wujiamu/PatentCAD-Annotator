using PatentMarker.IO;
using PatentMarker.Palette;
using Xunit;

namespace PatentMarker.Tests
{
    public class DictPaletteSessionTests
    {
        [Fact]
        public void LoadProjectsEntriesAndFiltersWithoutUi()
        {
            var session = new DictPaletteSession();
            var dict = new DictModel();
            dict.Entries.Add(new DictEntry { Number = "10", Name = "底座", Occurrences = 2 });
            dict.Entries.Add(new DictEntry { Number = "S1", Name = "支架", Occurrences = 1 });
            session.Load(dict, null);

            Assert.Equal(2, session.AllEntries.Count);
            Assert.Single(session.Filter("s1"));
            Assert.Equal("底座", session.Filter("底")[0].Name);
        }

        [Fact]
        public void LoadComputesDiffAndClearDropsDocumentState()
        {
            var previous = new DictModel();
            previous.Entries.Add(new DictEntry { Number = "10", Name = "旧名称" });
            var current = new DictModel();
            current.Entries.Add(new DictEntry { Number = "10", Name = "新名称" });

            var session = new DictPaletteSession();
            session.Load(current, previous);

            Assert.NotNull(session.CurrentDiff);
            Assert.Single(session.CurrentDiff!);
            session.Clear();
            Assert.Null(session.CurrentDict);
            Assert.Empty(session.AllEntries);
            Assert.Null(session.CurrentDiff);
        }
    }
}
