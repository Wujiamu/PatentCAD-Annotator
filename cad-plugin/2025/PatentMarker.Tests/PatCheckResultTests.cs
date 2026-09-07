using System.Collections.Generic;
using PatentMarker.Commands;
using Xunit;

namespace PatentMarker.Tests
{
    public class PatCheckResultTests
    {
        [Fact]
        public void ResultsAreIndependentPerDocumentAndReleaseRemovesState()
        {
            object firstDocument = new object();
            object secondDocument = new object();

            PatCheckResult.ClearAll();
            PatCheckResult.SetUnmarked(firstDocument, new List<string> { "1" });
            PatCheckResult.SetUnmarked(secondDocument, new List<string> { "2" });

            Assert.True(PatCheckResult.HasResultFor(firstDocument));
            Assert.True(PatCheckResult.IsUnmarked(firstDocument, "1"));
            Assert.False(PatCheckResult.IsUnmarked(firstDocument, "2"));
            Assert.True(PatCheckResult.IsUnmarked(secondDocument, "2"));
            Assert.False(PatCheckResult.IsUnmarked(secondDocument, "1"));
            int secondVersion = PatCheckResult.GetVersion(secondDocument);
            PatCheckResult.Clear(firstDocument);
            Assert.False(PatCheckResult.HasResultFor(firstDocument));
            Assert.True(PatCheckResult.HasResultFor(secondDocument));
            Assert.Equal(secondVersion, PatCheckResult.GetVersion(secondDocument));

            PatCheckResult.Release(firstDocument);
            Assert.False(PatCheckResult.HasResultFor(firstDocument));
            Assert.Equal(0, PatCheckResult.GetVersion(firstDocument));

            PatCheckResult.ClearAll();
        }

        [Fact]
        public void UnmarkedLookupIsCaseInsensitiveLikeNumberIdentity()
        {
            object document = new object();
            PatCheckResult.ClearAll();
            PatCheckResult.SetUnmarked(document, new List<string> { "1342A" });

            Assert.True(PatCheckResult.IsUnmarked(document, "1342a"));

            PatCheckResult.ClearAll();
        }
    }
}
