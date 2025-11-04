using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using NLog;

namespace AssetProcessor.TextureViewer;

/// <summary>
/// WPF control that hosts D3D11 texture renderer.
/// Uses HwndHost to embed native D3D11 window into WPF.
/// </summary>
public class D3D11TextureViewerControl : HwndHost {
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    private IntPtr hwndHost = IntPtr.Zero;
    private D3D11TextureRenderer? renderer;

    private const string WindowClassName = "D3D11TextureViewerHostWindow";
    private static bool classRegistered = false;

    public D3D11TextureRenderer? Renderer => renderer;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent) {
        // Register window class (once)
        if (!classRegistered) {
            RegisterWindowClass();
            classRegistered = true;
        }

        // Create host window
        hwndHost = CreateHostWindow(hwndParent.Handle);

        // Initialize D3D11 renderer
        int width = (int)ActualWidth;
        int height = (int)ActualHeight;

        if (width <= 0) width = 800;
        if (height <= 0) height = 600;

        try {
            renderer = new D3D11TextureRenderer();
            renderer.Initialize(hwndHost, width, height);

            logger.Info($"D3D11 texture viewer control initialized: {width}x{height}");
        } catch (Exception ex) {
            logger.Error(ex, "Failed to initialize D3D11 renderer");
        }

        return new HandleRef(this, hwndHost);
    }

    protected override void DestroyWindowCore(HandleRef hwnd) {
        renderer?.Dispose();
        renderer = null;

        if (hwndHost != IntPtr.Zero) {
            DestroyWindow(hwndHost);
            hwndHost = IntPtr.Zero;
        }

        logger.Info("D3D11 texture viewer control destroyed");
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) {
        base.OnRenderSizeChanged(sizeInfo);

        int width = (int)sizeInfo.NewSize.Width;
        int height = (int)sizeInfo.NewSize.Height;

        if (width > 0 && height > 0) {
            renderer?.Resize(width, height);
            logger.Debug($"D3D11 viewer resized: {width}x{height}");
        }
    }

    /// <summary>
    /// Render one frame. Call this from CompositionTarget.Rendering event or manually.
    /// </summary>
    public void RenderFrame() {
        renderer?.Render();
    }

    private void RegisterWindowClass() {
        WNDCLASSEX wndClass = new WNDCLASSEX {
            cbSize = Marshal.SizeOf(typeof(WNDCLASSEX)),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate<WndProcDelegate>(DefaultWndProc),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = GetModuleHandle(null),
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = WindowClassName,
            hIconSm = IntPtr.Zero
        };

        ushort atom = RegisterClassEx(ref wndClass);
        if (atom == 0) {
            throw new Exception($"Failed to register window class: {Marshal.GetLastWin32Error()}");
        }
    }

    private static readonly WndProcDelegate DefaultWndProc = DefWindowProcManaged;

    private static IntPtr DefWindowProcManaged(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam) {
        return DefWindowProc(hWnd, uMsg, wParam, lParam);
    }

    private IntPtr CreateHostWindow(IntPtr hwndParent) {
        IntPtr hwnd = CreateWindowEx(
            0,
            WindowClassName,
            "",
            WS_CHILD | WS_VISIBLE,
            0, 0,
            (int)ActualWidth, (int)ActualHeight,
            hwndParent,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (hwnd == IntPtr.Zero) {
            throw new Exception($"Failed to create host window: {Marshal.GetLastWin32Error()}");
        }

        return hwnd;
    }

    #region Win32 Interop

    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct WNDCLASSEX {
        public int cbSize;
        public int style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x, int y,
        int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    #endregion
}
