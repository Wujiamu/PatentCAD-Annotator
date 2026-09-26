using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PatentOffice.Visio
{
    [ComVisible(true)]
    [Guid("D1D78625-AF57-462D-A1AB-5C47587AB30C")]
    [ProgId("PatentOffice.VisioAddIn")]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class VisioAddIn : IDTExtensibility2
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
                VisioDiagnostics.Start(version);
                VisioDiagnostics.Write("lifecycle", "PASS", "OnConnection;mode=" + connectMode);
                ShowPalette();
            }
            catch (Exception ex)
            {
                VisioDiagnostics.Write("lifecycle", "FAIL", "OnConnection;" + ex.GetType().Name + ":" + ex.Message);
            }
        }

        public void OnDisconnection(int removeMode, ref Array custom)
        {
            ClosePalette("OnDisconnection;mode=" + removeMode);
            _application = null;
            VisioDiagnostics.Write("lifecycle", "PASS", "disconnected");
        }

        public void OnAddInsUpdate(ref Array custom)
        {
            VisioDiagnostics.Write("lifecycle", "PASS", "OnAddInsUpdate");
        }

        public void OnStartupComplete(ref Array custom)
        {
            VisioDiagnostics.Write("lifecycle", "PASS", "OnStartupComplete");
        }

        public void OnBeginShutdown(ref Array custom)
        {
            ClosePalette("OnBeginShutdown");
        }

        internal dynamic ApplicationObject { get { return _application; } }

        private void ShowPalette()
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
                if (_palette != null && !_palette.IsDisposed) _palette.CloseFromHost();
            }
            catch (Exception ex)
            {
                VisioDiagnostics.Write("lifecycle", "FAIL", reason + ";" + ex.GetType().Name + ":" + ex.Message);
            }
            finally { _palette = null; }
        }
    }
}
