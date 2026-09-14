using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Danslicer.App.Input;

/// <summary>
/// One process-wide Windows Raw Input receiver, owned by SixAxisSession. Create, poll and dispose
/// on the UI thread. Its message-only window survives viewport handoffs and never takes focus.
/// Motion polling reads managed state; no driver calls or COM objects are involved.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class RawInputSpaceMouse(Action<string>? log = null, Action? inputAvailable = null) : ISixAxisInput
{
    private const uint WmInput = 0x00FF, WmInputDeviceChange = 0x00FE;
    private const uint RidInput = 0x10000003, RidiDeviceInfo = 0x2000000b;
    private const uint RidevRemove = 1, RidevInputSink = 0x100, RidevDevNotify = 0x2000;
    private readonly RawSpaceMouseState _state = new(inputAvailable: inputAvailable);
    private readonly Dictionary<nint, uint?> _products = [];
    private readonly string _className = "Danslicer.SpaceMouse." + Guid.NewGuid().ToString("N");
    private WindowProc? _windowProc; // Root the native callback until the window and class are gone.
    private nint _window, _module;
    private ushort _windowClass;
    private bool _registered;
    private string? _lastError;
    private uint? _lastForegroundProcess;

    /// <summary>Whether the receiver is registered, independent of a device being plugged in.</summary>
    public bool IsConnected => _registered;
    public int DeviceCount => _products.Values.Count(p => p.HasValue);
    public long MotionReports => _state.MotionReports;
    public long RejectedReports => _state.RejectedReports;

    public bool TryConnect()
    {
        if (IsConnected) return true;
        Dispose();
        try
        {
            _module = GetModuleHandleW(null);
            _windowProc = WndProc;
            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(), Instance = _module,
                WindowProc = Marshal.GetFunctionPointerForDelegate(_windowProc), ClassName = _className,
            };
            _windowClass = RegisterClassExW(ref windowClass);
            if (_windowClass == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            _window = CreateWindowExW(0, _className, "", 0, 0, 0, 0, 0, new nint(-3), 0, _module, 0);
            if (_window == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            var registration = new RawInputDevice
            {
                UsagePage = 1, Usage = 8, Flags = RidevInputSink | RidevDevNotify, Target = _window,
            };
            if (!RegisterRawInputDevices(&registration, 1, (uint)sizeof(RawInputDevice)))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            _registered = true;
            EnumerateDevices();
            SyncForeground();
            log?.Invoke($"SpaceMouse: Windows Raw Input ready; {DeviceCount} device(s)");
            return true;
        }
        catch (Exception exception)
        {
            ReportError("initialization", exception);
            Dispose();
            return false;
        }
    }

    public SixAxisMotion Poll()
    {
        if (!IsConnected) return default;
        SyncForeground();
        return _state.Poll();
    }

    public IReadOnlyList<SixAxisButtonPress> DrainButtonPresses()
    {
        SyncForeground();
        return _state.DrainButtonPresses();
    }

    private void SyncForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
        if (SpaceMouseDiagnostics.Enabled && _lastForegroundProcess != processId)
            SpaceMouseDiagnostics.Write("FOCUS", $"pid={processId} active={processId == (uint)Environment.ProcessId}");
        _lastForegroundProcess = processId;
        _state.SetForeground(processId == (uint)Environment.ProcessId);
    }

    private nint WndProc(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == WmInput) ReadInput(lParam);
            else if (message == WmInputDeviceChange)
            {
                _products.Remove(lParam); // Invalidate metadata even if Windows reuses a handle.
                _state.RemoveDevice(lParam);
                if (wParam == 1) GetProduct(lParam); // GIDC_ARRIVAL; 2 means removal.
                log?.Invoke($"SpaceMouse: device {(wParam == 1 ? "arrival" : "removal")}; {DeviceCount} device(s)");
            }
        }
        catch (Exception exception)
        {
            // Never unwind through a native callback. A bad report must not kill the receiver.
            _state.Reset();
            ReportError("input", exception);
        }
        // Required for foreground WM_INPUT cleanup; harmless for INPUTSINK messages too.
        return DefWindowProcW(window, message, wParam, lParam);
    }

    private void ReadInput(nint input)
    {
        uint size = 0;
        if (GetRawInputData(input, RidInput, null, ref size, (uint)sizeof(RawInputHeader)) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (size < sizeof(RawInputHeader) + 8 || size > 65536) return;
        byte[]? rented = null;
        Span<byte> bytes = size <= 1024 ? stackalloc byte[1024] : (rented = ArrayPool<byte>.Shared.Rent((int)size));
        try
        {
            fixed (byte* data = bytes)
            {
                var copied = GetRawInputData(input, RidInput, data, ref size, (uint)sizeof(RawInputHeader));
                if (copied == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (copied < sizeof(RawInputHeader) + 8 || copied > bytes.Length) return;
                var header = *(RawInputHeader*)data;
                if (header.Type != 2 || header.Size > copied || header.Size < sizeof(RawInputHeader) + 8) return;
                var product = GetProduct(header.Device);
                if (product is null) return;
                SyncForeground();
                var hid = data + sizeof(RawInputHeader);
                if (SpaceMouseDiagnostics.Enabled)
                    SpaceMouseDiagnostics.Write("HID", $"device={header.Device:X} product={product:X} size={*(uint*)hid} count={*(uint*)(hid + 4)} queueAgeMs={unchecked((uint)Environment.TickCount - (uint)GetMessageTime())} bytes={Convert.ToHexString(bytes.Slice(sizeof(RawInputHeader) + 8, (int)header.Size - sizeof(RawInputHeader) - 8))}");
                _state.ReceiveBatch(header.Device, product.Value,
                    bytes.Slice(sizeof(RawInputHeader) + 8, (int)header.Size - sizeof(RawInputHeader) - 8),
                    *(uint*)hid, *(uint*)(hid + 4));
            }
        }
        finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented); }
    }

    private uint? GetProduct(nint handle)
    {
        if (_products.TryGetValue(handle, out var cached)) return cached;
        var info = new DeviceInfo { Size = (uint)sizeof(DeviceInfo) };
        var size = info.Size;
        // Do not cache failures: hotplug may briefly expose a handle before metadata is ready.
        if (GetRawInputDeviceInfoW(handle, RidiDeviceInfo, &info, ref size) == uint.MaxValue) return null;
        uint? product = IsSpaceMouse(info.Type, info.Vendor, info.Product, info.UsagePage, info.Usage) ? info.Product : null;
        _products[handle] = product;
        if (product.HasValue) log?.Invoke($"SpaceMouse: HID {info.Vendor:X4}:{info.Product:X4}");
        return product;
    }

    internal static bool IsSpaceMouse(uint type, uint vendor, uint product, ushort page, ushort usage) =>
        type == 2 && page == 1 && usage == 8 &&
        (vendor == 0x256F || (vendor == 0x046D && product is 0xC621 or 0xC623 or 0xC625 or 0xC626 or 0xC627 or 0xC628 or 0xC629 or 0xC62B));

    private void EnumerateDevices()
    {
        uint count = 0;
        if (GetRawInputDeviceList(null, ref count, (uint)sizeof(DeviceListEntry)) == uint.MaxValue || count > 4096) return;
        var entries = new DeviceListEntry[count];
        fixed (DeviceListEntry* list = entries)
        {
            var read = GetRawInputDeviceList(list, ref count, (uint)sizeof(DeviceListEntry));
            if (read == uint.MaxValue) return; // Arrival messages or the first report will fill the cache.
            for (var i = 0; i < read; i++) if (entries[i].Type == 2) GetProduct(entries[i].Device);
        }
    }

    private void ReportError(string operation, Exception exception)
    {
        var message = $"SpaceMouse Raw Input {operation}: {exception.Message} (0x{exception.HResult:X8})";
        if (_lastError == message) return;
        _lastError = message;
        System.Diagnostics.Trace.WriteLine(message);
        Danslicer.Core.Diagnostics.CrashLog.Write(message);
        log?.Invoke(message);
    }

    public void Dispose()
    {
        if (_registered)
        {
            var registration = new RawInputDevice { UsagePage = 1, Usage = 8, Flags = RidevRemove };
            RegisterRawInputDevices(&registration, 1, (uint)sizeof(RawInputDevice));
            _registered = false;
        }
        if (_window != 0) { DestroyWindow(_window); _window = 0; }
        if (_windowClass != 0) { UnregisterClassW(_className, _module); _windowClass = 0; }
        _windowProc = null;
        _products.Clear();
        _state.Reset();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProc(nint window, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public nint WindowProc;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? MenuName, ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader { public uint Type, Size; public nint Device; public nuint WParam; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceListEntry { public nint Device; public uint Type; }
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct DeviceInfo
    {
        [FieldOffset(0)] public uint Size;
        [FieldOffset(4)] public uint Type;
        [FieldOffset(8)] public uint Vendor;
        [FieldOffset(12)] public uint Product;
        [FieldOffset(20)] public ushort UsagePage;
        [FieldOffset(22)] public ushort Usage;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassExW(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterClassW(string className, nint instance);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterRawInputDevices(RawInputDevice* devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(nint input, uint command, void* data, ref uint size, uint headerSize);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputDeviceInfoW(nint device, uint command, DeviceInfo* info, ref uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputDeviceList(DeviceListEntry* devices, ref uint count, uint size);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetMessageTime();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
