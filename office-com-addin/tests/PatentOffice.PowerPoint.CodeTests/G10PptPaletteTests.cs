using System;
using System.IO;
using System.Text;
using Xunit;

namespace PatentOffice.PowerPoint.CodeTests
{
    /// <summary>
    /// G10 — R-008 (a transient read failure must not be cached as a content error) and
    /// R-024 (host binding changes must be re-checked on refresh and before actions).
    ///
    /// Layer: L1. The production <c>PaletteForm</c>, <c>PresentationBinding</c> and
    /// <c>DictionaryReader</c> run unmodified against in-memory PowerPoint objects. Real
    /// PowerPoint Undo/Redo, Save As and the installed add-in are NOT covered here.
    /// </summary>
    public sealed class G10PptPaletteTests
    {
        // ---------- R-008 ----------

        [Fact]
        public void G10_R008_SameBytesRecoverAfterOneFailedRead()
        {
            G10Sta.Run(delegate
            {
                using (G10PptPalette palette = G10PptPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10PptPalette.DictionaryJson("1342A", "S1"));
                    palette.Bind(dictionary);
                    palette.Refresh();
                    Assert.True(palette.DictionaryCurrent, "precondition: first read must succeed");
                    Assert.Equal(2, palette.EntryCount);

                    using (palette.LockDictionary(dictionary))
                    {
                        palette.Refresh();
                        Assert.False(palette.DictionaryCurrent, "a sharing violation must pause annotation");
                        Assert.False(palette.MarkEnabled);
                        Assert.False(palette.CheckEnabled);
                    }

                    // Same bytes, same hash, failure removed: the next poll must retry, not replay.
                    palette.Refresh();

                    Assert.True(palette.DictionaryCurrent,
                        "the poll after the failure cleared must read again; status was: " + palette.StatusText);
                    Assert.True(palette.CheckEnabled);
                    Assert.Equal(2, palette.EntryCount);
                }
            });
        }

