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
