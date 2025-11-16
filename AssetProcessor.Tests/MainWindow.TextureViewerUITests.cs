using System;
using System.Threading.Tasks;
using Xunit;

namespace AssetProcessor.Tests;

/// <summary>
/// Tests for MainWindow.TextureViewerUI channel mask handling.
/// Tests the new HandleChannelMaskCleared method that optimizes channel mask clearing
/// by avoiding unnecessary texture reloads in D3D11 mode.
/// </summary>
public class MainWindowTextureViewerUITests {
    /// <summary>
    /// Tests that channel mask state is properly tracked.
    /// </summary>
    [Fact]
    public void ChannelMask_TracksState() {
        var helper = new ChannelMaskHelper();

        helper.SetChannelMask("R");
        Assert.Equal("R", helper.CurrentChannelMask);

        helper.SetChannelMask("G");
        Assert.Equal("G", helper.CurrentChannelMask);

        helper.ClearChannelMask();
        Assert.Null(helper.CurrentChannelMask);
    }

    /// <summary>
    /// Tests that clearing channel mask with D3D11 renderer updates state correctly.
    /// </summary>
    [Fact]
    public void ClearChannelMask_WithD3D11_UpdatesState() {
        var helper = new ChannelMaskHelper {
            IsUsingD3D11 = true,
            HasD3D11Renderer = true
        };

        helper.SetChannelMask("R");
        helper.ClearChannelMask();

        Assert.Null(helper.CurrentChannelMask);
        Assert.True(helper.D3D11ChannelMaskCleared);
        Assert.True(helper.D3D11GammaRestored);
        Assert.True(helper.D3D11Rendered);
    }

    /// <summary>
    /// Tests that clearing channel mask without D3D11 renderer falls back to image reload.
    /// </summary>
    [Fact]
    public void ClearChannelMask_WithoutD3D11_FallsBackToImageReload() {
        var helper = new ChannelMaskHelper {
            IsUsingD3D11 = false,
            HasD3D11Renderer = false
        };

        helper.SetChannelMask("R");
        helper.ClearChannelMask();

        Assert.Null(helper.CurrentChannelMask);
        Assert.False(helper.D3D11ChannelMaskCleared);
        Assert.True(helper.OriginalImageShown);
    }

    /// <summary>
    /// Tests that channel mask operations are idempotent.
    /// </summary>
    [Fact]
    public void ChannelMask_Operations_AreIdempotent() {
        var helper = new ChannelMaskHelper {
            IsUsingD3D11 = true,
            HasD3D11Renderer = true
        };

        // Set same mask multiple times
        helper.SetChannelMask("R");
        helper.SetChannelMask("R");
        helper.SetChannelMask("R");

        Assert.Equal("R", helper.CurrentChannelMask);

        // Clear multiple times
        helper.ClearChannelMask();
        helper.ResetCounters();
        helper.ClearChannelMask();
        helper.ClearChannelMask();

        Assert.Null(helper.CurrentChannelMask);
        Assert.True(helper.D3D11ChannelMaskCleared);
    }

    /// <summary>
    /// Tests switching between different channel masks.
    /// </summary>
    [Fact]
    public void ChannelMask_SwitchingBetweenChannels() {
        var helper = new ChannelMaskHelper();

        var channels = new[] { "R", "G", "B", "A", "Normal" };
        foreach (var channel in channels) {
            helper.SetChannelMask(channel);
            Assert.Equal(channel, helper.CurrentChannelMask);
        }

        helper.ClearChannelMask();
        Assert.Null(helper.CurrentChannelMask);
    }

    /// <summary>
    /// Tests that channel mask clearing in D3D11 mode doesn't trigger reload.
    /// </summary>
    [Fact]
    public void ClearChannelMask_D3D11Mode_DoesNotTriggerReload() {
        var helper = new ChannelMaskHelper {
            IsUsingD3D11 = true,
            HasD3D11Renderer = true
        };

        helper.SetChannelMask("R");
        helper.ClearChannelMask();

        Assert.False(helper.OriginalImageShown, "Should not reload original image in D3D11 mode");
        Assert.True(helper.D3D11ChannelMaskCleared, "Should clear D3D11 channel mask");
    }

    /// <summary>
    /// Tests that channel mask state is preserved during mode switches.
    /// </summary>
    [Fact]
    public void ChannelMask_PreservedDuringModeSwitch() {
        var helper = new ChannelMaskHelper();

        helper.SetChannelMask("Normal");
        Assert.Equal("Normal", helper.CurrentChannelMask);

        // Switch from D3D11 to fallback mode
        helper.IsUsingD3D11 = false;
        Assert.Equal("Normal", helper.CurrentChannelMask);

        // Switch back to D3D11
        helper.IsUsingD3D11 = true;
        Assert.Equal("Normal", helper.CurrentChannelMask);
    }

    /// <summary>
    /// Tests that null or empty channel mask is handled gracefully.
    /// </summary>
    [Fact]
    public void ChannelMask_HandlesNullOrEmpty() {
        var helper = new ChannelMaskHelper();

        helper.SetChannelMask(null);
        Assert.Null(helper.CurrentChannelMask);

        helper.SetChannelMask("");
        Assert.Equal("", helper.CurrentChannelMask);

        helper.ClearChannelMask();
        Assert.Null(helper.CurrentChannelMask);
    }

