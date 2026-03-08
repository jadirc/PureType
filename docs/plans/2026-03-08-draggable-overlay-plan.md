# Draggable Overlay Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make the floating status overlay draggable with position persistence, double-click reset to center, and middle-click hide.

**Architecture:** Enable hit-testing on the overlay window while keeping it non-activating via Win32 styles. Use manual mouse tracking (not `DragMove()` which activates the window). Store overlay position as `OverlayLeft`/`OverlayTop` in `WindowSettings`. The overlay fires events (`PositionChanged`, `HideRequested`) that MainWindow handles to persist settings.

**Tech Stack:** C# (.NET 8), WPF, Win32 interop, xUnit for tests.

---

### Task 1: Add OverlayLeft / OverlayTop settings

**Files:**
- Modify: `src/PureType/Services/SettingsService.cs` (WindowSettings record, line 59-71)
- Test: `tests/PureType.Tests/Services/SettingsServiceTests.cs`

**Step 1: Write the failing tests**

Add to `SettingsServiceTests.cs`:

```csharp
[Fact]
public void OverlayPosition_defaults_to_null()
{
    var settings = new AppSettings();
    Assert.Null(settings.Window.OverlayLeft);
    Assert.Null(settings.Window.OverlayTop);
}

[Fact]
public void OverlayPosition_roundtrips_through_json()
{
    var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "settings.json");
    try
    {
        var svc = new SettingsService(path);
        var original = new AppSettings
        {
            Window = new WindowSettings { OverlayLeft = 100.5, OverlayTop = 50.0 }
        };
        svc.Save(original);
        var loaded = svc.Load();
        Assert.Equal(100.5, loaded.Window.OverlayLeft);
        Assert.Equal(50.0, loaded.Window.OverlayTop);
    }
    finally { Directory.Delete(dir, true); }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter "OverlayPosition"`
Expected: FAIL — `WindowSettings` has no `OverlayLeft`/`OverlayTop` properties.

**Step 3: Write minimal implementation**

In `src/PureType/Services/SettingsService.cs`, add to the `WindowSettings` record (after `ShowOverlay`, line 70):

```csharp
public record WindowSettings
{
    public double? Left { get; init; }
    public double? Top { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public bool StartMinimized { get; init; }
    public double? SettingsWidth { get; init; }
    public double? SettingsHeight { get; init; }
    public string Theme { get; init; } = "Auto";
    public string LogLevel { get; init; } = "Information";
    public bool ShowOverlay { get; init; } = true;
    public double? OverlayLeft { get; init; }
    public double? OverlayTop { get; init; }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test --filter "OverlayPosition"`
Expected: PASS

**Step 5: Commit**

```bash
git add src/PureType/Services/SettingsService.cs tests/PureType.Tests/Services/SettingsServiceTests.cs
git commit -m "feat: add OverlayLeft/OverlayTop settings to WindowSettings"
```

---

### Task 2: Make overlay draggable with manual mouse tracking

**Files:**
- Modify: `src/PureType/StatusOverlayWindow.xaml` (line 10, remove `IsHitTestVisible="False"`)
- Modify: `src/PureType/StatusOverlayWindow.xaml.cs`

**Step 1: Enable hit-testing in XAML**

In `StatusOverlayWindow.xaml`, change line 10 from:

```xml
IsHitTestVisible="False"
```

to:

```xml
IsHitTestVisible="True"
```

**Step 2: Add drag logic, double-click reset, middle-click hide, and position restore**

Replace the entire `StatusOverlayWindow.xaml.cs` with:

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace PureType;

