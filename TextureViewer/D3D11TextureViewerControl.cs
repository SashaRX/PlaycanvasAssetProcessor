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

    // Mouse state for zoom/pan
    private bool isPanning = false;
    private int lastMouseX = 0;
    private int lastMouseY = 0;
    private float zoom = 1.0f;
    private float panX = 0.0f;
    private float panY = 0.0f;

    // Cursor handles for pan
    private IntPtr hCursorSizeAll = IntPtr.Zero;
    private IntPtr hCursorArrow = IntPtr.Zero;

    public D3D11TextureRenderer? Renderer => renderer;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent) {
        // Register window class (once)
        if (!classRegistered) {
            RegisterWindowClass();
            classRegistered = true;
        }

        // Create host window
        hwndHost = CreateHostWindow(hwndParent.Handle);

        // Load cursors for pan
        hCursorSizeAll = LoadCursor(IntPtr.Zero, IDC_SIZEALL);
        hCursorArrow = LoadCursor(IntPtr.Zero, IDC_ARROW);

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
            // Unregister from instance dictionary
            windowInstances.Remove(hwndHost);

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

    /// <summary>
    /// Reset zoom and pan to default values.
    /// Call this when loading a new texture.
    /// </summary>
    public void ResetView() {
        zoom = 1.0f;
        panX = 0.0f;
        panY = 0.0f;
        isPanning = false;

        if (renderer != null) {
            renderer.SetZoom(zoom);
            renderer.SetPan(panX, panY);
        }

        logger.Info("View reset to default (zoom=1.0, pan=0,0)");
    }

    /// <summary>
    /// Handle zoom from WPF mouse wheel event.
    /// Call this from PreviewMouseWheel in parent window.
    /// </summary>
    public void HandleZoomFromWpf(int delta) {
        if (renderer == null) return;

        logger.Info($"[WPF] Mouse wheel: delta={delta}, zoom before={zoom}");

        // Zoom with mouse wheel
        float zoomDelta = delta > 0 ? 1.1f : 0.9f;
        zoom *= zoomDelta;
        zoom = Math.Clamp(zoom, 0.125f, 16.0f);

        renderer.SetZoom(zoom);

        logger.Info($"[WPF] Zoom after={zoom}");
    }

    // Static dictionary to map HWND to control instances for WndProc routing
    private static readonly System.Collections.Generic.Dictionary<IntPtr, D3D11TextureViewerControl> windowInstances = new();

    private void RegisterWindowClass() {
        WNDCLASSEX wndClass = new WNDCLASSEX {
            cbSize = Marshal.SizeOf(typeof(WNDCLASSEX)),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate<WndProcDelegate>(StaticWndProc),
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

    // Static WndProc that routes to instance method
    private static readonly WndProcDelegate StaticWndProc = StaticWndProcHandler;

    private static IntPtr StaticWndProcHandler(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam) {
        // Try to find the control instance for this window
        if (windowInstances.TryGetValue(hWnd, out var control)) {
            return control.InstanceWndProc(hWnd, uMsg, wParam, lParam);
        }

        return DefWindowProc(hWnd, uMsg, wParam, lParam);
    }

    // Instance WndProc that handles mouse messages
    private IntPtr InstanceWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam) {
        const uint WM_MOUSEWHEEL = 0x020A;
        const uint WM_MBUTTONDOWN = 0x0207;
        const uint WM_MBUTTONUP = 0x0208;
        const uint WM_MOUSEMOVE = 0x0200;
        const uint WM_SETFOCUS = 0x0007;
        const uint WM_SETCURSOR = 0x0020;

        switch (uMsg) {
            case WM_MOUSEWHEEL:
                HandleMouseWheel(wParam, lParam);
                return IntPtr.Zero;

            case WM_MBUTTONDOWN:
                HandleMiddleButtonDown(lParam);
                return IntPtr.Zero;

            case WM_MBUTTONUP:
                HandleMiddleButtonUp();
                return IntPtr.Zero;

            case WM_MOUSEMOVE:
                HandleMouseMove(lParam);
                return IntPtr.Zero;

            case WM_SETFOCUS:
                logger.Debug("[Native] Window received focus");
                return IntPtr.Zero;

            case WM_SETCURSOR:
                // Set appropriate cursor based on panning state
                if (isPanning && hCursorSizeAll != IntPtr.Zero) {
                    SetCursor(hCursorSizeAll);
                } else if (hCursorArrow != IntPtr.Zero) {
                    SetCursor(hCursorArrow);
                }
                return new IntPtr(1); // TRUE - we handled it
        }

        return DefWindowProc(hWnd, uMsg, wParam, lParam);
    }

    private void HandleMouseWheel(IntPtr wParam, IntPtr lParam) {
        if (renderer == null) return;

        // Extract wheel delta from wParam (high word)
        short delta = (short)((long)wParam >> 16);

        // Extract mouse position from lParam (screen coordinates)
        int screenX = (short)((long)lParam & 0xFFFF);
        int screenY = (short)((long)lParam >> 16);

        // Convert to client coordinates
        POINT pt = new POINT { x = screenX, y = screenY };
        ScreenToClient(hwndHost, ref pt);

        logger.Info($"[Native] Mouse wheel: delta={delta}, zoom before={zoom}, mouse at client ({pt.x}, {pt.y})");

        // Get window size
        int width = (int)ActualWidth;
        int height = (int)ActualHeight;
        if (width <= 0 || height <= 0) {
            // Fallback to simple zoom if no size
            zoom *= delta > 0 ? 1.1f : 0.9f;
            zoom = Math.Clamp(zoom, 0.125f, 16.0f);
            renderer.SetZoom(zoom);
            return;
        }

        // Calculate normalized mouse position (0 to 1, top-left is 0,0)
        // Since texture pivot is at top-left (0,0 UV), we use these coordinates directly
        float mouseNormX = pt.x / (float)width;
        float mouseNormY = pt.y / (float)height;

        // Apply zoom
        float oldZoom = zoom;
        float zoomDelta = delta > 0 ? 1.1f : 0.9f;
        zoom *= zoomDelta;
        zoom = Math.Clamp(zoom, 0.125f, 16.0f);

        // Adjust pan to keep point under cursor stationary
        // Formula: newPan = oldPan + mousePos * (1/oldZoom - 1/newZoom)
        // Pivot is at top-left (0,0), so we use mouseNorm directly without centering
        panX += mouseNormX * (1.0f / oldZoom - 1.0f / zoom);
        panY += mouseNormY * (1.0f / oldZoom - 1.0f / zoom);

        renderer.SetZoom(zoom);
        renderer.SetPan(panX, panY);

        logger.Info($"[Native] Zoom after={zoom}, pan=({panX:F3}, {panY:F3})");
    }

    private void HandleMiddleButtonDown(IntPtr lParam) {
        if (renderer == null) return;

        // Extract mouse position from lParam
        lastMouseX = (short)((long)lParam & 0xFFFF);
        lastMouseY = (short)((long)lParam >> 16);

        logger.Info($"[Native] Middle button down at {lastMouseX}, {lastMouseY}");

        isPanning = true;
        SetCapture(hwndHost);

        // Change cursor to 4-directional (SizeAll) for pan
        if (hCursorSizeAll != IntPtr.Zero) {
            SetCursor(hCursorSizeAll);
        }
    }

    private void HandleMiddleButtonUp() {
        logger.Info("[Native] Middle button up - stop panning");

        isPanning = false;
        ReleaseCapture();

        // Restore cursor to arrow
        if (hCursorArrow != IntPtr.Zero) {
            SetCursor(hCursorArrow);
        }
    }

    private void HandleMouseMove(IntPtr lParam) {
        // Set focus when mouse moves over the window to receive mouse wheel events
        if (GetFocus() != hwndHost) {
            SetFocus(hwndHost);
        }

        if (!isPanning || renderer == null) return;

        // Extract mouse position from lParam
        int currentX = (short)((long)lParam & 0xFFFF);
        int currentY = (short)((long)lParam >> 16);

        int deltaX = currentX - lastMouseX;
        int deltaY = currentY - lastMouseY;

        // Get window size for proper pan calculation
        int width = (int)ActualWidth;
        int height = (int)ActualHeight;
        if (width <= 0 || height <= 0) return;

        // Convert screen delta to normalized texture space
        // Account for zoom level and window size to ensure uniform pan speed
        float panSpeedX = 1.0f / (width * zoom);
        float panSpeedY = 1.0f / (height * zoom);
        panX -= deltaX * panSpeedX;  // X: left/right
        panY -= deltaY * panSpeedY;  // Y: up/down (inverted for correct direction)

        renderer.SetPan(panX, panY);

        lastMouseX = currentX;
        lastMouseY = currentY;
    }

    private IntPtr CreateHostWindow(IntPtr hwndParent) {
        IntPtr hwnd = CreateWindowEx(
            0,  // No extended style - we handle mouse events natively now
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

        // Register this window in the instance dictionary for WndProc routing
        windowInstances[hwnd] = this;

        logger.Info($"Created D3D11 host window with native mouse handling: hwnd={hwnd:X}");
        return hwnd;
    }

    #region Win32 Interop

    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT {
        public int x;
        public int y;
    }

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

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    // Cursor resource IDs
    private const int IDC_ARROW = 32512;
    private const int IDC_SIZEALL = 32646;

    #endregion
}
