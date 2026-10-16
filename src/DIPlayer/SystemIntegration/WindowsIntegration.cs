using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
namespace DIPlayer.SystemIntegration;

public sealed class WindowsIntegration : IDisposable
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    readonly HwndSource source;
    readonly NativeTray tray;
    readonly Action<int> command;
    readonly Action showWindow;
    readonly PowerModeChangedEventHandler power;
    readonly List<int> registered = [];
    readonly MMDeviceEnumerator? devices;
    readonly DeviceNotifications? notifications;
    public WindowsIntegration(System.Windows.Window window, Action<int> command, Action exit, Action suspend, Action resume, Action deviceChanged)
    {
        this.command = command;
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!;
        source.AddHook(Hook);
        uint[] keys = [0xB3, 0xB0, 0xB1, 0xB2];
        for (int i = 0; i < keys.Length; i++)
        if (RegisterHotKey(source.Handle, 700 + i, 0, keys[i]))
            registered.Add(700 + i);
        void Show()
        {
            window.Dispatcher.Invoke(() => WindowActivation.Restore(window));
        }
        showWindow = Show;
        tray = new NativeTray(source, Show, () => command(0), () => command(1), exit);
        power = (_, e) => window.Dispatcher.BeginInvoke(() => { if (e.Mode == PowerModes.Suspend) suspend(); else if (e.Mode == PowerModes.Resume) resume(); });
        SystemEvents.PowerModeChanged += power;
        try
        {
            devices = new MMDeviceEnumerator();
            notifications = new DeviceNotifications(() => window.Dispatcher.BeginInvoke(deviceChanged));
            devices.RegisterEndpointNotificationCallback(notifications);
        }
        catch (COMException) { devices?.Dispose(); devices = null; notifications = null; }
    }
    IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x802A)
        {
            showWindow();
            handled = true;
        }
        else if (msg == 0x312)
        {
            int id = w.ToInt32() - 700;
            if (id >= 0 && id < 4)
            {
                command(id);
                handled = true;
            }
        }
        else if (msg == 0x319)
        {
            int code = (int)((l.ToInt64() >> 16) & 0x7ff);
            int action = code switch
            {
                14 => 0,
                11 => 1,
                12 => 2,
                13 => 3,
                _ => -1
            };
            if (action >= 0)
            {
                command(action);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }
    public bool AllKeysRegistered => registered.Count == 4;
    public bool TrayAvailable => tray.Registered;
    public bool TrayUsesAppIcon => tray.UsesAppIcon;
    public void Track(string title) => tray.SetTitle("DIPlayer · " + title);
    public void Dispose()
    {
        if (notifications != null)
            devices?.UnregisterEndpointNotificationCallback(notifications);
        devices?.Dispose();
        SystemEvents.PowerModeChanged -= power;
        foreach (int id in registered)
            UnregisterHotKey(source.Handle, id);
        source.RemoveHook(Hook);
        tray.Dispose();
    }
    sealed class DeviceNotifications(Action changed) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
                changed();
        }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
        }
        public void OnDeviceAdded(string deviceId)
        {
        }
        public void OnDeviceRemoved(string deviceId)
        {
        }
        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
        }
    }
}
