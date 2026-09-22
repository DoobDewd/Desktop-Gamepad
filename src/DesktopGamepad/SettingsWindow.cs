using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DesktopGamepad
{
    /// <summary>
    /// The settings window: a normal Windows window with a dark title bar, showing the web UI from the "ui" folder.
    /// It is created when opened and disposed when closed, so the browser engine only uses memory while it is visible.
    /// </summary>
    sealed class SettingsWindow : Form
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        const string HostName = "desktopgamepad.app";
        readonly WebView2 web;
        readonly Action<string> onMessage;
        bool ready;

        public event Action PageReady;

        public SettingsWindow(Action<string> onMessage)
        {
            this.onMessage = onMessage;
            Text = "Desktop Gamepad";
            ClientSize = new Size(1180, 800);
            MinimumSize = new Size(820, 560);
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized; // opens full size; the size above is used when restored down
            BackColor = Color.FromArgb(0x20, 0x20, 0x20);
            Icon = AppIcon.Create(32);

            web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
            Controls.Add(web);
            Load += async (s, e) => await InitializeWebAsync();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1; // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
        }

        async System.Threading.Tasks.Task InitializeWebAsync()
        {
            try
            {
                string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopGamepad", "WebView2");
                var env = await CoreWebView2Environment.CreateAsync(null, dataFolder);
                await web.EnsureCoreWebView2Async(env);

                var core = web.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
#if !DEBUG
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
#endif
                string uiFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui");
                core.SetVirtualHostNameToFolderMapping(HostName, uiFolder, CoreWebView2HostResourceAccessKind.Allow);
                core.WebMessageReceived += (s, e) => onMessage(e.WebMessageAsJson);
                core.NavigationCompleted += (s, e) => { ready = e.IsSuccess; if (ready) PageReady?.Invoke(); };
                core.NewWindowRequested += (s, e) => e.Handled = true; // links never open extra windows
                core.Navigate("https://" + HostName + "/index.html");
            }
            catch (WebView2RuntimeNotFoundException)
            {
                MessageBox.Show(this, "Desktop Gamepad needs the Microsoft Edge WebView2 Runtime, which is part of Windows 11. " +
                    "Install it from Microsoft, then open Desktop Gamepad again.", "Desktop Gamepad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
            }
            catch (Exception ex)
            {
                Log.Write("settings window failed to load: " + ex);
                Close();
            }
        }

        /// <summary>Sends a JSON message to the page. Ignored until the page has loaded.</summary>
        public void Post(string json)
        {
            if (!ready || IsDisposed || web.CoreWebView2 == null) return;
            try { web.CoreWebView2.PostWebMessageAsJson(json); } catch (Exception ex) { Log.Write("post to page failed: " + ex.Message); }
        }

        /// <summary>Gives the page keyboard focus, so the controller highlight shows and text boxes take typing.</summary>
        public void FocusPage()
        {
            if (!IsDisposed && !web.ContainsFocus) web.Focus();
        }

        public void BringToFront(bool restore)
        {
            if (restore && WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Maximized;
            Show();
            Activate();
        }
    }
}
