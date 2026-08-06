using PatentMarker.IO;
using PatentMarker.Palette;
using Xunit;

namespace PatentMarker.Tests
{
    public sealed class DictPaletteWorkflowTests
    {
        [Fact]
        public void FindEntryUsesSharedNumberIdentityRules()
        {
            DictModel dict = new DictModel();
            DictEntry expected = new DictEntry { Number = " 1342A ", Name = "测试" };
            dict.Entries.Add(expected);

            DictEntry actual = new DictPaletteWorkflow().FindEntry(dict, "1342a");

            Assert.Same(expected, actual);
        }

        [Fact]
        public void FindEntryReturnsNullForMissingOrEmptyDictionary()
        {
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();

            Assert.Null(workflow.FindEntry(null, "1"));
            Assert.Null(workflow.FindEntry(new DictModel(), "1"));
        }
    }
}
