using System.Reflection;
using System.Text;
using System.Windows.Forms;
using PatentMarker.IO;
using PatentMarker.Palette;
using Doc = Autodesk.AutoCAD.ApplicationServices.Document;

internal static class Review
{
    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    static readonly string Root = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
    static Doc Active;
    static int passed, failed, blocked;
    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, Private).GetValue(o);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethods(Private)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(o, args);
    static void Check(string name, bool ok, string detail = "")
    { if (ok) passed++; else failed++; Console.WriteLine((ok ? "PASS" : "FAIL") + "|" + name + "|" + detail); }
    static void Run(string name, Action action)
    { try { action(); } catch(Exception ex) { blocked++; Console.WriteLine("SKIP/BLOCKED|" + name + "|" + ex.GetBaseException().Message); } }
    static string PathOf(Doc d) => Path.ChangeExtension(d.Name, ".dict.json");
    static void Write(string path, params string[] numbers)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, "{\"metadata\":{},\"entries\":[" + string.Join(",", numbers.Select(n =>
            "{\"number\":\"" + n + "\",\"name\":\"part\"}")) + "],\"warnings\":[]}", new UTF8Encoding(false));
    }
    sealed class Harness : IDisposable
    {
        public readonly DictPaletteControl Control = new DictPaletteControl();
        public readonly string Dir = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        public Doc A;
        public Harness()
        {
            Field<System.Windows.Forms.Timer>(Control,"_autoRefreshTimer").Stop();
            DictLoader.InvalidateCache(); ConfigLoader.Current = null; DictPaletteCadService.Reset();
            A = Drawing("a", "301", "401"); Select(A);
        }
        public Doc Drawing(string name, params string[] numbers)
        { var d = new Doc { Name = Path.Combine(Dir, name + ".dwg") }; Write(PathOf(d), numbers); return d; }
        public DictModel Model => (DictModel)typeof(DictPaletteControl).GetProperty("_currentDict",Private).GetValue(Control);
        public PendingRenameRegistry Registry => Field<PendingRenameRegistry>(Control,"_pendingRenames");
        public void Select(Doc d, bool cold = false)
        {
            Active = d; if(cold) DictLoader.InvalidateCache();
            var model = DictLoader.LoadForCurrentDrawing();
            if(model == null) Control.ShowNoDict(); else Control.LoadDict(model, false);
        }
        public bool Edit(string oldNumber, string newNumber, string name = null)
        {
            var entry=Model.Entries.Single(x=>NumberIdentity.AreEqual(x.Number, oldNumber));
            using(var dialog=new EditEntryDialog(Model,entry,PathOf(Active),Active,
                DictLoader.GetIdentity(Model,PathOf(Active)),new PendingRenameGuard(Registry,Active,PathOf(Active))))
            {
                Field<TextBox>(dialog,"_txtNumber").Text=newNumber;
                if(name!=null) Field<TextBox>(dialog,"_txtName").Text=name;
                Call(dialog,"BtnSave_Click",null,EventArgs.Empty);
                return dialog.DialogResult==DialogResult.OK;
            }
        }
        public RenameSyncContext Context() => RenameSyncContext.Capture(Active,PathOf(Active),DictLoader.GetIdentity(Model,PathOf(Active)));
        public void Sync(string oldNumber,string newNumber,RenameSyncContext c=null)
        { Call(Control,"RenameLeadersInDrawing",oldNumber,newNumber,c??Context()); }
        public void Debt(string oldNumber="301",string newNumber="302")
        {
            if(!Edit(oldNumber,newNumber)) throw new Exception("Fixture real edit failed");
            Select(Active);
            DictPaletteCadService.Enqueue(new DictPaletteRenameResult{Matched=1,Error="injected CAD failure"});
            Sync(oldNumber,newNumber);
            if(Control.CurrentPendingRename==null) throw new Exception("Fixture failed to register real failed sync");
            DictPaletteCadService.Reset();
        }
        public void Retry() => Call(Control,"BtnRenameRetry_Click",null,EventArgs.Empty);
        public bool Buttons => Field<Button>(Control,"_btnRenameRetry").Visible && Field<Button>(Control,"_btnRenameDiscard").Visible;
        public void Success() => DictPaletteCadService.Enqueue(new DictPaletteRenameResult{Matched=1,Changed=1,Committed=true});
        public void Dispose(){Control.Dispose();}
    }
    static void PendingMatrix()
    {
        Run("N1",()=>{using var h=new Harness(); h.Debt();
            string before=DictFileVisibility.ReadIdentity(PathOf(Active));
            Check("N1-related-block",!h.Edit("302","303")&&before==DictFileVisibility.ReadIdentity(PathOf(Active)));
            Check("N1-unrelated-block",!h.Edit("401","402")&&before==DictFileVisibility.ReadIdentity(PathOf(Active)));
            Check("N1-name-allowed",h.Edit("302","302","renamed part")&&h.Control.HasPendingRename);
            h.Select(Active);h.Success(); h.Retry(); Check("N1-name-change-first-retry-refuses",DictPaletteCadService.Calls.Count==0);
            h.Retry();Check("N1-name-change-confirm-retries",DictPaletteCadService.Calls.Count==1&&!h.Control.HasPendingRename);
            Check("N1-after-complete-edit",h.Edit("401","402"));
        });
        foreach(string content in new[]{"has-new","missing-new","bad"}) foreach(string cache in new[]{"stale","warm","cold"})
        Run("N3-"+content+"-"+cache,()=>{using var h=new Harness();h.Debt();
            string path=PathOf(Active);
            if(content=="bad") File.WriteAllText(path,"{broken",new UTF8Encoding(false));
            else Write(path,content=="has-new"?new[]{"302","999"}:new[]{"999"});
            if(cache!="stale")h.Select(Active,cache=="cold");
            h.Success();h.Retry();h.Retry();
            bool allowed=content=="has-new";
            Check("N3-"+content+"-"+cache,DictPaletteCadService.Calls.Count==(allowed?1:0),
                "expected-calls="+(allowed?1:0)+"|actual-calls="+DictPaletteCadService.Calls.Count+"|pending="+h.Control.HasPendingRename);
        });
        foreach(string change in new[]{"stable","content","deleted","document","save-as"})
        Run("N2-"+change,()=>{using var h=new Harness(); if(!h.Edit("301","302"))throw new Exception("edit failed");h.Select(Active);
            var context=h.Context();Doc original=Active;
            if(change=="content")Write(PathOf(Active),"302","999");
            if(change=="deleted")File.Delete(PathOf(Active));
            if(change=="document")Active=h.Drawing("b","999");
            if(change=="save-as"){var b=h.Drawing("b","999");Active.Name=b.Name;}
            h.Success();h.Sync("301","302",context);
            bool allowed=change=="stable";
            Check("N2-"+change,DictPaletteCadService.Calls.Count==(allowed?1:0),"calls="+DictPaletteCadService.Calls.Count);
            if(!allowed) Check("N2-"+change+"-keeps-debt",h.Registry.Get(original)!=null);
        });
        foreach(bool discard in new[]{false,true})
        Run("N4-activation-"+discard,()=>{using var h=new Harness();h.Debt();Doc a=Active;
            var b=h.Drawing("b","501","601");h.Select(b);h.Debt("501","502");h.Select(a);
            if(discard)Field<Button>(h.Control,"_btnRenameDiscard").PerformClick();else{h.Success();h.Retry();}
            Check("N4-A-resolved-"+discard,!h.Control.HasPendingRename);
            h.Select(b);
            Check("N4-B-buttons-after-A-resolved-"+discard,h.Control.HasPendingRename&&h.Buttons,
                "pending="+h.Control.HasPendingRename+"|buttons="+h.Buttons);
            h.Control.ReleaseAllPendingRenames();Check("N4-release-all-"+discard,h.Registry.Count==0&&!h.Buttons);
        });
        Run("N4-no-debt-buttons",()=>{using var h=new Harness();h.Debt();
            var b=h.Drawing("b","999");h.Select(b);
            Check("N4-no-debt-hides-buttons",!h.Control.HasPendingRename&&!h.Buttons,"pending="+h.Control.HasPendingRename+"|buttons="+h.Buttons);
            File.Delete(PathOf(b));h.Select(b);
            Check("N4-no-dictionary-hides-buttons",!h.Control.HasPendingRename&&!h.Buttons,"buttons="+h.Buttons);
        });
        Run("N4-confirm-phase",()=>{using var h=new Harness();h.Debt();Doc a=Active;
            var b=h.Drawing("b","501");h.Select(b);h.Debt("501","502");
            h.Select(a);Write(PathOf(a),"302","999");h.Retry();
            Write(PathOf(b),"502","888");h.Select(b);h.Success();h.Retry();
            Check("N4-confirm-not-inherited",DictPaletteCadService.Calls.Count==0,
                "expected-first-retry-calls=0|actual="+DictPaletteCadService.Calls.Count);
        });
        Run("N4-close-after-save-as",()=>{using var h=new Harness();h.Debt();Doc a=Active;
            var b=h.Drawing("b","999");
            Call(h.Control,"ReleasePendingRenameForDrawing",b.Name,b);
            Check("N4-closing-unrelated-keeps-A",h.Registry.Get(a)!=null);
            string oldName=a.Name;a.Name=b.Name;h.Select(a);
            var reopenedOldPath=new Doc{Name=oldName};
            Call(h.Control,"ReleasePendingRenameForDrawing",oldName,reopenedOldPath);
            Check("N4-close-reused-old-path-keeps-renamed-doc",h.Registry.Get(a)!=null,
                "pending-A="+(h.Registry.Get(a)!=null));
        });
        Run("N5-results",()=>{using var h=new Harness();h.Debt();
            DictPaletteCadService.Enqueue(new DictPaletteRenameResult{Matched=2,Changed=0,Error="transaction aborted"});h.Retry();
            Check("N5-failure-keeps",h.Control.HasPendingRename);
            DictPaletteCadService.Enqueue(new DictPaletteRenameResult{Matched=0,Committed=true});h.Retry();
            Check("N5-zero-match-completes",!h.Control.HasPendingRename);
        });
        foreach(bool containsNew in new[]{false,true})foreach(bool refresh in new[]{false,true})
        Run("N3-save-as-"+containsNew+refresh,()=>{using var h=new Harness();h.Debt();
            var b=h.Drawing("b",containsNew?new[]{"302","999"}:new[]{"999"});Active.Name=b.Name;
            if(refresh)h.Select(Active);
            h.Success();h.Retry();
            Check("N3-save-as-first-refuses-"+containsNew+refresh,DictPaletteCadService.Calls.Count==0,"calls="+DictPaletteCadService.Calls.Count);
            h.Retry();Check("N3-save-as-confirm-"+containsNew+refresh,DictPaletteCadService.Calls.Count==(containsNew?1:0),"calls="+DictPaletteCadService.Calls.Count);
        });
        foreach(string change in new[]{"stable","replacement","bad","deleted","save-as"})
        Run("N2-real-edit-"+change,()=>{using var h=new Harness();
            h.Control.CreateControl();var list=Field<ListView>(h.Control,"_lstEntries");var handle=list.Handle;
            foreach(ListViewItem item in list.Items)item.Selected=((PaletteEntry)item.Tag).Number=="301";
            if(list.SelectedItems.Count!=1)throw new Exception("Fixture selection not ready");
            bool opened=false,saved=false;h.Success();
            using(var pump=new System.Windows.Forms.Timer()){pump.Interval=40;pump.Tick+=(sender,args)=>{
                var d=Application.OpenForms.OfType<EditEntryDialog>().FirstOrDefault();if(d==null)return;
                pump.Stop();opened=true;Field<TextBox>(d,"_txtNumber").Text="302";Call(d,"BtnSave_Click",null,EventArgs.Empty);saved=d.DialogResult==DialogResult.OK;
                if(!saved){d.DialogResult=DialogResult.Cancel;d.Close();return;}
                if(change=="replacement")Write(PathOf(Active),"999");
                if(change=="bad")File.WriteAllText(PathOf(Active),"{broken after save",new UTF8Encoding(false));
                if(change=="deleted")File.Delete(PathOf(Active));
                if(change=="save-as"){var b=h.Drawing("b","999");Active.Name=b.Name;}
            };pump.Start();Call(h.Control,"EditSelectedEntry");pump.Stop();}
            Check("N2-real-edit-open-saved-"+change,opened&&saved,"opened="+opened+"|saved="+saved);
            Check("N2-real-edit-sync-"+change,DictPaletteCadService.Calls.Count==(change=="stable"?1:0),"calls="+DictPaletteCadService.Calls.Count);
            if(change!="stable")Check("N2-real-edit-retains-debt-"+change,h.Control.HasPendingRename);
        });
    }
    static DictConflict.RecoverySelection Selection(Harness h,bool current,bool backup)
    {
        Write(PathOf(Active),"301");
        string b=PathOf(Active)+".word-20261102-120000.bak";Write(b,"201");
        if(!current)File.WriteAllText(PathOf(Active),"{bad current",new UTF8Encoding(false));
        if(!backup)File.WriteAllText(b,"{bad backup",new UTF8Encoding(false));
        var s=DictConflict.CaptureSelection(PathOf(Active),b);s.Document=Active;return s;
    }
    static void ArbitrationMatrix()
    {
        foreach(bool current in new[]{false,true})foreach(bool backup in new[]{false,true})foreach(string action in new[]{"keep","restore","later"})
        Run("A1-"+current+backup+action,()=>{using var h=new Harness();var s=Selection(h,current,backup);
            using(var d=new ArbitrateDialog(s)){
                Check("A1-buttons-"+current+backup+action,Field<Button>(d,"_btnKeepWord").Enabled==current&&Field<Button>(d,"_btnRestoreCad").Enabled==backup&&Field<Button>(d,"_btnLater").Enabled);
            }
            string before=DictFileVisibility.ReadIdentity(s.DictPath);
            if(action=="later")Check("A1-later-"+current+backup,before==DictFileVisibility.ReadIdentity(s.DictPath)&&File.Exists(s.BackupPath));
            else{
                var o=action=="keep"?DictConflict.KeepSelectedVersion(s,DictPaletteWorkflow.ContextGuard(s)):DictConflict.RestoreSelectedBackup(s,DictPaletteWorkflow.ContextGuard(s));
                bool allowed=action=="keep"?current:backup;
                Check("A1-action-"+current+backup+action,o.Succeeded==allowed&&(allowed?!File.Exists(s.BackupPath):before==DictFileVisibility.ReadIdentity(s.DictPath)&&File.Exists(s.BackupPath)),"success="+o.Succeeded+"|error="+o.Error);
            }
        });
        foreach(string drift in new[]{"current-change","backup-change","current-delete","backup-delete","document","path"})foreach(string action in new[]{"keep","restore"})
        Run("A2-"+drift+action,()=>{using var h=new Harness();var s=Selection(h,true,true);
            if(drift=="current-change")Write(s.DictPath,"999");if(drift=="backup-change")Write(s.BackupPath,"999");
            if(drift=="current-delete")File.Delete(s.DictPath);if(drift=="backup-delete")File.Delete(s.BackupPath);
            if(drift=="document")Active=h.Drawing("b","999");
            if(drift=="path"){var b=h.Drawing("b","999");Active.Name=b.Name;}
            string before=DictFileVisibility.ReadIdentity(s.DictPath);
            var o=action=="keep"?DictConflict.KeepSelectedVersion(s,DictPaletteWorkflow.ContextGuard(s)):DictConflict.RestoreSelectedBackup(s,DictPaletteWorkflow.ContextGuard(s));
            Check("A2-"+drift+"-"+action,!o.Succeeded&&!o.Wrote&&before==DictFileVisibility.ReadIdentity(s.DictPath),"error="+o.Error);
        });
        foreach(string fault in new[]{"none","baseline","delete-backup","readonly","baseline-repeat","delete-backup-repeat"})
        Run("A3-"+fault,()=>{using var h=new Harness();var s=Selection(h,true,true);h.Select(Active);
            FileStream held=null;if(fault.StartsWith("baseline"))Directory.CreateDirectory(s.DictPath+".word-baseline");
            if(fault.StartsWith("delete-backup"))held=new FileStream(s.BackupPath,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(fault=="readonly")File.SetAttributes(s.DictPath,FileAttributes.ReadOnly);
            bool opened=false,wrote=false,success=false;
            using(var pump=new System.Windows.Forms.Timer()){pump.Interval=40;pump.Tick+=(sender,args)=>{
                var d=Application.OpenForms.OfType<ArbitrateDialog>().FirstOrDefault();if(d==null)return;
                pump.Stop();opened=true;Field<Button>(d,"_btnRestoreCad").PerformClick();wrote=d.Outcome.Wrote;success=d.Outcome.Succeeded;
                if(fault.EndsWith("repeat")&&d.DialogResult==DialogResult.None)Field<Button>(d,"_btnRestoreCad").PerformClick();
                if(d.DialogResult==DialogResult.None)Field<Button>(d,"_btnLater").PerformClick();
            };pump.Start();Call(h.Control,"BtnArbitrate_Click",null,EventArgs.Empty);pump.Stop();}
            held?.Dispose();File.SetAttributes(s.DictPath,FileAttributes.Normal);
            Call(h.Control,"AutoRefreshTimer_Tick",null,EventArgs.Empty);Call(h.Control,"AutoRefreshTimer_Tick",null,EventArgs.Empty);
            string disk=DictLoader.Load(s.DictPath).Entries[0].Number;
            string panel=h.Model==null?"null":h.Model.Entries[0].Number;
            Check("A3-open-"+fault,opened,"opened="+opened);
            Check("A3-result-"+fault,success==(fault=="none")&&wrote==(fault!="readonly"),"success="+success+"|wrote="+wrote);
            Check("A3-panel-matches-disk-"+fault,panel==disk,"panel="+panel+"|disk="+disk);
        });
        Run("A4-other-backup",()=>{using var h=new Harness();var s=Selection(h,true,true);string older=s.DictPath+".word-20261101-120000.bak";Write(older,"111");
            var o=DictConflict.KeepSelectedVersion(s,DictPaletteWorkflow.ContextGuard(s));Check("A4-other-backup-retained",o.Succeeded&&File.Exists(older));
        });
    }
    [STAThread] static int Main()
    {
        Directory.CreateDirectory(Root);RuntimeHost.SetActiveDocumentOverride(()=>Active);
        Console.WriteLine("ENV|"+Environment.UserDomainName+"\\"+Environment.UserName+"|"+System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        PendingMatrix();ArbitrationMatrix();
        Console.WriteLine("SUMMARY|passed="+passed+"|failed="+failed+"|blocked="+blocked);
        return failed==0&&blocked==0?0:1;
    }
}
