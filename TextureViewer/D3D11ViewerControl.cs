using System;
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using NLog;

namespace AssetProcessor.TextureViewer;

/// <summary>
/// WPF control that hosts D3D11 texture renderer using HwndHost.
/// Provides native Win32 window for DirectX rendering in WPF.
/// </summary>
public sealed class D3D11ViewerControl : HwndHost, IDisposable {
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    private const string WindowClassName = "D3D11ViewerControlWindow";
    private static bool windowClassRegistered = false;
    private IntPtr hwnd;
    private D3D11TextureRenderer? renderer;
    private TextureData? currentTexture;
    private bool isDisposed;
    private int currentWidth;
    private int currentHeight;

    public D3D11ViewerControl() {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) {
        // HwndHost will call BuildWindowCore when ready
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent) {
        // Register window class (only once)
        if (!windowClassRegistered) {
            RegisterWindowClass();
            windowClassRegistered = true;
        }

        // Get initial size
        currentWidth = (int)ActualWidth;
        currentHeight = (int)ActualHeight;

        if (currentWidth == 0) currentWidth = 512;
        if (currentHeight == 0) currentHeight = 512;

        // Create native window
        hwnd = CreateHostWindow(hwndParent.Handle);

        if (hwnd == IntPtr.Zero) {
            throw new InvalidOperationException("Failed to create host window for D3D11 renderer");
        }

        logger.Info($"Created D3D11 host window: 0x{hwnd:X}");

        // Initialize D3D11 renderer
        try {
            logger.Info($"Creating D3D11TextureRenderer: {currentWidth}x{currentHeight}");
            renderer = new D3D11TextureRenderer();
            renderer.Initialize(hwnd, currentWidth, currentHeight);
            logger.Info("D3D11TextureRenderer initialized successfully");

            // Render current texture if one was set before renderer was ready
            if (currentTexture != null) {
                RenderCurrentTexture();
            }
        } catch (Exception ex) {
            logger.Error(ex, "Failed to initialize D3D11TextureRenderer");
            throw;
        }

        return new HandleRef(this, hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd) {
        logger.Info("Destroying D3D11 host window");
        DestroyWindow(hwnd.Handle);
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
        const int WM_SIZE = 0x0005;

        if (msg == WM_SIZE) {
            int newWidth = lParam.ToInt32() & 0xFFFF;
            int newHeight = (lParam.ToInt32() >> 16) & 0xFFFF;

            if (newWidth > 0 && newHeight > 0 && (newWidth != currentWidth || newHeight != currentHeight)) {
                currentWidth = newWidth;
                currentHeight = newHeight;

                try {
                    logger.Info($"Resizing D3D11 renderer to {newWidth}x{newHeight}");
                    renderer?.Resize(newWidth, newHeight);

                    // Re-render current texture at new size
                    if (currentTexture != null) {
                        RenderCurrentTexture();
                    }
                } catch (Exception ex) {
                    logger.Error(ex, "Failed to resize D3D11 renderer");
                }
            }

            handled = true;
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    /// <summary>
    /// Load and display a KTX2 texture file.
    /// </summary>
    public void LoadKtx2File(string filePath) {
        if (isDisposed) {
            throw new ObjectDisposedException(nameof(D3D11ViewerControl));
        }

        try {
            logger.Info($"Loading KTX2 file: {filePath}");

            // Dispose previous texture
            currentTexture?.Dispose();
            currentTexture = null;

            // Load KTX2 via libktx
            currentTexture = Ktx2TextureLoader.LoadFromFile(filePath);

            logger.Info($"Loaded KTX2: {currentTexture.Width}x{currentTexture.Height}, " +
                       $"{currentTexture.MipCount} mips, format: {currentTexture.SourceFormat}");

            // Render if renderer is ready
            if (renderer != null) {
                RenderCurrentTexture();
            }
        } catch (Exception ex) {
            logger.Error(ex, $"Failed to load KTX2 file: {filePath}");
            currentTexture?.Dispose();
            currentTexture = null;
            throw;
        }
    }

    /// <summary>
    /// Load and display a PNG texture file.
    /// </summary>
    public void LoadPngFile(string filePath) {
        if (isDisposed) {
            throw new ObjectDisposedException(nameof(D3D11ViewerControl));
        }

        try {
            logger.Info($"Loading PNG file: {filePath}");

            // Dispose previous texture
            currentTexture?.Dispose();
            currentTexture = null;

            // Load PNG via ImageSharp
            currentTexture = PngTextureLoader.LoadFromFile(filePath);

            logger.Info($"Loaded PNG: {currentTexture.Width}x{currentTexture.Height}");

            // Render if renderer is ready
            if (renderer != null) {
                RenderCurrentTexture();
            }
        } catch (Exception ex) {
            logger.Error(ex, $"Failed to load PNG file: {filePath}");
            currentTexture?.Dispose();
            currentTexture = null;
            throw;
        }
    }

    /// <summary>
    /// Clear the current texture.
    /// </summary>
    public void Clear() {
        currentTexture?.Dispose();
        currentTexture = null;

        // Render empty (will show clear color)
        renderer?.Render();
    }

    private void RenderCurrentTexture() {
        if (renderer == null || currentTexture == null) {
            return;
        }

        try {
            logger.Info("Loading and rendering texture to D3D11 surface");
            renderer.LoadTexture(currentTexture);
            renderer.Render();
        } catch (Exception ex) {
            logger.Error(ex, "Failed to render texture");
            throw;
        }
    }

    protected override void Dispose(bool disposing) {
        if (!isDisposed) {
            if (disposing) {
                logger.Info("Disposing D3D11ViewerControl");

                currentTexture?.Dispose();
                currentTexture = null;

                renderer?.Dispose();
                renderer = null;
            }

            isDisposed = true;
        }

        base.Dispose(disposing);
    }

    #region Win32 Interop

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static void RegisterWindowClass() {
        // Create a delegate for DefWindowProc
        WndProcDelegate wndProcDelegate = DefWindowProc;

        WNDCLASS wndClass = new WNDCLASS {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProcDelegate),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = GetModuleHandle(null),
            hIcon = IntPtr.Zero,
            hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW),
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = WindowClassName
        };

        ushort classAtom = RegisterClass(ref wndClass);
        if (classAtom == 0) {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to register window class. Error: {error}");
        }

        // Keep delegate alive to prevent garbage collection
        GC.KeepAlive(wndProcDelegate);
    }

    private IntPtr CreateHostWindow(IntPtr hwndParent) {
        return CreateWindowEx(
            0,
            WindowClassName,
            "",
            WS_CHILD | WS_VISIBLE,
            0, 0,
            currentWidth, currentHeight,
            hwndParent,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private static readonly IntPtr IDC_ARROW = new IntPtr(32512);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

    #endregion
}
