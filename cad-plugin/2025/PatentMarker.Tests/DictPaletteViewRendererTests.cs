using PatentMarker.Commands;
using PatentMarker.IO;
using PatentMarker.Palette;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace PatentMarker.Tests
{
    public class DictPaletteViewRendererTests
    {
        [Fact]
        public void CompareRowsKeepRemovedEntriesAndFilteringKeepsDiffColumns()
        {
            DictModel oldModel = new DictModel();
            oldModel.Entries.Add(new DictEntry { Number = "1", Name = "old-name" });
            oldModel.Entries.Add(new DictEntry { Number = "2", Name = "removed-name" });

            DictModel newModel = new DictModel();
            newModel.Entries.Add(new DictEntry { Number = "1", Name = "new-name" });
            newModel.Entries.Add(new DictEntry { Number = "3", Name = "added-name" });

            DictPaletteSession session = new DictPaletteSession();
            session.Load(newModel, oldModel);

            using (ListView list = CreateListView())
            using (Label dictInfo = new Label())
            using (Label status = new Label())
            {
                DictPaletteViewRenderer renderer = new DictPaletteViewRenderer(
                    list, dictInfo, status, list.Columns[3], list.Columns[4]);
                PatCheckResult.Clear();
                renderer.RenderDictionary(newModel, session, true);

                Assert.Equal(3, list.Items.Count);
                ListViewItem removed = Find(list, "2");
                Assert.NotNull(removed);
                Assert.Equal(5, removed.SubItems.Count);
                Assert.Equal(Color.LightPink, removed.BackColor);
                Assert.True(((PaletteEntry)removed.Tag).IsRemoved);

                renderer.RenderFiltered(session.Filter("new"), session.CurrentDiff, true);
                Assert.Single(list.Items);
                Assert.Equal(5, list.Items[0].SubItems.Count);
                Assert.Equal("1", list.Items[0].SubItems[3].Text);
                Assert.Equal("old-name", list.Items[0].SubItems[4].Text);
                Assert.Equal(Color.LightBlue, list.Items[0].BackColor);
            }
        }

        [Fact]
        public void RendererUsesTheSelectedDocumentCheckResult()
        {
            DictModel model = new DictModel();
            model.Entries.Add(new DictEntry { Number = "1", Name = "part" });
            DictPaletteSession session = new DictPaletteSession();
            session.Load(model, null);
            object firstDocument = new object();
            object secondDocument = new object();

            using (ListView list = CreateListView())
            using (Label dictInfo = new Label())
            using (Label status = new Label())
            {
                DictPaletteViewRenderer renderer = new DictPaletteViewRenderer(
                    list, dictInfo, status, list.Columns[3], list.Columns[4]);
                PatCheckResult.ClearAll();
                PatCheckResult.SetUnmarked(firstDocument, new List<string> { "1" });
                renderer.SetCheckDocumentKey(firstDocument);
                renderer.RenderDictionary(model, session, false);
                Assert.Equal(Color.DarkOrange, list.Items[0].ForeColor);

                renderer.SetCheckDocumentKey(secondDocument);
                renderer.RenderDictionary(model, session, false);
                Assert.NotEqual(Color.DarkOrange, list.Items[0].ForeColor);
            }

            PatCheckResult.ClearAll();
        }

        private static ListView CreateListView()
        {
            ListView list = new ListView();
            list.View = View.Details;
            list.Columns.Add("Number");
            list.Columns.Add("Name");
            list.Columns.Add("Count");
            list.Columns.Add("Old#");
            list.Columns.Add("Old Name");
            return list;
        }

        private static ListViewItem Find(ListView list, string number)
        {
            foreach (ListViewItem item in list.Items)
            {
                if (item.Text == number || item.Text == "△ " + number)
                    return item;
            }
            return null;
        }
    }
}
