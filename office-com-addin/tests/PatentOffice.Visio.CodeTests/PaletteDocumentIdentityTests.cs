using System;
using System.Reflection;
using PatentOffice.Visio;
using Xunit;

namespace PatentOffice.Visio.CodeTests
{
    public sealed class PaletteDocumentIdentityTests
    {
        [Fact]
        public void ReopenedDocumentAtSamePathHasNewPaletteIdentity()
        {
            var first = new FakeDocument { FullName = @"C:\test\drawing.vsdx", Name = "drawing.vsdx" };
            var reopened = new FakeDocument { FullName = first.FullName, Name = first.Name };
            var method = typeof(VisioAddIn).Assembly.GetType("PatentOffice.Visio.PaletteForm", true)
                .GetMethod("GetDocumentKey", BindingFlags.Static | BindingFlags.NonPublic);

            var firstKey = (string)method.Invoke(null, new object[] { first });
            var reopenedKey = (string)method.Invoke(null, new object[] { reopened });
            var firstAgainKey = (string)method.Invoke(null, new object[] { first });

            Assert.Equal(firstKey, firstAgainKey);
            Assert.NotEqual(firstKey, reopenedKey);
        }

    }

    public sealed class FakeDocument
    {
        public string FullName { get; set; }
        public string Name { get; set; }
    }
}
