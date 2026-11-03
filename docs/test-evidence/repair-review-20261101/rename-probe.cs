using System;
using System.Collections.Generic;
using PatentMarker.IO;
using PatentMarker.I18n;
namespace PatentMarker {public static class PatentMarkerApp{public static void RawLog(string value){}}}
namespace PatentMarker.I18n {public static class Strings {public const string Status_NumberSyncFailed="Status_NumberSyncFailed";
public const string Status_NumberSyncedCmd="Status_NumberSyncedCmd";
public const string Status_NumberSynced="Status_NumberSynced";
public const string Dict_Changed="Dict_Changed";}}
namespace PatentMarker.IO {
 public class DictEntry {public string Number; public string Name;}
 public class DictModel {public List<DictEntry> Entries=new List<DictEntry>();}
 public class FakeEditor {public void Regen(){} public void WriteMessage(string value){}}
 public class FakeDocument {public FakeEditor Editor=new FakeEditor();}
 public static class RuntimeHost {public static FakeDocument ActiveDocument;}
}
namespace PatentMarker.Palette {public sealed class DictPaletteRenameResult
    {
        public int Matched;
        public int Changed;
        public bool Committed;
        public readonly List<string> Failures = new List<string>();
        public string Error = "";

        public int Failed { get { return Matched - Changed; } }
        public bool Complete { get { return Committed && Matched > 0 && Failed == 0; } }
    } public static class DictPaletteCadService {
  public static Queue<DictPaletteRenameResult> Results=new Queue<DictPaletteRenameResult>();
  public static List<string> Calls=new List<string>();
  public static DictPaletteRenameResult RenameNumber(FakeDocument d,string a,string b){Calls.Add(a+">"+b);return Results.Dequeue();}
 }
 public class ProbeWorkflow {public DictEntry FindEntry(DictModel dict, string number)
        {
            if (dict == null || dict.Entries == null) return null;
            foreach (DictEntry entry in dict.Entries)
            {
                if (NumberIdentity.AreEqual(entry.Number, number)) return entry;
            }
            return null;
        } }
 public class LabelStub {public string Text;}
 public class ButtonStub {public bool Visible;}
 public class RenameProbe {
  private string _pendingRenameOld; private string _pendingRenameNew; private object _pendingRenameDocument;
  private DictModel _currentDict; private ProbeWorkflow _workflow=new ProbeWorkflow();
  private LabelStub _lblStatus=new LabelStub(); private ButtonStub _btnRenameRetry=new ButtonStub();
  private void ReportRenamePending(string a,string b,string detail){}private int RenameLeadersInDrawing(string oldNumber, string newNumber)
        {
            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return 0;

            DictPaletteRenameResult result;
            try
            {
                result = DictPaletteCadService.RenameNumber(doc, oldNumber, newNumber);
            }
            catch (System.Exception ex)
            {
                PatentMarkerApp.RawLog("RenameLeadersInDrawing error: " + ex.Message);
                _lblStatus.Text = string.Format(Strings.Status_NumberSyncFailed, ex.Message);
                return 0;
            }

            if (result.Complete)
            {
                doc.Editor.Regen();
                doc.Editor.WriteMessage(string.Format(
                    Strings.Status_NumberSyncedCmd, result.Changed, oldNumber, newNumber));
                _lblStatus.Text = string.Format(
                    Strings.Status_NumberSynced, result.Changed, oldNumber, newNumber);
                return result.Changed;
            }

            if (result.Matched == 0 && result.Error.Length == 0)
            {
                // 图纸里没有旧号标注：字典照常生效，不谎称同步过。
                _lblStatus.Text = string.Format(Strings.Status_NumberSynced, 0, oldNumber, newNumber);
                return 0;
            }

            PatentMarkerApp.RawLog(string.Format(
                "Renumber drawing sync incomplete: old={0} new={1} matched={2} changed={3} error={4} {5}",
                oldNumber, newNumber, result.Matched, result.Changed, result.Error,
                string.Join(" | ", result.Failures.ToArray())));
            string detail = result.Error.Length > 0 ? result.Error : Strings.Status_NumberSyncFailed;
            ReportRenamePending(oldNumber, newNumber, detail);
            _pendingRenameOld = oldNumber;
            _pendingRenameNew = newNumber;
            _pendingRenameDocument = doc;
            ShowRenameRetry(true);
            return result.Changed;
        }
private void RetryRenameInDrawing()
        {
            if (_pendingRenameOld == null || _pendingRenameNew == null) return;
            if (_pendingRenameDocument == null ||
                !object.ReferenceEquals(_pendingRenameDocument, IO.RuntimeHost.ActiveDocument))
            {
                _lblStatus.Text = Strings.Dict_Changed;
                return;
            }
            DictEntry entry = _currentDict != null
                ? _workflow.FindEntry(_currentDict, _pendingRenameNew) : null;
            if (entry == null || !NumberIdentity.AreEqual(entry.Number, _pendingRenameNew))
            {
                _lblStatus.Text = Strings.Dict_Changed;
                return;
            }
            string oldNumber = _pendingRenameOld;
            string newNumber = _pendingRenameNew;
            ClearRenameRetry();
            RenameLeadersInDrawing(oldNumber, newNumber);
        }
private void ClearRenameRetry()
        {
            _pendingRenameOld = null;
            _pendingRenameNew = null;
            _pendingRenameDocument = null;
            ShowRenameRetry(false);
        }
private void ShowRenameRetry(bool visible)
        {
            if (_btnRenameRetry == null) return;
            _btnRenameRetry.Visible = visible;
        }  public static string[] Run(){var output=new List<string>();
   foreach(string c in new string[]{"same_context","different_document","different_dictionary_same_new_number"}){
    DictPaletteCadService.Results.Clear();DictPaletteCadService.Calls.Clear();
    var p=new RenameProbe();var original=new FakeDocument();RuntimeHost.ActiveDocument=c=="different_document"?new FakeDocument():original;
    p._pendingRenameOld="301";p._pendingRenameNew="302";p._pendingRenameDocument=original;
    p._currentDict=new DictModel();p._currentDict.Entries.Add(new DictEntry{Number="302",Name=c=="different_dictionary_same_new_number"?"unrelated entry from another dictionary":"original part"});
    DictPaletteCadService.Results.Enqueue(new DictPaletteRenameResult{Matched=1,Changed=1,Committed=true});
    p.RetryRenameInDrawing();int expected=c=="same_context"?1:0;int actual=DictPaletteCadService.Calls.Count;
    output.Add((actual==expected?"PASS":"FAIL")+"|rename_retry_"+c+"|expected_calls="+expected+"|actual_calls="+actual);
   }
   DictPaletteCadService.Results.Clear();DictPaletteCadService.Calls.Clear();
   var chained=new RenameProbe();RuntimeHost.ActiveDocument=new FakeDocument();
   chained._currentDict=new DictModel();var entry=new DictEntry{Number="302",Name="part"};chained._currentDict.Entries.Add(entry);
   DictPaletteCadService.Results.Enqueue(new DictPaletteRenameResult{Matched=1,Error="refused"});
   chained.RenameLeadersInDrawing("301","302");
   entry.Number="303";
   DictPaletteCadService.Results.Enqueue(new DictPaletteRenameResult{Matched=0,Committed=true});
   chained.RenameLeadersInDrawing("302","303");
   int before=DictPaletteCadService.Calls.Count;chained.RetryRenameInDrawing();
   bool ok=chained._pendingRenameNew=="303" && DictPaletteCadService.Calls.Count>before;
   output.Add((ok?"PASS":"FAIL")+"|rename_chain_301_302_303|dictionary=303|pending_new="+chained._pendingRenameNew+"|retry_service_calls="+(DictPaletteCadService.Calls.Count-before));
   return output.ToArray();
  }
 }
}
namespace PatentMarker.IO
{
    /// <summary>
    /// Centralized comparison rules for patent marking numbers.
    /// Numbers are trimmed and compared case-insensitively everywhere. Comparer
    /// applies the same normalization as AreEqual, including when used by a set
    /// or dictionary without pre-normalizing its keys.
    /// </summary>
    public static class NumberIdentity
    {
        private static readonly IEqualityComparer<string> NumberComparer = new NormalizedComparer();

#if NET8_0_OR_GREATER
        public static string Normalize(string? value)
#else
        public static string Normalize(string value)
#endif
        {
            return value == null ? "" : value.Trim();
        }

#if NET8_0_OR_GREATER
        public static bool AreEqual(string? left, string? right)
#else
        public static bool AreEqual(string left, string right)
#endif
        {
            return string.Equals(
                Normalize(left), Normalize(right),
                StringComparison.OrdinalIgnoreCase);
        }

        public static IEqualityComparer<string> Comparer
        {
            get { return NumberComparer; }
        }

        private sealed class NormalizedComparer : IEqualityComparer<string>
        {
#if NET8_0_OR_GREATER
            public bool Equals(string? left, string? right)
#else
            public bool Equals(string left, string right)
#endif
            {
                return AreEqual(left, right);
            }

            public int GetHashCode(string value)
            {
                return StringComparer.OrdinalIgnoreCase.GetHashCode(Normalize(value));
            }
        }
    }
}
