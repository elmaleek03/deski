using System;
using System.IO;
using System.Reflection;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;

internal static class Program
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_APP = 0x8000;
    private const int WM_RBUTTONUP = 0x0205;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x1;
    private const int NIF_ICON = 0x2;
    private const int NIF_TIP = 0x4;

    private const uint MOD_ALT = 0x0001;
    private const uint VK_1 = 0x31; // '1'

    // tray menu
    private const uint MF_STRING = 0x0000;
    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint ID_TRAY_EXIT = 1001;

    private static IntPtr _hwnd;
    private static NotifyIconData _tray;
    private static WndProc _wndProcDelegate;
    private static IntPtr _currentHicon = IntPtr.Zero;
    private static float _dpiScale = 1.0f;

    // for DPI awareness
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ------- P/Invoke -------
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("shell32.dll")]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NotifyIconData lpdata);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    private static IntPtr _nativeHandle = IntPtr.Zero;
    private static string? _nativePath;

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern sbyte GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();


    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(
        IntPtr hMenu,
        uint uFlags,
        int x, int y,
        IntPtr hWnd,
        IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    // ------- Main -------

    private static void EnsureVirtualDesktopDllLoaded()
    {
        if (_nativeHandle != IntPtr.Zero)
            return;

        // Extract the embedded DLL to a temp folder
        string tempDir = Path.Combine(Path.GetTempPath(), "DeskMiniVD");
        Directory.CreateDirectory(tempDir);

        string dllPath = Path.Combine(tempDir, "VirtualDesktopAccessor.dll");

        if (!File.Exists(dllPath))
        {
            using Stream? resStream =
                Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream("VirtualDesktopAccessor.dll");
            if (resStream == null)
                throw new InvalidOperationException("Embedded VirtualDesktopAccessor.dll not found.");

            using FileStream fs = File.Create(dllPath);
            resStream.CopyTo(fs);
        }

        _nativeHandle = LoadLibrary(dllPath);
        if (_nativeHandle == IntPtr.Zero)
            throw new InvalidOperationException("Failed to LoadLibrary VirtualDesktopAccessor.dll.");

        _nativePath = dllPath;
    }

    private static void UnloadVirtualDesktopDll()
    {
        if (_nativeHandle != IntPtr.Zero)
        {
            FreeLibrary(_nativeHandle);
            _nativeHandle = IntPtr.Zero;

            try
            {
                if (!string.IsNullOrEmpty(_nativePath) && File.Exists(_nativePath))
                    File.Delete(_nativePath);
            }
            catch
            {
                // best-effort; ok if delete fails
            }
        }
    }


    [STAThread]
    private static void Main()
    {
        EnsureVirtualDesktopDllLoaded();
        // DPI aware for sharp icons
        try { SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); } catch { }

        try
        {
            _dpiScale = GetDpiForSystem() / 96f;
            if (_dpiScale <= 0) _dpiScale = 1.0f;
        }
        catch { _dpiScale = 1.0f; }

        _hwnd = CreateMessageWindow();

        // Alt+1..9 hotkeys
        for (int i = 1; i <= 9; i++)
        {
            RegisterHotKey(_hwnd, i, MOD_ALT, VK_1 + (uint)(i - 1));
        }

        AddTrayIcon();

        // Poll current desktop to keep icon updated
        var timer = new Timer(_ =>
        {
            try
            {
                int raw = VirtualDesktop.GetCurrentDesktopNumber(); // 0-based
                int num = raw + 1;
                if (num <= 0) num = 1;
                UpdateTrayIcon(num);
            }
            catch { }
        }, null, 0, 200);

        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        // cleanup
        for (int i = 1; i <= 9; i++)
            UnregisterHotKey(_hwnd, i);

        RemoveTrayIcon();
        if (_currentHicon != IntPtr.Zero)
            DestroyIcon(_currentHicon);

        UnloadVirtualDesktopDll();   // <<< add this
    }

    // ------- Window / WndProc -------

    private static IntPtr CreateMessageWindow()
    {
        const string CLASS_NAME = "DeskMiniMessageWindow";

        _wndProcDelegate = WndProcImpl;

        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = GetModuleHandle(null),
            lpszClassName = CLASS_NAME
        };

        RegisterClassEx(ref wc);

        return CreateWindowEx(
            0,
            CLASS_NAME,
            "",
            0,
            0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
    }

    private static IntPtr WndProcImpl(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32(); // 1..9
            int targetIndexZeroBased = id - 1;
            if (targetIndexZeroBased < 0) targetIndexZeroBased = 0;
            VirtualDesktop.GoToDesktopNumber(targetIndexZeroBased);
            return IntPtr.Zero;
        }

        // tray callback
        if (msg == WM_APP)
        {
            if (lParam.ToInt32() == WM_RBUTTONUP)
            {
                ShowTrayMenu();
                return IntPtr.Zero;
            }
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    // ------- Tray & menu helpers -------

    private static void ShowTrayMenu()
    {
        if (!GetCursorPos(out POINT pt))
            return;

        IntPtr hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero)
            return;

        AppendMenu(hMenu, MF_STRING, ID_TRAY_EXIT, "Exit");

        uint cmd = TrackPopupMenuEx(
            hMenu,
            TPM_LEFTALIGN | TPM_BOTTOMALIGN | TPM_RETURNCMD,
            pt.X, pt.Y,
            _hwnd,
            IntPtr.Zero);

        DestroyMenu(hMenu);

        if (cmd == ID_TRAY_EXIT)
        {
            PostQuitMessage(0); // ends message loop → app exits
        }
    }

    private static void AddTrayIcon()
    {
        int num = 1;
        try
        {
            int raw = VirtualDesktop.GetCurrentDesktopNumber();
            num = Math.Max(1, raw + 1);
        }
        catch { }

        _currentHicon = CreateNumberIcon(num);

        _tray = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_APP,
            hIcon = _currentHicon,
            szTip = $"Desktop {num}"
        };

        Shell_NotifyIcon(NIM_ADD, ref _tray);
    }

    private static void UpdateTrayIcon(int desktop)
    {
        _tray.szTip = $"Desktop {desktop}";

        if (_currentHicon != IntPtr.Zero)
        {
            DestroyIcon(_currentHicon);
            _currentHicon = IntPtr.Zero;
        }

        _currentHicon = CreateNumberIcon(desktop);
        _tray.hIcon = _currentHicon;

        Shell_NotifyIcon(NIM_MODIFY, ref _tray);
    }

    private static void RemoveTrayIcon()
    {
        Shell_NotifyIcon(NIM_DELETE, ref _tray);
    }

    // ------- Icon drawing -------

    private static IntPtr CreateNumberIcon(int number)
    {
        int baseSize = 16;
        int size = (int)Math.Round(baseSize * _dpiScale);
        if (size < 16) size = 16;
        if (size > 64) size = 64;

        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            string text = number.ToString();
            float fontSize = size * 0.9f; // big text

            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);

            SizeF textSize = g.MeasureString(text, font);
            float textX = (size - textSize.Width) / 2f;
            float textY = (size - textSize.Height) / 2f;

            // outline so it stands on both light/dark backgrounds
            using var outlineBrush = new SolidBrush(Color.FromArgb(220, 0, 0, 0));
            using var textBrush = new SolidBrush(Color.White);

            g.DrawString(text, font, outlineBrush, textX + 1, textY + 1);
            g.DrawString(text, font, textBrush, textX, textY);
        }

        return bmp.GetHicon();
    }
}
