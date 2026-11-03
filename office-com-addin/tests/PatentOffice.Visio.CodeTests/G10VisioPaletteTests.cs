using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace PatentOffice.Visio.CodeTests
{
    /// <summary>
    /// G10 — R-008 (transient read failure must not be cached as a content error) and
    /// R-024 (host binding changes must be re-checked on refresh and before actions).
    ///
    /// Layer: L1. The production <c>PaletteForm</c>, <c>VisioDocumentBinding</c> and
    /// <c>DictionaryReader</c> run unmodified against in-memory Visio objects. Real Visio
    /// Undo/Redo, Save As and the installed add-in are NOT covered here.
    /// </summary>
    public sealed class G10VisioPaletteTests
    {
        // ---------- R-008 ----------

        [Fact]
        public void G10_R008_SameBytesRecoverAfterOneFailedRead()
        {
            G10Sta.Run(delegate
            {
                using (G10VisioPalette palette = G10VisioPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10VisioPalette.DictionaryJson("1342A", "S1"));
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

                    Assert.True(palette.DictionaryCurrent, "the poll after the failure cleared must read again");
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
                using (G10VisioPalette palette = G10VisioPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10VisioPalette.DictionaryJson("1342A"));
                    palette.Bind(dictionary);
                    palette.Refresh();
                    Assert.True(palette.DictionaryCurrent);

                    // The production panel records "bytes H failed" and then skips every later read of H.
                    // Reproducing that exact state needs the private fields, so this is a white-box L1
                    // reproduction of the documented defect state, not a user path.
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
        public void G10_R008_StaleErrorIsNotPairedWithANewerHash()
        {
            G10Sta.Run(delegate
            {
                using (G10VisioPalette palette = G10VisioPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10VisioPalette.DictionaryJson("1342A"));
                    palette.Bind(dictionary);
                    palette.Refresh();
                    Assert.True(palette.DictionaryCurrent);

                    // The documented Visio catch path: the error was captured, then a hash of the
                    // already-repaired file was taken and stored next to the older error.
                    palette.SeedLegacyFailureCacheForCurrentBytes();

                    palette.Refresh();
                    palette.Refresh();

                    Assert.True(palette.DictionaryCurrent,
                        "a hash taken after the failure must never validate the older error; status was: " +
                        palette.StatusText);
                    Assert.DoesNotContain("IOException", palette.StatusText);
                }
            });
        }

        [Fact]
        public void G10_R008_FileRepairedToNewContentIsReadImmediately()
        {
            G10Sta.Run(delegate
            {
                using (G10VisioPalette palette = G10VisioPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10VisioPalette.DictionaryJson("1342A"));
                    palette.Bind(dictionary);
                    palette.Refresh();
                    Assert.True(palette.DictionaryCurrent);

                    using (palette.LockDictionary(dictionary))
                    {
                        palette.Refresh();
                        Assert.False(palette.DictionaryCurrent);
                    }

                    File.WriteAllBytes(dictionary,
                        new UTF8Encoding(false).GetBytes(G10VisioPalette.DictionaryJson("1342A", "S1", "10-1")));
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
                using (G10VisioPalette palette = G10VisioPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10VisioPalette.DictionaryJson("1342A"));
                    palette.Bind(dictionary);
                    palette.Refresh();
                    Assert.True(palette.DictionaryCurrent);

                    File.Delete(dictionary);
                    palette.Refresh();
                    Assert.False(palette.DictionaryCurrent, "a missing dictionary must pause annotation");
                    Assert.False(palette.MarkEnabled);

                    File.WriteAllBytes(dictionary,
                        new UTF8Encoding(false).GetBytes(G10VisioPalette.DictionaryJson("1342A", "S1")));
                    palette.Refresh();

                    Assert.True(palette.DictionaryCurrent, "restored file must be picked up by the poll");
                    Assert.Equal(2, palette.EntryCount);
                }
            });
        }

        [Fact]
        public void G10_R008_CorruptJsonStaysFailedAndDiagnosable()
        {
            G10Sta.Run(delegate
            {
                using (G10VisioPalette palette = G10VisioPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10VisioPalette.DictionaryJson("1342A"));
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
                using (G10VisioPalette palette = G10VisioPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10VisioPalette.DictionaryJson("1342A"));
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
                    int failures = G10VisioPalette.CountOccurrences(log, "dict.read\tFAIL");
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
                using (G10VisioPalette palette = G10VisioPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10VisioPalette.DictionaryJson("1342A"));
                    palette.Bind(dictionary);
                    palette.Refresh();
                    Assert.True(palette.DictionaryCurrent);

                    // Re-resolving the binding every poll must not mean logging every poll.
                    long offset = palette.LogLength();
                    for (int i = 0; i < 6; i++) palette.Refresh();

                    string log = palette.ReadLogSince(offset);
                    Assert.True(G10VisioPalette.CountOccurrences(log, "binding.resolve\tPASS") <= 1,
                        "steady polling wrote repeated binding.resolve PASS lines: " + log);
                    Assert.True(G10VisioPalette.CountOccurrences(log, "dict.read\tPASS") <= 1,
                        "steady polling wrote repeated dict.read PASS lines: " + log);
                }
            });
        }

        [Fact]
        public void G10_R024_BindingUndoIsReflectedWithoutDocumentChange()
        {
            G10Sta.Run(delegate
            {
                using (G10VisioPalette palette = G10VisioPalette.Create())
                {
                    string dictionaryA = palette.WriteDictionary("a.dict.json",
                        G10VisioPalette.DictionaryJson("1342A"));
                    string dictionaryB = palette.WriteDictionary("b.dict.json",
                        G10VisioPalette.DictionaryJson("S1", "10-1"));

                    palette.Bind(dictionaryA);
                    palette.Refresh();
                    Assert.Equal(Path.GetFullPath(dictionaryA), palette.BoundPath);
                    Assert.Equal(1, palette.EntryCount);

                    // Same document object, same path, binding changed: bind B (the user action).
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
                using (G10VisioPalette palette = G10VisioPalette.Create())
                {
                    string dictionaryA = palette.WriteDictionary("a.dict.json",
                        G10VisioPalette.DictionaryJson("1342A"));
                    string dictionaryB = palette.WriteDictionary("b.dict.json",
                        G10VisioPalette.DictionaryJson("S1"));

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
                using (G10VisioPalette palette = G10VisioPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10VisioPalette.DictionaryJson("1342A", "S1"));
                    palette.Bind(dictionary);
                    palette.Refresh();
                    palette.SelectFirstEntry();
                    Assert.Equal(1, palette.SelectedCount);
                    Assert.Equal(2, palette.EntryCount);

                    // Undo of the whole bind removes the rows; the panel must not keep the old rows.
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
                using (G10VisioPalette palette = G10VisioPalette.Create())
                {
                    string dictionary = palette.WriteDictionary("parts.dict.json",
                        G10VisioPalette.DictionaryJson("1342A", "S1"));
                    palette.Bind(dictionary);
                    palette.Refresh();
                    palette.SelectFirstEntry();
                    Assert.Equal(1, palette.SelectedCount);

                    // Control case: no binding change, so the action must proceed and apply the
                    // selected number. This proves the guard does not simply block every action.
                    palette.ClickMark();

                    Assert.Equal(1, palette.AnnotationCreations);
                    Assert.Equal("1342A", palette.LastAnnotationNumber);
                    Assert.Equal(1, palette.MarkedNumberCount);
                }
            });
        }

        [Fact]
        public void G10_R024_BindingChangeClearsStaleSelectionBeforeMark()
        {
            G10Sta.Run(delegate
            {
                using (G10VisioPalette palette = G10VisioPalette.Create())
                {
                    string dictionaryA = palette.WriteDictionary("a.dict.json",
                        G10VisioPalette.DictionaryJson("1342A", "S1"));
                    string dictionaryB = palette.WriteDictionary("b.dict.json",
                        G10VisioPalette.DictionaryJson("10-1", "10-2"));

                    palette.Bind(dictionaryA);
                    palette.Refresh();
                    palette.SelectFirstEntry();
                    Assert.Equal(1, palette.SelectedCount);

                    // The binding changes behind the panel (Undo/Redo or external edit), then the
                    // user clicks "mark". The stale row from A must not be used for B.
                    palette.RewriteBinding(dictionaryB);
                    palette.ClickMark();

                    Assert.Equal(Path.GetFullPath(dictionaryB), palette.BoundPath);
                    Assert.Equal(0, palette.AnnotationCreations);
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
                using (G10VisioPalette palette = G10VisioPalette.Create())
                {
                    string dictionaryA = palette.WriteDictionary("a.dict.json",
                        G10VisioPalette.DictionaryJson("1342A", "S1", "10-1"));
                    string dictionaryB = palette.WriteDictionary("b.dict.json",
                        G10VisioPalette.DictionaryJson("S1"));

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
        public void G10_R024_MultiDocumentSwitchKeepsPerDocumentBinding()
        {
            G10Sta.Run(delegate
            {
                using (G10VisioPalette palette = G10VisioPalette.Create())
                {
                    string dictionaryA = palette.WriteDictionary("a.dict.json",
                        G10VisioPalette.DictionaryJson("1342A"));
                    string dictionaryB = palette.WriteDictionary("b.dict.json",
                        G10VisioPalette.DictionaryJson("S1", "10-1"));

                    palette.Bind(dictionaryA);
                    palette.Refresh();
                    Assert.Equal(1, palette.EntryCount);

                    G10VisioDocument second = new G10VisioDocument(
                        Path.Combine(palette.Root, "second.vsdx"), palette.Application);
                    File.WriteAllBytes(second.FullName, new byte[0]);
                    palette.Application.Documents.Add(second);
                    palette.BindDocument(second, dictionaryB);
                    palette.Refresh();
                    Assert.Equal(2, palette.EntryCount);

                    // Switch back to the first document: its own binding must come back.
                    palette.Application.ActiveDocument = palette.Document;
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
                using (G10VisioPalette palette = G10VisioPalette.Create())
                {
                    string nested = Path.Combine(palette.Root, "dicts");
                    Directory.CreateDirectory(nested);
                    string dictionary = Path.Combine(nested, "parts.dict.json");
                    File.WriteAllBytes(dictionary,
                        new UTF8Encoding(false).GetBytes(G10VisioPalette.DictionaryJson("1342A")));

                    palette.Bind(dictionary);
                    palette.Refresh();
                    Assert.Equal(Path.GetFullPath(dictionary), palette.BoundPath);

                    // Save As into a sibling folder: the stored binding is relative, so the
                    // resolved path moves with the document and must be re-resolved on refresh.
                    string moved = Path.Combine(palette.Root, "moved");
                    Directory.CreateDirectory(moved);
                    string movedDictionary = Path.Combine(moved, "dicts", "parts.dict.json");
                    Directory.CreateDirectory(Path.GetDirectoryName(movedDictionary));
                    File.Copy(dictionary, movedDictionary);

                    string movedDocument = Path.Combine(moved, "drawing.vsdx");
                    File.Copy(palette.Document.FullName, movedDocument, true);
                    palette.Document.FullName = movedDocument;
                    palette.Document.Name = Path.GetFileName(movedDocument);

                    palette.Refresh();

                    Assert.Equal(Path.GetFullPath(movedDictionary), palette.BoundPath);
                    Assert.True(palette.DictionaryCurrent);
                    Assert.Equal(1, palette.EntryCount);
                }
            });
        }
    }
}
