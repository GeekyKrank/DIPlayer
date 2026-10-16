using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace DIPlayer.SystemIntegration;

// Shell notification icon and native popup menu: avoids loading WinForms solely for the tray.
public sealed class NativeTray : IDisposable
{
    const int Callback = 0x802B;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct IconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, CallbackMessage; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Point
    {
        public int X, Y;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint message, ref IconData data);
    [DllImport("user32.dll")] static extern IntPtr LoadIcon(IntPtr instance, IntPtr icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, int message, IntPtr w, IntPtr l);
    readonly HwndSource source;
    readonly Action open, toggle, next, exit;
    readonly uint explorerCreated;
    IconData data;
    IntPtr ownedIcon;
    bool disposed;
    public bool Registered
    {
        get; private set;
    }
    public bool UsesAppIcon => ownedIcon != IntPtr.Zero;
    public NativeTray(HwndSource source, Action open, Action toggle, Action next, Action exit)
    {
        this.source = source;
        this.open = open;
        this.toggle = toggle;
        this.next = next;
        this.exit = exit;
        ExtractIconEx(Path.Combine(AppContext.BaseDirectory, "DIPlayer.exe"), 0, out var large, out var small, 1);
        ownedIcon = small != IntPtr.Zero ? small : large;
        if (large != IntPtr.Zero && large != ownedIcon) DestroyIcon(large);
        data = new IconData { Size = (uint)Marshal.SizeOf<IconData>(), Window = source.Handle, Id = 1, Flags = 7, CallbackMessage = Callback, Icon = ownedIcon != IntPtr.Zero ? ownedIcon : LoadIcon(IntPtr.Zero, (IntPtr)32512), Tip = "DIPlayer", Info = "", InfoTitle = "" };
        explorerCreated = RegisterWindowMessage("TaskbarCreated");
        source.AddHook(Hook);
        Registered = Shell_NotifyIcon(0, ref data);
    }
    public void SetTitle(string text)
    {
        data.Tip = text[..Math.Min(text.Length, 127)];
        if (data.Tip.Length > 0 && char.IsHighSurrogate(data.Tip[^1]))
            data.Tip = data.Tip[..^1];
        if (!disposed)
            Shell_NotifyIcon(1, ref data);
    }
    IntPtr Hook(IntPtr window, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (message == explorerCreated)
        {
            Registered = Shell_NotifyIcon(0, ref data);
        }
        if (message == Callback)
        {
            handled = true;
            if (l.ToInt32() == 0x203)
                open();
            else if (l.ToInt32() == 0x205)
                Menu();
        }
        return IntPtr.Zero;
    }
    void Menu()
    {
        IntPtr menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, 0, (UIntPtr)1, "Open DIPlayer");
            AppendMenu(menu, 0, (UIntPtr)2, "Play / pause");
            AppendMenu(menu, 0, (UIntPtr)3, "Next");
            AppendMenu(menu, 0, (UIntPtr)4, "Quit");
            GetCursorPos(out var point);
            SetForegroundWindow(source.Handle);
            uint selected = TrackPopupMenuEx(menu, 0x102, point.X, point.Y, source.Handle, IntPtr.Zero);
            PostMessage(source.Handle, 0, IntPtr.Zero, IntPtr.Zero);
            if (selected == 1)
                open();
            else if (selected == 2)
                toggle();
            else if (selected == 3)
                next();
            else if (selected == 4)
                exit();
        }
        finally { DestroyMenu(menu); }
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        Shell_NotifyIcon(2, ref data);
        if (ownedIcon != IntPtr.Zero) { DestroyIcon(ownedIcon); ownedIcon = IntPtr.Zero; }
        source.RemoveHook(Hook);
    }
}
