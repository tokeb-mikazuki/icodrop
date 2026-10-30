using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;

namespace WallBlur
{
    /// <summary>Finds the desktop's SysListView32 and the windows it lives in.</summary>
    class DesktopShell
    {
        public IntPtr Progman;
        public IntPtr DefView;
        public IntPtr ListView;
        /// <summary>Top-level window that owns SHELLDLL_DefView (Progman, or a WorkerW on older builds).</summary>
        public IntPtr DefViewOwner;

        public bool Locate()
        {
            Progman = Native.FindWindow("Progman", null);
            DefView = ListView = DefViewOwner = IntPtr.Zero;

            IntPtr dv = IntPtr.Zero, owner = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                string cls = Native.ClassOf(h);
                if (cls != "Progman" && cls != "WorkerW") return true;
                IntPtr d = Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (d == IntPtr.Zero) return true;
                dv = d;
                owner = h;
                return false;
            }, IntPtr.Zero);

            if (dv == IntPtr.Zero) return false;
            DefView = dv;
            DefViewOwner = owner;
            ListView = Native.FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
            return ListView != IntPtr.Zero;
        }
    }

    /// <summary>
    /// Reads desktop icon rectangles straight out of Explorer's list view
    /// (LVM_GETITEMRECT needs a RECT that lives in Explorer's address space).
    /// </summary>
    class DesktopIconReader : IDisposable
    {
        const uint LVM_GETITEMCOUNT = 0x1004;
        const uint LVM_GETITEMRECT = 0x100E;
        const uint PROCESS_ACCESS = 0x0008 | 0x0010 | 0x0020 | 0x0400;

        IntPtr listView;
        IntPtr proc;
        IntPtr remote;
        readonly byte[] zero = new byte[16];
        readonly byte[] buf = new byte[16];

        public void Bind(IntPtr lv)
        {
            if (lv == listView && proc != IntPtr.Zero) return;
            Release();
            listView = lv;
            if (lv == IntPtr.Zero) return;

            uint pid;
            Native.GetWindowThreadProcessId(lv, out pid);
            proc = Native.OpenProcess(PROCESS_ACCESS, false, pid);
            if (proc == IntPtr.Zero) return;
            remote = Native.VirtualAllocEx(proc, IntPtr.Zero, (UIntPtr)16, 0x3000, 0x04);
            if (remote == IntPtr.Zero) Release();
        }

        /// <summary>Icon cells in screen coordinates. Empty when desktop icons are hidden.</summary>
        public List<Rect> Read()
        {
            List<Rect> result = new List<Rect>();
            if (listView == IntPtr.Zero || proc == IntPtr.Zero || remote == IntPtr.Zero) return result;
            if (!Native.IsWindowVisible(listView)) return result;

            IntPtr res;
            if (Native.SendMessageTimeout(listView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero,
                Native.SMTO_ABORTIFHUNG, 100, out res) == IntPtr.Zero) return result;
            int count = res.ToInt32();
            if (count <= 0 || count > 2000) return result;

            POINT origin = new POINT();
            Native.ClientToScreen(listView, ref origin);

            IntPtr io;
            for (int i = 0; i < count; i++)
            {
                Native.WriteProcessMemory(proc, remote, zero, 16, out io); // left = LVIR_BOUNDS
                if (Native.SendMessageTimeout(listView, LVM_GETITEMRECT, (IntPtr)i, remote,
                    Native.SMTO_ABORTIFHUNG, 100, out res) == IntPtr.Zero || res == IntPtr.Zero) continue;
                if (!Native.ReadProcessMemory(proc, remote, buf, 16, out io)) continue;

                int l = BitConverter.ToInt32(buf, 0);
                int t = BitConverter.ToInt32(buf, 4);
                int r = BitConverter.ToInt32(buf, 8);
                int b = BitConverter.ToInt32(buf, 12);
                if (r <= l || b <= t) continue;
                result.Add(new Rect(origin.X + l, origin.Y + t, r - l, b - t));
            }
            return result;
        }

        void Release()
        {
            if (proc != IntPtr.Zero)
            {
                if (remote != IntPtr.Zero) Native.VirtualFreeEx(proc, remote, UIntPtr.Zero, 0x8000);
                Native.CloseHandle(proc);
            }
            proc = remote = IntPtr.Zero;
        }

        public void Dispose()
        {
            Release();
            listView = IntPtr.Zero;
        }
    }

    /// <summary>Enumerates ordinary, visible, un-maximised top-level windows.</summary>
    static class WindowReader
    {
        static readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;

        static readonly HashSet<string> ignoredClasses = new HashSet<string>
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
            "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "NotifyIconOverflowWindow",
            "TopLevelWindowForOverflowXamlIsland"
        };

        public static List<KeyValuePair<long, Rect>> Read(Rect[] monitors)
        {
            List<KeyValuePair<long, Rect>> list = new List<KeyValuePair<long, Rect>>();
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;

                int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                if ((ex & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE)) != 0) return true;
                if (Native.GetWindowTextLength(h) == 0) return true;

                int cloaked;
                if (Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;

                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == ownPid) return true;
                if (ignoredClasses.Contains(Native.ClassOf(h))) return true;

                RECT r;
                if (Native.DwmGetWindowAttribute(h, 9, out r, 16) != 0 && !Native.GetWindowRect(h, out r)) return true;
                if (r.Width < 48 || r.Height < 48) return true;

                Rect rect = new Rect(r.Left, r.Top, r.Width, r.Height);
                // A window that fills a whole monitor hides the wallpaper anyway.
                foreach (Rect m in monitors)
                {
                    if (rect.Left <= m.Left + 8 && rect.Top <= m.Top + 8 &&
                        rect.Right >= m.Right - 8 && rect.Bottom >= m.Bottom - 8) return true;
                }
                list.Add(new KeyValuePair<long, Rect>(h.ToInt64(), rect));
                return true;
            }, IntPtr.Zero);
            return list;
        }
    }
}
