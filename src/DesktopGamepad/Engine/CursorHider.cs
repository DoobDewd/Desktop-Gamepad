using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DesktopGamepad.Engine
{
    /// <summary>
    /// Hides the cursor in apps set to Off (and the Xbox app) once the controller is used there. The cursor is swapped for
    /// a blank one and moved into a corner with a real input
    /// event, so it also stops highlighting buttons. A real mouse move brings it back where it was; using the controller
    /// again hides it again. The user's own cursors are always restored when Desktop Gamepad starts or quits.
    /// </summary>
    public sealed class CursorHider : IDisposable
    {
        [DllImport("user32.dll")] static extern IntPtr CreateCursor(IntPtr instance, int hotX, int hotY, int width, int height, byte[] andPlane, byte[] xorPlane);
        [DllImport("user32.dll")] static extern bool SetSystemCursor(IntPtr cursor, uint id);
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, IntPtr vparam, uint winIni);
        const uint SPI_SETCURSORS = 0x0057;

        // Arrow, I-beam, wait, cross, up, the four resize arrows, move, no, hand, app starting, help, pin, person.
        static readonly uint[] CursorIds = { 32512, 32513, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651, 32671, 32672 };
        const int GraceMs = 300;
        const int ParkInset = 16;

        readonly object gate = new object();
        volatile bool wanted, hidden;
        int hiddenAt, restoredAt;
        bool parked;
        Native.POINT parkedFrom;

        public CursorHider()
        {
            // A previous run may have been ended while the cursor was hidden.
            SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
        }

        /// <summary>
        /// Called when the front app or the controller changes. Wanting the cursor hidden does not hide it straight away:
        /// that waits until the controller is actually used, so someone using a real mouse in that app never loses it.
        /// </summary>
        public void SetWanted(bool want)
        {
            lock (gate)
            {
                if (want == wanted) return;
                wanted = want;
                if (!want && hidden) RestoreLocked();
            }
        }

        /// <summary>
        /// From the low-level mouse hook, for real (not injected) mouse moves. Returns true to swallow the move: Windows would
        /// otherwise apply it from the parked corner right after the cursor is put back.
        /// </summary>
        public bool OnRealMouseMove()
        {
            if (!hidden || Environment.TickCount - hiddenAt < GraceMs) return false;
            ThreadPool.QueueUserWorkItem(_ => { lock (gate) { if (hidden) RestoreLocked(); } });
            return true;
        }

        /// <summary>From the controller reader, for every report.</summary>
        public void OnControllerUsed()
        {
            if (!wanted || hidden || Environment.TickCount - restoredAt < GraceMs) return;
            ThreadPool.QueueUserWorkItem(_ => { lock (gate) { if (wanted && !hidden) HideLocked(); } });
        }

        void HideLocked()
        {
            hiddenAt = Environment.TickCount;
            hidden = true;

            var andPlane = new byte[32 * 32 / 8];
            for (int i = 0; i < andPlane.Length; i++) andPlane[i] = 0xFF; // fully transparent
            var xorPlane = new byte[32 * 32 / 8];
            foreach (uint id in CursorIds)
            {
                // SetSystemCursor takes ownership of the handle, so each cursor id needs its own blank cursor.
                IntPtr blank = CreateCursor(IntPtr.Zero, 0, 0, 32, 32, andPlane, xorPlane);
                if (blank != IntPtr.Zero) SetSystemCursor(blank, id);
            }

            if (Native.GetCursorPos(out Native.POINT pt))
            {
                IntPtr front = Native.GetForegroundWindow();
                var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
                if (Native.GetMonitorInfo(Native.MonitorFromWindow(front, 2), ref mi))
                {
                    // Near the bottom-right corner of the window in front, and inside the work area. The work area leaves out
                    // the taskbar (any size, any edge) and other docked bars; keeping off its edges stops an auto-hiding
                    // taskbar popping up. Staying inside the front window matters because another tool can still click at
                    // the hidden cursor (a Steam layout with A as left click): that click must not bring another window forward.
                    var area = mi.rcWork;
                    if (Native.GetClientRect(front, out Native.RECT client) && client.Right > 2 * ParkInset && client.Bottom > 2 * ParkInset)
                    {
                        var corner = new Native.POINT { X = client.Right, Y = client.Bottom };
                        if (Native.ClientToScreen(front, ref corner))
                        {
                            area.Right = Math.Min(area.Right, corner.X);
                            area.Bottom = Math.Min(area.Bottom, corner.Y);
                        }
                    }
                    if (!parked) parkedFrom = pt;
                    parked = true;
                    // Middle of the right edge, not the bottom-right corner: video players keep their control bars up while
                    // the pointer rests on them, and those bars run along the bottom.
                    Output.MoveTo(Math.Max(area.Left, area.Right - ParkInset), (area.Top + area.Bottom) / 2);
                }
            }
        }

        void RestoreLocked()
        {
            restoredAt = Environment.TickCount;
            if (parked)
            {
                parked = false;
                Output.MoveTo(parkedFrom.X, parkedFrom.Y);
            }
            hidden = false;
            SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0); // reload the user's own cursor scheme
        }

        public void Dispose()
        {
            lock (gate)
            {
                wanted = false;
                if (hidden) RestoreLocked();
                else SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
            }
        }
    }
}
