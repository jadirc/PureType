using System.Windows.Input;
using PureType.Helpers;

namespace PureType.Tests.Helpers;

public class UiHelperTests
{
    [Fact]
    public void FormatShortcut_single_modifier()
    {
        var result = UiHelper.FormatShortcut(ModifierKeys.Control, Key.X);
        Assert.Equal("Ctrl+X", result);
    }

    [Fact]
    public void FormatShortcut_multiple_modifiers()
    {
        var result = UiHelper.FormatShortcut(ModifierKeys.Control | ModifierKeys.Alt, Key.X);
        Assert.Equal("Ctrl+Alt+X", result);
    }

    [Fact]
    public void FormatShortcut_left_right_keys()
    {
        var result = UiHelper.FormatShortcut(ModifierKeys.Windows, Key.LeftCtrl);
        Assert.Equal("Win+L-Ctrl", result);
    }

    [Fact]
    public void ParseShortcut_roundtrip()
    {
        var original = (ModifierKeys.Control | ModifierKeys.Alt, Key.X);
        var formatted = UiHelper.FormatShortcut(original.Item1, original.Item2);
        var (mods, key) = UiHelper.ParseShortcut(formatted, Key.None);

        Assert.Equal(original.Item1, mods);
        Assert.Equal(original.Item2, key);
    }

    [Fact]
    public void ParseShortcut_uses_default_on_invalid()
    {
        var (mods, key) = UiHelper.ParseShortcut("InvalidGarbage", Key.Space);
        Assert.Equal(ModifierKeys.None, mods);
        Assert.Equal(Key.Space, key);
    }
}
