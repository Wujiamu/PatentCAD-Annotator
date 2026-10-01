using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

// Standalone, opt-in test tool. Does not load or modify the Computer Use runtime.
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly HashSet<string> Targets = new(StringComparer.OrdinalIgnoreCase) { "acad", "notepad" };

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) throw new ArgumentException("Use self-test, windows <pid>, activate <pid> <hwnd>, capture <pid> <hwnd> <new-directory>, click <observation> <x> <y> <1|2>, key <observation> <key>, text <observation> <text>, drag <observation> <x1> <y1> <x2> <y2>.");
            if (args[0] == "self-test") { SelfTest(); return 0; }
            if (!Native.SetProcessDpiAwarenessContext(new IntPtr(-4)))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot enable physical-pixel DPI coordinates.");
            switch (args[0])
            {
                case "windows":
                    var process = Target(int.Parse(args[1]));
                    var windows = new List<object>();
                    Native.EnumWindows((hwnd, _) =>
                    {
                        Native.GetWindowThreadProcessId(hwnd, out uint pid);
                        if (pid == process.Id && Native.IsWindowVisible(hwnd))
                        {
                            var title = new System.Text.StringBuilder(1024);
                            Native.GetWindowText(hwnd, title, title.Capacity);
                            windows.Add(new { pid, hwnd = hwnd.ToInt64(), title = title.ToString(), minimized = Native.IsIconic(hwnd) });
                        }
                        return true;
                    }, IntPtr.Zero);
                    Console.WriteLine(JsonSerializer.Serialize(windows, Json));
                    break;
                case "activate":
                    int activatePid = int.Parse(args[1]);
                    IntPtr activateHwnd = new(long.Parse(args[2]));
                    ValidateWindow(activatePid, activateHwnd);
                    if (Native.IsIconic(activateHwnd)) Native.ShowWindow(activateHwnd, 9);
                    if (!Native.SetForegroundWindow(activateHwnd)) throw new InvalidOperationException("Windows refused foreground activation; no input sent.");
                    Console.WriteLine("Activated. Capture a new observation before input.");
                    break;
                case "capture":
                    Console.WriteLine(Capture(int.Parse(args[1]), new IntPtr(long.Parse(args[2])), args[3]));
                    break;
                case "click":
                    var click = Observation.Read(args[1]);
                    int x = int.Parse(args[2]), y = int.Parse(args[3]), count = int.Parse(args[4]);
                    if (count != 1 && count != 2) throw new ArgumentException("Click count must be 1 or 2.");
                    Guard(click, x, y);
                    Consume(args[1]);
                    Move(click.Left + x, click.Top + y);
                    GuardForeground(click);
                    GuardPoint(click, x, y);
                    var clicks = Enumerable.Range(0, count).SelectMany(_ => new[] { Input.Mouse(0x0002), Input.Mouse(0x0004) }).ToArray();
                    Send(clicks);
                    Console.WriteLine("Input sent. Business result is unverified; capture a new observation.");
                    break;
                case "key":
                    var key = Observation.Read(args[1]);
                    Guard(key);
                    var inputs = KeyInputs(args[2]);
                    Consume(args[1]);
                    Send(inputs);
                    Console.WriteLine("Input sent. Capture a new observation.");
                    break;
                case "text":
                case "command":
                    var text = Observation.Read(args[1]);
                    Guard(text);
                    if (args[0] == "command" && !Target(text.Pid).ProcessName.Equals("acad", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Command submission is limited to AutoCAD.");
                    if (args[2].Any(char.IsControl)) throw new ArgumentException("Text must not include control characters. Send keys separately.");
                    if (args[2].Length > 2048) throw new ArgumentException("Text exceeds 2048 characters.");
                    Consume(args[1]);
                    foreach (char character in args[2])
                    {
                        GuardForeground(text);
                        Send(new[] { Input.Unicode(character, false), Input.Unicode(character, true) });
                    }
                    if (args[0] == "command") { GuardForeground(text); Send(KeyInputs("Enter")); }
                    Console.WriteLine("Input sent. Capture a new observation.");
                    break;
                case "drag":
                    var drag = Observation.Read(args[1]);
                    int x1 = int.Parse(args[2]), y1 = int.Parse(args[3]), x2 = int.Parse(args[4]), y2 = int.Parse(args[5]);
                    Guard(drag, x1, y1);
                    GuardPoint(drag, x2, y2);
                    Consume(args[1]);
                    Move(drag.Left + x1, drag.Top + y1);
                    GuardForeground(drag);
                    GuardPoint(drag, x1, y1);
                    bool down = false;
                    try
                    {
                        Send(new[] { Input.Mouse(0x0002) }); down = true;
                        for (int step = 1; step <= 20; step++)
                        {
                            if ((Native.GetAsyncKeyState(0x1b) & 0x8000) != 0) throw new InvalidOperationException("Escape stopped drag.");
                            int dx = x1 + (x2 - x1) * step / 20, dy = y1 + (y2 - y1) * step / 20;
                            GuardForeground(drag);
                            GuardPoint(drag, dx, dy);
                            Move(drag.Left + dx, drag.Top + dy);
                            Thread.Sleep(20);
                        }
                    }
                    finally { if (down) Send(new[] { Input.Mouse(0x0004) }); }
                    Console.WriteLine("Drag sent. Capture a new observation and verify the drawing.");
                    break;
                default: throw new ArgumentException("Unknown operation.");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
    }

    private static Process Target(int pid)
    {
        Process process = Process.GetProcessById(pid);
        if (!Targets.Contains(process.ProcessName)) throw new InvalidOperationException("Only acad.exe and notepad.exe are allowed.");
        if (process.SessionId != Process.GetCurrentProcess().SessionId) throw new InvalidOperationException("Target is outside the current desktop session.");
        return process;
    }

    private static void ValidateWindow(int pid, IntPtr hwnd)
    {
        Target(pid);
        Native.GetWindowThreadProcessId(hwnd, out uint actualPid);
        if (!Native.IsWindow(hwnd) || actualPid != pid || !Native.IsWindowVisible(hwnd))
            throw new InvalidOperationException("Window is absent, hidden, or belongs to another process.");
    }

    private static void GuardForeground(Observation observation)
    {
        IntPtr hwnd = new(observation.Hwnd);
        ValidateWindow(observation.Pid, hwnd);
        if (Native.IsIconic(hwnd)) throw new InvalidOperationException("Target is minimized.");
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint foregroundPid);
        if (foregroundPid != observation.Pid || Native.GetAncestor(Native.GetForegroundWindow(), 3) != Native.GetAncestor(hwnd, 3))
            throw new InvalidOperationException("Target window group is not foreground; no input sent.");
    }

    private static void Guard(Observation observation, int? x = null, int? y = null)
    {
        if (DateTime.UtcNow - observation.Utc > TimeSpan.FromMinutes(2) || observation.Utc > DateTime.UtcNow)
            throw new InvalidOperationException("Observation is stale. Capture again.");
        Process process = Target(observation.Pid);
        if (process.StartTime.ToUniversalTime().Ticks != observation.ProcessStartUtcTicks)
            throw new InvalidOperationException("Process identity changed.");
        GuardForeground(observation);
        Rect rect = VisibleRect(new IntPtr(observation.Hwnd));
        if (rect.Left != observation.Left || rect.Top != observation.Top || rect.Width != observation.Width || rect.Height != observation.Height ||
            Native.GetDpiForWindow(new IntPtr(observation.Hwnd)) != observation.Dpi)
            throw new InvalidOperationException("Window geometry or DPI changed. Capture again.");
        if (Hash(observation.Image) != observation.Sha256) throw new InvalidOperationException("Observation image changed.");
        if (x.HasValue && y.HasValue)
        {
            GuardPoint(observation, x.Value, y.Value);
            using var previous = new Bitmap(observation.Image);
            using var current = Pixels(rect);
            int differences = 0, samples = 0;
            for (int py = Math.Max(0, y.Value - 10); py <= Math.Min(rect.Height - 1, y.Value + 10); py++)
                for (int px = Math.Max(0, x.Value - 10); px <= Math.Min(rect.Width - 1, x.Value + 10); px++)
                {
                    Color a = previous.GetPixel(px, py), b = current.GetPixel(px, py);
                    if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 48) differences++;
                    samples++;
                }
            if (differences > samples / 20) throw new InvalidOperationException("Pixels at the input point changed. Capture again.");
        }
    }

    private static void GuardPoint(Observation observation, int x, int y)
    {
        if (x < 0 || y < 0 || x >= observation.Width || y >= observation.Height) throw new ArgumentException("Point is outside the observed image.");
        IntPtr hit = Native.WindowFromPoint(new Point(observation.Left + x, observation.Top + y));
        Native.GetWindowThreadProcessId(hit, out uint hitPid);
        if (hitPid != observation.Pid || Native.GetAncestor(hit, 3) != Native.GetAncestor(new IntPtr(observation.Hwnd), 3))
            throw new InvalidOperationException("Point is covered by another window; no button press sent.");
    }

    private static void Consume(string path)
    {
        using var consumed = new FileStream(path + ".used", FileMode.CreateNew, FileAccess.Write, FileShare.None);
        consumed.WriteByte(1);
    }

    private static Rect VisibleRect(IntPtr hwnd)
    {
        if (!Native.GetWindowRect(hwnd, out Rect rect)) throw new Win32Exception(Marshal.GetLastWin32Error());
        int left = Native.GetSystemMetrics(76), top = Native.GetSystemMetrics(77);
        rect.Left = Math.Max(rect.Left, left); rect.Top = Math.Max(rect.Top, top);
        rect.Right = Math.Min(rect.Right, left + Native.GetSystemMetrics(78));
        rect.Bottom = Math.Min(rect.Bottom, top + Native.GetSystemMetrics(79));
        if (rect.Width <= 0 || rect.Height <= 0) throw new InvalidOperationException("No visible target pixels.");
        return rect;
    }

    private static Bitmap Pixels(Rect rect)
    {
        var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            IntPtr destination = graphics.GetHdc(), source = Native.GetDC(IntPtr.Zero);
            try
            {
                if (source == IntPtr.Zero || !Native.BitBlt(destination, 0, 0, rect.Width, rect.Height, source, rect.Left, rect.Top, 0x40CC0020))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Desktop BitBlt failed.");
            }
            finally { if (source != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, source); graphics.ReleaseHdc(destination); }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    private static string Capture(int pid, IntPtr hwnd, string directory)
    {
        Process process = Target(pid);
        var observation = new Observation { Pid = pid, Hwnd = hwnd.ToInt64(), ProcessStartUtcTicks = process.StartTime.ToUniversalTime().Ticks };
        GuardForeground(observation);
        Rect rect = VisibleRect(hwnd);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("Capture directory must be new.");
        Directory.CreateDirectory(directory);
        observation.Image = Path.GetFullPath(Path.Combine(directory, "window.png"));
        using (Bitmap bitmap = Pixels(rect)) bitmap.Save(observation.Image, ImageFormat.Png);
        // Reject a target switch/move during capture.
        GuardForeground(observation);
        Rect after = VisibleRect(hwnd);
        if (!rect.Equals(after)) throw new InvalidOperationException("Window moved during capture; discard this image.");
        observation.Left = rect.Left; observation.Top = rect.Top; observation.Width = rect.Width; observation.Height = rect.Height;
        observation.Dpi = Native.GetDpiForWindow(hwnd); observation.Utc = DateTime.UtcNow; observation.Sha256 = Hash(observation.Image);
        string path = Path.GetFullPath(Path.Combine(directory, "observation.json"));
        File.WriteAllText(path, JsonSerializer.Serialize(observation, Json));
        return path;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static int Normalize(int coordinate, int origin, int length)
    {
        if (length <= 1 || coordinate < origin || coordinate >= origin + length) throw new ArgumentOutOfRangeException(nameof(coordinate));
        return (int)Math.Round((coordinate - origin) * 65535.0 / (length - 1));
    }

    private static void Move(int x, int y)
    {
        int dx = Normalize(x, Native.GetSystemMetrics(76), Native.GetSystemMetrics(78));
        int dy = Normalize(y, Native.GetSystemMetrics(77), Native.GetSystemMetrics(79));
        Send(new[] { Input.Mouse(0xC001, dx, dy) });
        if (!Native.GetCursorPos(out Point point) || Math.Abs(point.X - x) > 1 || Math.Abs(point.Y - y) > 1)
            throw new InvalidOperationException("Physical cursor did not reach the observed point; no button press sent.");
    }

    private static Input[] KeyInputs(string name)
    {
        var keys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["Escape"] = 0x1b, ["Enter"] = 0x0d, ["Tab"] = 0x09, ["Space"] = 0x20,
            ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
            ["Home"] = 0x24, ["End"] = 0x23, ["F2"] = 0x71, ["F8"] = 0x77, ["Delete"] = 0x2e,
            ["Backspace"] = 0x08
        };
        if (!keys.TryGetValue(name, out ushort vk)) throw new ArgumentException("Unsupported key. Windows/security shortcuts are not provided.");
        return new[] { Input.Key(vk, false), Input.Key(vk, true) };
    }

    private static void Send(Input[] inputs)
    {
        uint sent = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent == inputs.Length) return;
        int error = Marshal.GetLastWin32Error();
        // A partial batch must not leave a button/key held. Release only inputs that
        // were actually inserted; the original operation still fails.
        var release = new List<Input>();
        foreach (Input input in inputs.Take((int)sent))
        {
            if (input.Type == 0 && (input.Data.Mouse.Flags & 2) != 0) release.Add(Input.Mouse(4));
            if (input.Type == 1 && (input.Data.Keyboard.Flags & 2) == 0)
            {
                Input keyUp = input; keyUp.Data.Keyboard.Flags |= 2; release.Add(keyUp);
            }
        }
        if (release.Count > 0) Native.SendInput((uint)release.Count, release.ToArray(), Marshal.SizeOf<Input>());
        throw new Win32Exception(error, "SendInput incomplete. Outcome is unknown; observe before any retry.");
    }

    private static void SelfTest()
    {
        void Assert(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS: " + name); }
        Assert(Marshal.SizeOf<Input>() == 40, "x64 INPUT ABI");
        Assert(Normalize(-1920, -1920, 3840) == 0 && Normalize(1919, -1920, 3840) == 65535, "negative virtual-desktop origin and endpoints");
        for (int x = -1920; x < 1920; x++)
            if (Math.Abs(Math.Round(Normalize(x, -1920, 3840) * 3839.0 / 65535) - (x + 1920)) > 1) throw new InvalidOperationException("Pixel mapping drift.");
        Assert(true, "all 3840 horizontal pixels map within one pixel");
        bool rejected = false; try { Normalize(1920, -1920, 3840); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Assert(rejected, "out-of-bounds coordinate rejected");
        Assert(Input.Mouse(2).Type == 0 && Input.Key(0x1b, false).Type == 1 && Input.Unicode('中', true).Data.Keyboard.Flags == 6, "mouse/key/unicode input representation");
        Assert(!Targets.Contains("powershell") && !Targets.Contains("Codex") && Targets.SetEquals(new[] { "acad", "notepad" }), "target allowlist");
        rejected = false; try { KeyInputs("Windows"); } catch (ArgumentException) { rejected = true; }
        Assert(rejected, "unsupported shortcut rejected");
        Console.WriteLine("L1_ONLY: No window, screenshot, keyboard, or mouse API executed. Desktop validation remains SKIP/BLOCKED until authorized.");
    }
}

internal sealed class Observation
{
    public int Pid { get; set; }
    public long Hwnd { get; set; }
    public long ProcessStartUtcTicks { get; set; }
    public DateTime Utc { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public uint Dpi { get; set; }
    public string Image { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public static Observation Read(string path)
    {
        if (File.Exists(path + ".used")) throw new InvalidOperationException("Observation was already used. Capture again.");
        return JsonSerializer.Deserialize<Observation>(File.ReadAllText(path)) ?? throw new InvalidOperationException("Invalid observation.");
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct Rect { public int Left, Top, Right, Bottom; public readonly int Width => Right - Left; public readonly int Height => Bottom - Top; }
[StructLayout(LayoutKind.Sequential)]
internal struct Input
{
    public uint Type;
    public InputUnion Data;
    public static Input Mouse(uint flags, int x = 0, int y = 0) => new() { Type = 0, Data = new() { Mouse = new() { X = x, Y = y, Flags = flags } } };
    public static Input Key(ushort key, bool up) => new() { Type = 1, Data = new() { Keyboard = new() { Vk = key, Flags = up ? 2u : 0u } } };
    public static Input Unicode(char character, bool up) => new() { Type = 1, Data = new() { Keyboard = new() { Scan = character, Flags = up ? 6u : 4u } } };
}
[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
[StructLayout(LayoutKind.Sequential)]
internal struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
[StructLayout(LayoutKind.Sequential)]
internal struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public UIntPtr Extra; }
internal static class Native
{
    public delegate bool EnumCallback(IntPtr hwnd, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumCallback callback, IntPtr value);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int length);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] public static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint flags);
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint count, Input[] inputs, int size);
}