        [Fact]
        public void G10_R008_FailureCachedAgainstUnchangedBytesIsNotReplayed()
        {
            G10Sta.Run(delegate
            {
                using (G10PptPalette palette = G10PptPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10PptPalette.DictionaryJson("1342A"));
                    palette.Bind(dictionary);
                    palette.Refresh();
                    Assert.True(palette.DictionaryCurrent);

                    // The production panel records "bytes H failed" and then skips every later read of H.
                    palette.SeedLegacyFailureCacheForCurrentBytes();

                    palette.Refresh();

                    Assert.True(palette.DictionaryCurrent,
                        "an unchanged, readable file must be read again instead of replaying a stored failure; " +
                        "status was: " + palette.StatusText);
                    Assert.True(palette.CheckEnabled);
                }
            });
        }

        [Fact]
        public void G10_R008_FileRepairedToNewContentIsReadImmediately()
        {
            G10Sta.Run(delegate
            {
                using (G10PptPalette palette = G10PptPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10PptPalette.DictionaryJson("1342A"));
                    palette.Bind(dictionary);
                    palette.Refresh();
                    Assert.True(palette.DictionaryCurrent);

                    using (palette.LockDictionary(dictionary))
                    {
                        palette.Refresh();
                        Assert.False(palette.DictionaryCurrent);
                    }

                    File.WriteAllBytes(dictionary,
                        new UTF8Encoding(false).GetBytes(G10PptPalette.DictionaryJson("1342A", "S1", "10-1")));
                    palette.Refresh();

                    Assert.True(palette.DictionaryCurrent, "a repaired file with new content must be read");
                    Assert.Equal(3, palette.EntryCount);
                }
            });
        }

        [Fact]
        public void G10_R008_MissingFileRecoveryIsReadWhenItReappears()
        {
            G10Sta.Run(delegate
            {
                using (G10PptPalette palette = G10PptPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10PptPalette.DictionaryJson("1342A"));
                    palette.Bind(dictionary);
                    palette.Refresh();
                    Assert.True(palette.DictionaryCurrent);

                    File.Delete(dictionary);
                    palette.Refresh();
                    Assert.False(palette.DictionaryCurrent, "a missing dictionary must pause annotation");
                    Assert.False(palette.MarkEnabled);

                    File.WriteAllBytes(dictionary,
                        new UTF8Encoding(false).GetBytes(G10PptPalette.DictionaryJson("1342A", "S1")));
                    palette.Refresh();

                    Assert.True(palette.DictionaryCurrent,
                        "a restored file must be picked up by the poll; status was: " + palette.StatusText);
                    Assert.Equal(2, palette.EntryCount);
                }
            });
        }

        [Fact]
        public void G10_R008_CorruptJsonStaysFailedAndDiagnosable()
        {
            G10Sta.Run(delegate
            {
                using (G10PptPalette palette = G10PptPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10PptPalette.DictionaryJson("1342A"));
                    palette.Bind(dictionary);
                    palette.Refresh();
                    Assert.True(palette.DictionaryCurrent);

                    File.WriteAllBytes(dictionary, new UTF8Encoding(false).GetBytes("{\"entries\":[{\"number\":"));
                    palette.Refresh();
                    Assert.False(palette.DictionaryCurrent, "truncated JSON is a content failure");
                    Assert.Contains("读取失败", palette.StatusText);

                    // Still corrupt: the state must stay failed (no silent recovery to empty data).
                    palette.Refresh();
                    Assert.False(palette.DictionaryCurrent);
                    Assert.False(palette.MarkEnabled);
                    Assert.False(palette.CheckEnabled);
                }
            });
        }

        [Fact]
        public void G10_R008_RepeatedFailuresDoNotFloodTheDiagnosticLog()
        {
            G10Sta.Run(delegate
            {
                using (G10PptPalette palette = G10PptPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10PptPalette.DictionaryJson("1342A"));
                    palette.Bind(dictionary);
                    palette.Refresh();
                    Assert.True(palette.DictionaryCurrent);

                    long offset = palette.LogLength();
                    using (palette.LockDictionary(dictionary))
                    {
                        for (int i = 0; i < 6; i++) palette.Refresh();
                        Assert.False(palette.DictionaryCurrent);
                    }

                    string log = palette.ReadLogSince(offset);
                    int failures = G10PptPalette.CountOccurrences(log, "dict.read\tFAIL");
                    Assert.True(failures >= 1, "the first failure must be diagnosable; log was: " + log);
                    Assert.True(failures <= 2,
                        "six polls of the same unchanged failure wrote " + failures +
                        " dict.read FAIL lines (expected at most 2); log was: " + log);
                }
            });
        }

        // ---------- R-024 ----------

        [Fact]
        public void G10_R024_SteadyPollDoesNotFloodTheDiagnosticLog()
        {
            G10Sta.Run(delegate
            {
                using (G10PptPalette palette = G10PptPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10PptPalette.DictionaryJson("1342A"));
                    palette.Bind(dictionary);
                    palette.Refresh();
                    Assert.True(palette.DictionaryCurrent);

                    // Re-resolving the binding every poll must not mean logging every poll.
                    long offset = palette.LogLength();
                    for (int i = 0; i < 6; i++) palette.Refresh();

                    string log = palette.ReadLogSince(offset);
                    Assert.True(G10PptPalette.CountOccurrences(log, "binding.resolve\tPASS") <= 1,
                        "steady polling wrote repeated binding.resolve PASS lines: " + log);
                    Assert.True(G10PptPalette.CountOccurrences(log, "dict.read\tPASS") <= 1,
                        "steady polling wrote repeated dict.read PASS lines: " + log);
                }
            });
        }

        [Fact]
        public void G10_R024_BindingUndoIsReflectedWithoutPresentationChange()
        {
            G10Sta.Run(delegate
            {
                using (G10PptPalette palette = G10PptPalette.Create())
                {
                    string dictionaryA = palette.WriteDictionary("a.dict.json",
                        G10PptPalette.DictionaryJson("1342A"));
                    string dictionaryB = palette.WriteDictionary("b.dict.json",
                        G10PptPalette.DictionaryJson("S1", "10-1"));

                    palette.Bind(dictionaryA);
                    palette.Refresh();
                    Assert.Equal(Path.GetFullPath(dictionaryA), palette.BoundPath);
                    Assert.Equal(1, palette.EntryCount);

                    // Same presentation object, same path, binding changed by the user action.
                    palette.Bind(dictionaryB);
                    palette.Refresh();
                    Assert.Equal(Path.GetFullPath(dictionaryB), palette.BoundPath);
                    Assert.Equal(2, palette.EntryCount);

                    // Undo of that bind: only the stored binding changes back to A.
                    palette.RewriteBinding(dictionaryA);
                    palette.Refresh();

                    Assert.Equal(Path.GetFullPath(dictionaryA), palette.BoundPath);
                    Assert.Equal(Path.GetFullPath(dictionaryA), palette.PathInputText);
                    Assert.Equal(1, palette.EntryCount);
                    Assert.Equal("1342A", palette.RowNumbers()[0]);
                    Assert.True(palette.DictionaryCurrent);
                }
            });
        }

        [Fact]
        public void G10_R024_BindingUndoRedoSequenceKeepsPanelAndBindingInSync()
        {
            G10Sta.Run(delegate
            {
                using (G10PptPalette palette = G10PptPalette.Create())
                {
                    string dictionaryA = palette.WriteDictionary("a.dict.json",
                        G10PptPalette.DictionaryJson("1342A"));
                    string dictionaryB = palette.WriteDictionary("b.dict.json",
                        G10PptPalette.DictionaryJson("S1"));

                    palette.Bind(dictionaryA);
                    palette.Refresh();
                    palette.Bind(dictionaryB);
                    palette.Refresh();
                    Assert.Equal(Path.GetFullPath(dictionaryB), palette.BoundPath);

                    // A -> B -> Undo -> A -> Redo -> B
                    palette.RewriteBinding(dictionaryA);
                    palette.Refresh();
                    Assert.Equal(Path.GetFullPath(dictionaryA), palette.BoundPath);
                    Assert.Equal("1342A", palette.RowNumbers()[0]);

                    palette.RewriteBinding(dictionaryB);
                    palette.Refresh();
                    Assert.Equal(Path.GetFullPath(dictionaryB), palette.BoundPath);
                    Assert.Equal("S1", palette.RowNumbers()[0]);
                }
            });
        }

        [Fact]
        public void G10_R024_BindingRemovalUndoClearsSelectionAndMarkedStats()
        {
            G10Sta.Run(delegate
            {
                using (G10PptPalette palette = G10PptPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10PptPalette.DictionaryJson("1342A", "S1"));
                    palette.AddMarkedShape("1342A");
                    palette.Bind(dictionary);
                    palette.Refresh();
                    palette.SelectFirstEntry();
                    Assert.Equal(1, palette.SelectedCount);
                    Assert.Equal(2, palette.EntryCount);
                    Assert.Equal(1, palette.MarkedNumberCount);

                    // Undo of the whole bind removes the part; the panel must not keep the old rows.
                    palette.RemoveBinding();
                    palette.Refresh();

                    Assert.Null(palette.BoundPath);
                    Assert.Equal("", palette.PathInputText);
                    Assert.Equal(0, palette.EntryCount);
                    Assert.Equal(0, palette.SelectedCount);
                    Assert.Equal(0, palette.MarkedNumberCount);
                    Assert.False(palette.DictionaryCurrent);
                    Assert.False(palette.MarkEnabled);
                    Assert.False(palette.CheckEnabled);
                }
            });
        }

        [Fact]
        public void G10_R024_UnchangedContextStillMarksTheSelectedNumber()
        {
            G10Sta.Run(delegate
            {
                using (G10PptPalette palette = G10PptPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10PptPalette.DictionaryJson("1342A", "S1"));
                    palette.Bind(dictionary);
                    palette.Refresh();
                    palette.SelectFirstEntry();
                    Assert.Equal(1, palette.SelectedCount);

                    // Control case: no binding change, so the action must proceed and apply the
                    // selected number. This proves the guard does not simply block every action.
                    palette.ClickMark();

                    Assert.Equal(1, palette.MarkAttempts);
                    Assert.Equal("1342A", palette.LastAnnotationNumber);
                }
            });
        }

        [Fact]
        public void G10_R024_BindingChangeClearsStaleSelectionBeforeMark()
        {
            G10Sta.Run(delegate
            {
                using (G10PptPalette palette = G10PptPalette.Create())
                {
                    string dictionaryA = palette.WriteDictionary("a.dict.json",
                        G10PptPalette.DictionaryJson("1342A", "S1"));
                    string dictionaryB = palette.WriteDictionary("b.dict.json",
                        G10PptPalette.DictionaryJson("10-1", "10-2"));

                    palette.Bind(dictionaryA);
                    palette.Refresh();
                    palette.SelectFirstEntry();
                    Assert.Equal(1, palette.SelectedCount);

                    // The binding changes behind the panel (Undo/Redo or external edit), then the
                    // user clicks "mark". The stale row from A must not be used for B.
                    palette.RewriteBinding(dictionaryB);
                    palette.ClickMark();

                    Assert.Equal(Path.GetFullPath(dictionaryB), palette.BoundPath);
                    Assert.True(palette.MarkAttempts == 0,
                        "a stale selection from the previous binding must not reach the host; " +
                        "status was: " + palette.StatusText);
                    Assert.Null(palette.LastAnnotationNumber);
                    Assert.Contains("重新选择", palette.StatusText);
                }
            });
        }

        [Fact]
        public void G10_R024_BindingChangeBeforeCheckUsesNewDictionary()
        {
            G10Sta.Run(delegate
            {
                using (G10PptPalette palette = G10PptPalette.Create())
                {
                    string dictionaryA = palette.WriteDictionary("a.dict.json",
                        G10PptPalette.DictionaryJson("1342A", "S1", "10-1"));
                    string dictionaryB = palette.WriteDictionary("b.dict.json",
                        G10PptPalette.DictionaryJson("S1"));

                    palette.Bind(dictionaryA);
                    palette.Refresh();
                    palette.SelectFirstEntry();

                    palette.RewriteBinding(dictionaryB);
                    palette.ClickCheck();

                    Assert.Equal(Path.GetFullPath(dictionaryB), palette.BoundPath);
                    Assert.Equal(1, palette.EntryCount);
                    Assert.Contains("S1", palette.StatusText);
                    Assert.DoesNotContain("1342A", palette.StatusText);
                    Assert.DoesNotContain("10-1", palette.StatusText);
                }
            });
        }

        [Fact]
        public void G10_R024_MultiPresentationSwitchKeepsPerPresentationBinding()
        {
            G10Sta.Run(delegate
            {
                using (G10PptPalette palette = G10PptPalette.Create())
                {
                    string dictionaryA = palette.WriteDictionary("a.dict.json",
                        G10PptPalette.DictionaryJson("1342A"));
                    string dictionaryB = palette.WriteDictionary("b.dict.json",
                        G10PptPalette.DictionaryJson("S1", "10-1"));

                    palette.Bind(dictionaryA);
                    palette.Refresh();
                    Assert.Equal(1, palette.EntryCount);

                    G10Presentation second = new G10Presentation(Path.Combine(palette.Root, "second.pptx"));
                    File.WriteAllBytes(second.FullName, new byte[0]);
                    second.Slides.Add(new G10Slide { SlideID = 257 });
                    palette.Application.Presentations.Add(second);
                    palette.BindPresentation(second, dictionaryB);
                    palette.Refresh();
                    Assert.Equal(2, palette.EntryCount);

                    // Switch back to the first presentation: its own binding must come back.
                    palette.Application.ActivePresentation = palette.Presentation;
                    palette.Refresh();
                    Assert.Equal(Path.GetFullPath(dictionaryA), palette.BoundPath);
                    Assert.Equal(1, palette.EntryCount);
                }
            });
        }

        [Fact]
        public void G10_R024_SaveAsRelativeBindingStillResolvesAfterMove()
        {
            G10Sta.Run(delegate
            {
                using (G10PptPalette palette = G10PptPalette.Create())
                {
                    string nested = Path.Combine(palette.Root, "dicts");
                    Directory.CreateDirectory(nested);
                    string dictionary = Path.Combine(nested, "parts.dict.json");
                    File.WriteAllBytes(dictionary,
                        new UTF8Encoding(false).GetBytes(G10PptPalette.DictionaryJson("1342A")));

                    palette.Bind(dictionary);
                    palette.Refresh();
                    Assert.Equal(Path.GetFullPath(dictionary), palette.BoundPath);

                    // Save As into a sibling folder: the stored binding is relative, so the resolved
                    // path moves with the presentation and must be re-resolved on refresh.
                    string moved = Path.Combine(palette.Root, "moved");
                    Directory.CreateDirectory(moved);
                    string movedDictionary = Path.Combine(moved, "dicts", "parts.dict.json");
                    Directory.CreateDirectory(Path.GetDirectoryName(movedDictionary));
                    File.Copy(dictionary, movedDictionary);

                    string movedPresentation = Path.Combine(moved, "deck.pptx");
                    File.Copy(palette.Presentation.FullName, movedPresentation, true);
                    palette.Presentation.FullName = movedPresentation;

                    palette.Refresh();

                    Assert.Equal(Path.GetFullPath(movedDictionary), palette.BoundPath);
                    Assert.True(palette.DictionaryCurrent);
                    Assert.Equal(1, palette.EntryCount);
                }
            });
        }
    }
}
