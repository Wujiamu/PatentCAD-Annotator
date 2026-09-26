using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PatentOffice.PowerPoint
{
    [ComVisible(true)]
    [Guid("4E9B0E7A-4D92-47AF-A9D7-7E769CB367D8")]
    [ProgId("PatentOffice.PowerPointAddIn")]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class PowerPointAddIn : IDTExtensibility2
    {
        private object _application;
        private PaletteForm _palette;

        public void OnConnection(object application, int connectMode, object addInInstance, ref Array custom)
        {
            try
            {
                _application = application;
                string version = "unknown";
                try { version = Convert.ToString(((dynamic)application).Version); }
                catch { }
                OfficeDiagnostics.Start(version);
                OfficeDiagnostics.Write("lifecycle", "PASS",
                    "OnConnection;mode=" + connectMode + ";assembly=" + OfficeDiagnostics.AssemblyPath());
                ShowPalette();
            }
            catch (Exception ex)
            {
                OfficeDiagnostics.Write("lifecycle", "FAIL", "OnConnection;" + ex.GetType().Name + ":" + ex.Message);
            }
        }

        public void OnDisconnection(int removeMode, ref Array custom)
        {
            ClosePalette("OnDisconnection;mode=" + removeMode);
            _application = null;
            OfficeDiagnostics.Write("lifecycle", "PASS", "disconnected");
        }

        public void OnAddInsUpdate(ref Array custom)
        {
            OfficeDiagnostics.Write("lifecycle", "PASS", "OnAddInsUpdate");
        }

        public void OnStartupComplete(ref Array custom)
        {
            OfficeDiagnostics.Write("lifecycle", "PASS", "OnStartupComplete");
        }

        public void OnBeginShutdown(ref Array custom)
        {
            ClosePalette("OnBeginShutdown");
            OfficeDiagnostics.Write("lifecycle", "PASS", "OnBeginShutdown");
        }

        internal dynamic ApplicationObject
        {
            get { return _application; }
        }

        internal void ShowPalette()
        {
            if (_palette != null && !_palette.IsDisposed)
            {
                if (!_palette.Visible) _palette.Show();
                _palette.Activate();
                return;
            }

            _palette = new PaletteForm(this);
            _palette.FormClosed += delegate { _palette = null; };
            _palette.Show();
        }

        private void ClosePalette(string reason)
        {
            try
            {
                if (_palette != null && !_palette.IsDisposed)
                    _palette.CloseFromHost();
            }
            catch (Exception ex)
            {
                OfficeDiagnostics.Write("lifecycle", "FAIL", reason + ";" + ex.GetType().Name + ":" + ex.Message);
            }
            finally { _palette = null; }
        }
    }
}
