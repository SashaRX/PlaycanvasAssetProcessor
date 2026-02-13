using AssetProcessor.Helpers;
using AssetProcessor.Settings;
using AssetProcessor.ViewModels;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace AssetProcessor {
    /// <summary>
    /// D3D11 viewer lifecycle: window activation (Alt+Tab fix), MainWindow_Loaded,
    /// render loop, mouse/keyboard handlers.
    /// Texture loading logic is in D3D11Viewer.TextureLoading.cs.
    /// </summary>
    public partial class MainWindow {

        // Pending assets data when loading completes while window is inactive
        private AssetsLoadedEventArgs? _pendingAssetsData = null;

        // Alt+Tab fix: Track window active state to skip render loops when inactive
        private CancellationTokenSource? _activationCts;
        private readonly object _activationLock = new();
        private HwndSource? _hwndSource;

        // Win32 constants
        private const int WM_ACTIVATEAPP = 0x001C;
        private const int WM_ACTIVATE = 0x0006;
        private const int WA_INACTIVE = 0;
        private const int WM_MOUSEWHEEL = 0x020A;

        #region Alt+Tab Fix (Window Activation)

        private void SetupAltTabFix() {
            SourceInitialized += (s, e) => {
                _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                _hwndSource?.AddHook(WndProcHook);
            };

            Closed += (s, e) => {
                _hwndSource?.RemoveHook(WndProcHook);
                _hwndSource = null;
            };
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
            if (msg == WM_ACTIVATEAPP) {
                bool isActivating = wParam != IntPtr.Zero;

                if (!isActivating) {
                    _isWindowActive = false;
                    AssetProcessor.TextureViewer.D3D11TextureRenderer.GlobalRenderingEnabled = false;

                    lock (_activationLock) {
                        _activationCts?.Cancel();
                    }
                } else {
                    ScheduleDelayedActivation();
                }
            } else if (msg == WM_ACTIVATE) {
                int activateState = (int)(wParam.ToInt64() & 0xFFFF);

                if (activateState == WA_INACTIVE) {
                    _isWindowActive = false;
                    AssetProcessor.TextureViewer.D3D11TextureRenderer.GlobalRenderingEnabled = false;

                    lock (_activationLock) {
                        _activationCts?.Cancel();
                    }
                }
            }

            return IntPtr.Zero;
        }

        private void ScheduleDelayedActivation() {
            lock (_activationLock) {
                _activationCts?.Cancel();
                _activationCts = new CancellationTokenSource();
            }

            var cts = _activationCts;
            bool hasPendingData = _pendingAssetsData != null;

            _ = Task.Run(async () => {
                try {
                    if (hasPendingData) {
                        await Task.Delay(200, cts.Token);
                    } else {
                        for (int i = 0; i < 10; i++) {
                            await Task.Delay(200, cts.Token);
                        }
                    }

                    if (cts.Token.IsCancellationRequested) return;

                    await Dispatcher.InvokeAsync(() => {
                        if (IsActive && !cts.Token.IsCancellationRequested) {
                            _isWindowActive = true;
                            AssetProcessor.TextureViewer.D3D11TextureRenderer.GlobalRenderingEnabled = true;

                            D3D11TextureViewer?.ApplyPendingResize();

                            if (_pendingAssetsData != null) {
                                var pendingData = _pendingAssetsData;
                                _pendingAssetsData = null;
                                ApplyAssetsToUI(pendingData);
                            }
                        }
                    });
                } catch (OperationCanceledException) { }
            });
        }

        #endregion

        #region MainWindow_Loaded

        private void MainWindow_Loaded(object sender, RoutedEventArgs e) {
            logger.Info("MainWindow loaded - D3D11 viewer ready");

            ComponentDispatcher.ThreadFilterMessage += ComponentDispatcher_ThreadFilterMessage;

            bool useD3D11 = AppSettings.Default.UseD3D11Preview;
            _ = ApplyRendererPreferenceAsync(useD3D11);

            // Configure HelixViewport3D CameraController
            viewPort3d.Loaded += (_, __) => {
                if (viewPort3d.CameraController != null) {
                    viewPort3d.CameraController.IsInertiaEnabled = false;
                    viewPort3d.CameraController.InertiaFactor = 0;
                    viewPort3d.CameraController.ZoomSensitivity = 1;
                }
            };

            // Load saved column layouts for all DataGrids
            dataGridLayoutService.LoadColumnOrder(TexturesDataGrid, GetColumnOrderSettingName(TexturesDataGrid));
            dataGridLayoutService.LoadColumnOrder(ModelsDataGrid, GetColumnOrderSettingName(ModelsDataGrid));
            dataGridLayoutService.LoadColumnOrder(MaterialsDataGrid, GetColumnOrderSettingName(MaterialsDataGrid));

            dataGridLayoutService.LoadColumnWidths(TexturesDataGrid, GetColumnWidthsSettingName(TexturesDataGrid));
            dataGridLayoutService.LoadColumnWidths(ModelsDataGrid, GetColumnWidthsSettingName(ModelsDataGrid));
            dataGridLayoutService.LoadColumnWidths(MaterialsDataGrid, GetColumnWidthsSettingName(MaterialsDataGrid));

            LoadAllColumnVisibility();
            SubscribeToColumnWidthChanges();
            RestoreRightPanelWidth();
            InitializeDarkThemeCheckBox();
            UpdateExportCounts();

            if (tabControl.SelectedItem is TabItem selectedTab && selectedTab.Header?.ToString() == "Textures") {
                exportToolsPanel.TextureToolsPanel.Visibility = Visibility.Visible;
            }
        }

        private void InitializeDarkThemeCheckBox() {
            DarkThemeCheckBox.IsChecked = ThemeHelper.IsDarkTheme;
        }

        private void RestoreRightPanelWidth() {
            double savedWidth = AppSettings.Default.RightPanelWidth;
            if (savedWidth >= 256 && savedWidth <= 512) {
                PreviewColumn.Width = new GridLength(savedWidth);
                isViewerVisible = true;
                viewModel.ToggleViewButtonContent = "◄";
            } else if (savedWidth <= 0) {
                PreviewColumn.Width = new GridLength(0);
                PreviewColumn.MinWidth = 0;
                isViewerVisible = false;
                viewModel.ToggleViewButtonContent = "►";
            }
        }

        private void LoadAllColumnVisibility() {
            LoadColumnVisibilityWithMenu(TexturesDataGrid, nameof(AppSettings.TexturesColumnVisibility),
                (System.Windows.Controls.ContextMenu)FindResource("TextureColumnHeaderContextMenu"));
            LoadColumnVisibilityWithMenu(ModelsDataGrid, nameof(AppSettings.ModelsColumnVisibility),
                (System.Windows.Controls.ContextMenu)FindResource("ModelColumnHeaderContextMenu"));
            LoadColumnVisibilityWithMenu(MaterialsDataGrid, nameof(AppSettings.MaterialsColumnVisibility),
                (System.Windows.Controls.ContextMenu)FindResource("MaterialColumnHeaderContextMenu"));

            dataGridLayoutService.FillRemainingSpace(TexturesDataGrid, dataGridLayoutService.HasSavedWidths(TexturesDataGrid));
            dataGridLayoutService.FillRemainingSpace(ModelsDataGrid, dataGridLayoutService.HasSavedWidths(ModelsDataGrid));
            dataGridLayoutService.FillRemainingSpace(MaterialsDataGrid, dataGridLayoutService.HasSavedWidths(MaterialsDataGrid));
        }

        #endregion

        #region Render Loop

        private void OnD3D11Rendering(object? sender, EventArgs e) {
            if (!_isWindowActive) return;

            if (texturePreviewService.IsD3D11RenderLoopEnabled) {
                D3D11TextureViewer?.RenderFrame();
            }
        }

        private void TexturePreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e) {
            // D3D11TextureViewerControl handles resize via OnRenderSizeChanged
        }

        #endregion

        #region Mouse/Keyboard Handlers

        private void TexturePreviewViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            // ComponentDispatcher_ThreadFilterMessage handles zoom for HwndHost
        }

        private void TexturePreviewViewport_MouseEnter(object sender, MouseEventArgs e) {
            TexturePreviewViewport?.Focus();
        }

        private void TexturePreviewViewport_MouseLeave(object sender, MouseEventArgs e) {
            if (TexturePreviewViewport == null || !TexturePreviewViewport.IsKeyboardFocusWithin) return;

            DependencyObject focusScope = FocusManager.GetFocusScope(TexturePreviewViewport);
            if (focusScope != null) {
                FocusManager.SetFocusedElement(focusScope, null);
            }
            Keyboard.ClearFocus();
        }

        /// <summary>
        /// Win32 message hook - catches WM_MOUSEWHEEL before WPF.
        /// Required because HwndHost doesn't participate in WPF routed events.
        /// Also handles HelixViewport3D zoom to bypass ScrollViewer interception.
        /// </summary>
        private void ComponentDispatcher_ThreadFilterMessage(ref System.Windows.Interop.MSG msg, ref bool handled) {
            if (msg.message == WM_MOUSEWHEEL && !handled) {
                int x = (short)(msg.lParam.ToInt64() & 0xFFFF);
                int y = (short)((msg.lParam.ToInt64() >> 16) & 0xFFFF);
                Point screenPoint = new Point(x, y);

                // Check HelixViewport3D (model viewer) first
                if (viewModel.ActiveViewerType == ViewModels.ViewerType.Model && viewPort3d != null) {
                    try {
                        Point viewportPoint = viewPort3d.PointFromScreen(screenPoint);
                        if (viewportPoint.X >= 0 && viewportPoint.Y >= 0 &&
                            viewportPoint.X <= viewPort3d.ActualWidth &&
                            viewportPoint.Y <= viewPort3d.ActualHeight) {
                            short delta = (short)((msg.wParam.ToInt64() >> 16) & 0xFFFF);
                            ZoomCamera(delta);
                            handled = true;
                            return;
                        }
                    } catch { }
                }

                // Check D3D11 texture viewer
                if (TexturePreviewViewport != null && texturePreviewService.IsUsingD3D11Renderer &&
                    D3D11TextureViewer != null && D3D11TextureViewer.Visibility == Visibility.Visible) {
                    try {
                        Point viewportPoint = TexturePreviewViewport.PointFromScreen(screenPoint);
                        if (viewportPoint.X >= 0 && viewportPoint.Y >= 0 &&
                            viewportPoint.X <= TexturePreviewViewport.ActualWidth &&
                            viewportPoint.Y <= TexturePreviewViewport.ActualHeight) {
                            short delta = (short)((msg.wParam.ToInt64() >> 16) & 0xFFFF);
                            D3D11TextureViewer.HandleZoomFromWpf(delta, x, y);
                            handled = true;
                        }
                    } catch { }
                }
            }
        }

        private void TextureViewerScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            // ComponentDispatcher_ThreadFilterMessage handles zoom
        }

        private void D3D11TextureViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            // ComponentDispatcher_ThreadFilterMessage handles zoom
        }

        #endregion
    }
}
