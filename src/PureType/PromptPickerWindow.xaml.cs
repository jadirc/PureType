using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using PureType.Services;

namespace PureType;

public partial class PromptPickerWindow : Window
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private readonly List<NamedPrompt> _allPrompts;

    /// <summary>The prompt the user selected, or null if cancelled.</summary>
    public NamedPrompt? SelectedPrompt { get; private set; }

    /// <summary>True if user held Shift when confirming (result goes to clipboard only).</summary>
    public bool ShiftHeld { get; private set; }

    public PromptPickerWindow(List<NamedPrompt> prompts)
    {
        InitializeComponent();
        _allPrompts = prompts;

        if (prompts.Count == 0)
        {
            EmptyMessage.Visibility = Visibility.Visible;
            PromptList.Visibility = Visibility.Collapsed;
        }
        else
        {
            PromptList.ItemsSource = prompts;
            PromptList.SelectedIndex = 0;
        }

        Loaded += (_, _) =>
        {
            PositionNearCursor();
            SearchBox.Focus();
        };

        Activated += (_, _) => SearchBox.Focus();
    }

    private void PositionNearCursor()
    {
        if (!GetCursorPos(out var pt)) return;

        var dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        var x = pt.X * dpiScale;
        var y = pt.Y * dpiScale;

        var screen = SystemParameters.WorkArea;
        if (x + Width > screen.Right) x = screen.Right - Width;
        if (y + Height > screen.Bottom) y = screen.Bottom - Height;
        if (x < screen.Left) x = screen.Left;
        if (y < screen.Top) y = screen.Top;

        Left = x;
        Top = y;
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allPrompts
            : _allPrompts.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        PromptList.ItemsSource = filtered;
        if (filtered.Count > 0)
            PromptList.SelectedIndex = 0;

        EmptyMessage.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyMessage.Text = _allPrompts.Count == 0 ? "No prompts configured" : "No matches";
        PromptList.Visibility = filtered.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (PromptList.Items.Count > 0)
                {
                    PromptList.SelectedIndex = Math.Min(PromptList.SelectedIndex + 1, PromptList.Items.Count - 1);
                    e.Handled = true;
                }
                break;

            case Key.Up:
                if (PromptList.Items.Count > 0)
                {
                    PromptList.SelectedIndex = Math.Max(PromptList.SelectedIndex - 1, 0);
                    e.Handled = true;
                }
                break;

            case Key.Enter:
                Confirm();
                e.Handled = true;
                break;

            case Key.Escape:
                DialogResult = false;
                Close();
                e.Handled = true;
                break;
        }
    }

    private void PromptList_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Let the click select the item, then refocus SearchBox so arrow keys keep working
        Dispatcher.InvokeAsync(() => SearchBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void PromptList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Confirm();
    }

    private void Confirm()
    {
        if (PromptList.SelectedItem is not NamedPrompt prompt)
            return;

        SelectedPrompt = prompt;
        ShiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        DialogResult = true;
        Close();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (IsVisible && DialogResult == null)
        {
            DialogResult = false;
            Close();
        }
    }
}
