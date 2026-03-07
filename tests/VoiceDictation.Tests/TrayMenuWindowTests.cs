namespace VoiceDictation.Tests;

public class TrayMenuWindowTests
{
    [Fact]
    public void ComputeState_not_connected()
    {
        var state = TrayMenuWindow.ComputeState(connected: false, recording: false, muted: false);
        Assert.Equal("Not connected", state.StatusText);
        Assert.Equal("RedBrush", state.StatusBrush);
        Assert.Equal("Connect", state.ConnectText);
        Assert.False(state.MuteVisible);
    }

    [Fact]
    public void ComputeState_connected_idle()
    {
        var state = TrayMenuWindow.ComputeState(connected: true, recording: false, muted: false);
        Assert.Equal("Connected", state.StatusText);
        Assert.Equal("GreenBrush", state.StatusBrush);
        Assert.Equal("Disconnect", state.ConnectText);
        Assert.False(state.MuteVisible);
    }

    [Fact]
    public void ComputeState_recording()
    {
        var state = TrayMenuWindow.ComputeState(connected: true, recording: true, muted: false);
        Assert.Equal("Recording", state.StatusText);
        Assert.Equal("RedBrush", state.StatusBrush);
        Assert.Equal("Disconnect", state.ConnectText);
        Assert.False(state.MuteVisible);
    }

    [Fact]
    public void ComputeState_muted_takes_priority()
    {
        var state = TrayMenuWindow.ComputeState(connected: true, recording: true, muted: true);
        Assert.Equal("Muted", state.StatusText);
        Assert.Equal("YellowBrush", state.StatusBrush);
        Assert.Equal("Disconnect", state.ConnectText);
        Assert.True(state.MuteVisible);
    }

    [Fact]
    public void ComputeState_muted_while_disconnected()
    {
        var state = TrayMenuWindow.ComputeState(connected: false, recording: false, muted: true);
        Assert.Equal("Muted", state.StatusText);
        Assert.Equal("YellowBrush", state.StatusBrush);
        Assert.Equal("Connect", state.ConnectText);
        Assert.True(state.MuteVisible);
    }
}