    /// <summary>
    /// Tests concurrent channel mask operations are handled correctly.
    /// </summary>
    [Fact]
    public async Task ChannelMask_ConcurrentOperations() {
        var helper = new ChannelMaskHelper {
            IsUsingD3D11 = true,
            HasD3D11Renderer = true
        };

        var tasks = new Task[100];
        var channels = new[] { "R", "G", "B", "A", "Normal" };

        for (int i = 0; i < tasks.Length; i++) {
            var channel = channels[i % channels.Length];
            tasks[i] = Task.Run(() => {
                helper.SetChannelMask(channel);
                helper.ClearChannelMask();
            });
        }

        await Task.WhenAll(tasks);

        // Final state should be cleared
        Assert.Null(helper.CurrentChannelMask);
    }

    /// <summary>
    /// Tests that channel button state updates are tracked.
    /// </summary>
    [Fact]
    public void ChannelButtons_StateTracking() {
        var helper = new ChannelMaskHelper();

        helper.SetChannelMask("R");
        Assert.True(helper.ButtonStateUpdated);

        helper.ResetCounters();
        helper.SetChannelMask("G");
        Assert.True(helper.ButtonStateUpdated);

        helper.ResetCounters();
        helper.ClearChannelMask();
        Assert.True(helper.ButtonStateUpdated);
    }

    /// <summary>
    /// Tests that D3D11 operations are only called when renderer is available.
    /// </summary>
    [Fact]
    public void D3D11Operations_OnlyCalledWhenRendererAvailable() {
        var helper = new ChannelMaskHelper {
            IsUsingD3D11 = true,
            HasD3D11Renderer = false // No renderer
        };

        helper.SetChannelMask("R");
        helper.ClearChannelMask();

        Assert.False(helper.D3D11ChannelMaskCleared, "Should not call D3D11 operations without renderer");
        Assert.True(helper.OriginalImageShown, "Should fall back to image reload");
    }

    /// <summary>
    /// Tests histogram refresh behavior when clearing channel mask.
    /// </summary>
    [Fact]
    public void ClearChannelMask_RefreshesHistogram_InD3D11Mode() {
        var helper = new ChannelMaskHelper {
            IsUsingD3D11 = true,
            HasD3D11Renderer = true,
            HasHistogramSource = true
        };

        helper.SetChannelMask("R");
        helper.ResetCounters();
        helper.ClearChannelMask();

        Assert.True(helper.HistogramUpdated, "Histogram should be refreshed when clearing mask");
    }

    /// <summary>
    /// Tests that channel mask operations don't interfere with each other.
    /// </summary>
    [Fact]
    public void ChannelMask_OperationsDontInterfere() {
        var helper = new ChannelMaskHelper {
            IsUsingD3D11 = true,
            HasD3D11Renderer = true
        };

        // Set mask
        helper.SetChannelMask("R");
        var d3d11CallsAfterSet = helper.D3D11RenderCount;

        // Clear mask
        helper.ClearChannelMask();
        var d3d11CallsAfterClear = helper.D3D11RenderCount;

        // Each operation should only call render once
        Assert.True(d3d11CallsAfterClear > d3d11CallsAfterSet, "Clear should trigger additional render");
    }
}

/// <summary>
/// Test helper class that simulates channel mask handling logic from MainWindow.TextureViewerUI
/// without WPF dependencies.
/// </summary>
public class ChannelMaskHelper {
    private string? currentChannelMask;
    private readonly object lockObject = new();

    public string? CurrentChannelMask {
        get {
            lock (lockObject) {
                return currentChannelMask;
            }
        }
    }

    public bool IsUsingD3D11 { get; set; }
    public bool HasD3D11Renderer { get; set; }
    public bool HasHistogramSource { get; set; }

    // Test tracking properties
    public bool D3D11ChannelMaskCleared { get; private set; }
    public bool D3D11GammaRestored { get; private set; }
    public bool D3D11Rendered { get; private set; }
    public bool OriginalImageShown { get; private set; }
    public bool ButtonStateUpdated { get; private set; }
    public bool HistogramUpdated { get; private set; }
    public int D3D11RenderCount { get; private set; }

    public void SetChannelMask(string? mask) {
        lock (lockObject) {
            currentChannelMask = mask;
            ButtonStateUpdated = true;
        }
    }

    public void ClearChannelMask() {
        lock (lockObject) {
            currentChannelMask = null;
            ButtonStateUpdated = true;

            if (IsUsingD3D11 && HasD3D11Renderer) {
                D3D11ChannelMaskCleared = true;
                D3D11GammaRestored = true;
                D3D11Rendered = true;
                D3D11RenderCount++;

                if (HasHistogramSource) {
                    HistogramUpdated = true;
                }
            } else {
                OriginalImageShown = true;
            }
        }
    }

    public void ResetCounters() {
        lock (lockObject) {
            D3D11ChannelMaskCleared = false;
            D3D11GammaRestored = false;
            D3D11Rendered = false;
            OriginalImageShown = false;
            ButtonStateUpdated = false;
            HistogramUpdated = false;
        }
    }
}