public partial class StatusOverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private bool _dragging;
    private Point _dragStart;
    private double _windowLeft, _windowTop;

    /// <summary>Fired when the user finishes dragging. Args: (left, top).</summary>
    public event Action<double, double>? PositionChanged;

    /// <summary>Fired when the user middle-clicks to hide the overlay.</summary>
    public event Action? HideRequested;

    /// <summary>Saved position to restore; set before Show().</summary>
    public double? RestoreLeft { get; set; }

    /// <summary>Saved position to restore; set before Show().</summary>
    public double? RestoreTop { get; set; }

    public StatusOverlayWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };

        Loaded += (_, _) =>
        {
            if (RestoreLeft.HasValue && RestoreTop.HasValue && IsOnScreen(RestoreLeft.Value, RestoreTop.Value))
            {
                Left = RestoreLeft.Value;
                Top = RestoreTop.Value;
            }
            else
            {
                PositionTopCenter();
            }
        };
    }

    public void UpdateState(bool recording, bool muted, string statusText, Color dotColor)
    {
        StatusDot.Fill = new SolidColorBrush(dotColor);
        StatusText.Text = statusText;
    }

    // ── Drag ────────────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = PointToScreen(e.GetPosition(this));
        _windowLeft = Left;
        _windowTop = Top;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        var current = PointToScreen(e.GetPosition(this));
        Left = _windowLeft + (current.X - _dragStart.X);
        Top = _windowTop + (current.Y - _dragStart.Y);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        PositionChanged?.Invoke(Left, Top);
        e.Handled = true;
    }

    // ── Double-click: reset to center ───────────────────────────────────

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _dragging = false;
            ReleaseMouseCapture();
            PositionTopCenter();
            PositionChanged?.Invoke(Left, Top);
            e.Handled = true;
        }
    }

    // ── Middle-click: hide overlay ──────────────────────────────────────

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            HideRequested?.Invoke();
            e.Handled = true;
            return;
        }
        base.OnMouseDown(e);
    }

    // ── Positioning ─────────────────────────────────────────────────────

    private void PositionTopCenter()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Top + 16;
    }

    /// <summary>
    /// Returns true if the given position is at least partially visible on any monitor.
    /// </summary>
    internal static bool IsOnScreen(double left, double top)
    {
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var bounds = screen.WorkingArea;
            // Check if point is within the screen bounds (with some margin)
            if (left >= bounds.Left - 100 && left < bounds.Right &&
                top >= bounds.Top - 50 && top < bounds.Bottom)
                return true;
        }
        return false;
    }
}
```

**Step 3: Verify it compiles**

Run: `dotnet build`
Expected: BUILD SUCCEEDED

Note: `IsOnScreen` uses `System.Windows.Forms.Screen`. The project already targets `net8.0-windows` and uses `UseWPF`. We need to check if `System.Windows.Forms` is available. If not, add `<UseWindowsForms>true</UseWindowsForms>` to the csproj. **Alternative without Forms dependency:** use Win32 `MonitorFromPoint` or simply check against `SystemParameters.VirtualScreenLeft/Top/Width/Height`:

```csharp
internal static bool IsOnScreen(double left, double top)
{
    return left >= SystemParameters.VirtualScreenLeft - 100
        && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
        && top >= SystemParameters.VirtualScreenTop - 50
        && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
}
```

Use this simpler version to avoid a WinForms dependency.

**Step 4: Commit**

```bash
git add src/PureType/StatusOverlayWindow.xaml src/PureType/StatusOverlayWindow.xaml.cs
git commit -m "feat: make overlay draggable with double-click reset and middle-click hide"
```

---

### Task 3: Wire overlay events into MainWindow

**Files:**
- Modify: `src/PureType/MainWindow.xaml.cs`

**Step 1: Create a helper method to set up overlay events**

Add a private method that wires `PositionChanged` and `HideRequested` and sets `RestoreLeft`/`RestoreTop`. This avoids duplicating event wiring in the two places where `_overlay` is created (line 203 and line 416).

```csharp
private StatusOverlayWindow CreateOverlay()
{
    var overlay = new StatusOverlayWindow
    {
        RestoreLeft = _settings.Window.OverlayLeft,
        RestoreTop = _settings.Window.OverlayTop,
    };

    overlay.PositionChanged += (left, top) =>
    {
        _settings = _settings with
        {
            Window = _settings.Window with { OverlayLeft = left, OverlayTop = top }
        };
        _settingsService.Save(_settings);
    };

    overlay.HideRequested += () =>
    {
        _settings = _settings with
        {
            Window = _settings.Window with { ShowOverlay = false }
        };
        _settingsService.Save(_settings);
        _overlay?.Close();
        _overlay = null;
    };

    return overlay;
}
```

**Step 2: Replace all `new StatusOverlayWindow()` calls with `CreateOverlay()`**

In `MainWindow.xaml.cs`, replace line 203:
```csharp
// Before:
_overlay = new StatusOverlayWindow();
// After:
_overlay = CreateOverlay();
```

Replace line 416:
```csharp
// Before:
_overlay = new StatusOverlayWindow();
// After:
_overlay = CreateOverlay();
```

**Step 3: Verify it compiles**

Run: `dotnet build`
Expected: BUILD SUCCEEDED

**Step 4: Run all tests**

Run: `dotnet test`
Expected: All tests pass.

**Step 5: Commit**

```bash
git add src/PureType/MainWindow.xaml.cs
git commit -m "feat: wire overlay drag/hide events into MainWindow with position persistence"
```

---

### Task 4: Add IsOnScreen unit tests

**Files:**
- Test: `tests/PureType.Tests/StatusOverlayWindowTests.cs` (new file)

**Step 1: Write tests**

Create `tests/PureType.Tests/StatusOverlayWindowTests.cs`:

```csharp
using PureType;

namespace PureType.Tests;

public class StatusOverlayWindowTests
{
    [Fact]
    public void IsOnScreen_center_of_virtual_screen_returns_true()
    {
        var left = System.Windows.SystemParameters.VirtualScreenLeft
                 + System.Windows.SystemParameters.VirtualScreenWidth / 2;
        var top = System.Windows.SystemParameters.VirtualScreenTop
                + System.Windows.SystemParameters.VirtualScreenHeight / 2;
        Assert.True(StatusOverlayWindow.IsOnScreen(left, top));
    }

    [Fact]
    public void IsOnScreen_far_offscreen_returns_false()
    {
        Assert.False(StatusOverlayWindow.IsOnScreen(-10000, -10000));
    }

    [Fact]
    public void IsOnScreen_slightly_left_of_screen_returns_true()
    {
        // Within the -100 margin
        var left = System.Windows.SystemParameters.VirtualScreenLeft - 50;
        var top = System.Windows.SystemParameters.VirtualScreenTop + 100;
        Assert.True(StatusOverlayWindow.IsOnScreen(left, top));
    }
}
```

**Step 2: Run tests to verify they pass**

Run: `dotnet test --filter "IsOnScreen"`
Expected: PASS

**Step 3: Commit**

```bash
git add tests/PureType.Tests/StatusOverlayWindowTests.cs
git commit -m "test: add IsOnScreen unit tests for overlay position validation"
```

---

### Task 5: Final verification

**Step 1: Run all tests**

Run: `dotnet test`
Expected: All tests pass.

**Step 2: Manual test**

Run the app. Connect. Verify:
- Overlay appears at top-center (default position)
- Left-click drag moves the overlay
- After restart, overlay appears at the saved position
- Double-click resets overlay to top-center
- Middle-click hides overlay; restart app and verify `showOverlay` is `false` in settings.json
- Re-enable in settings, drag to edge of screen, restart — overlay should appear at saved position
- Unplug second monitor (if applicable), restart — overlay should fall back to top-center if saved position is off-screen

**Step 3: Commit any fixes**

If any adjustments were needed, commit them.
