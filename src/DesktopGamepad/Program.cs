using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DesktopGamepad
{
    static class Program
    {
        const string InstanceMutexName = "DesktopGamepad_SingleInstance";
        public const string ShowWindowEventName = "DesktopGamepad_ShowWindow";
        public const string QuitEventName = "DesktopGamepad_Quit";

        [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, int size);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [StructLayout(LayoutKind.Sequential)] struct PowerThrottlingState { public uint Version, ControlMask, StateMask; }

        /// <summary>
        /// Windows 11 saves power in apps with no visible window, which is how Desktop Gamepad usually runs: it lowers their
        /// CPU priority (EcoQoS) and ignores their timer precision requests. Both make the cursor and scrolling stutter, so
        /// this app opts out. It only does work while the controller is being used, so this costs nothing at rest.
        /// </summary>
        static void KeepInputSmooth()
        {
            const uint ExecutionSpeed = 0x1, IgnoreTimerResolution = 0x4;
            var state = new PowerThrottlingState { Version = 1, ControlMask = ExecutionSpeed | IgnoreTimerResolution, StateMask = 0 };
            try
            {
                if (!SetProcessInformation(GetCurrentProcess(), 4 /* ProcessPowerThrottling */, ref state, Marshal.SizeOf(typeof(PowerThrottlingState))))
                    Log.Write("could not opt out of power throttling");
            }
            catch (EntryPointNotFoundException) { } // older Windows 10 has no such setting
        }

        [STAThread]
        static int Main(string[] args)
        {
            bool background = Array.IndexOf(args, "--background") >= 0;
            // "--quit" is used by the installer and uninstaller: a running copy closes cleanly, so the cursor is put back.
            bool quit = Array.IndexOf(args, "--quit") >= 0;

            using (var mutex = new Mutex(true, InstanceMutexName, out bool isFirst))
            {
                if (!isFirst)
                {
                    // Already running: ask that copy to quit, or to show its window (not for the startup launch).
                    string signal = quit ? QuitEventName : background ? null : ShowWindowEventName;
                    if (signal != null)
                        try { using (var e = EventWaitHandle.OpenExisting(signal)) e.Set(); } catch { }
                    if (quit)
                        try { mutex.WaitOne(TimeSpan.FromSeconds(10)); } catch (AbandonedMutexException) { } // until it has exited
                    return 0;
                }
                if (quit) return 0; // nothing was running
                KeepInputSmooth();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Write("UI thread error: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("unhandled error: " + e.ExceptionObject);

                Log.Write("Desktop Gamepad starting" + (background ? " in the background" : ""));
                Application.Run(new AppHost(openWindow: !background));
                GC.KeepAlive(mutex);
                return 0;
            }
        }
    }
}
