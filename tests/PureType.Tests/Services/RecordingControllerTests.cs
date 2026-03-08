using System.Windows.Media;
using PureType.Services;

namespace PureType.Tests.Services;

public class RecordingControllerTests
{
    private readonly AudioCaptureService _audio = new();
    private readonly ReplacementService _replacements = new(Path.GetTempFileName());

    private RecordingController CreateController(bool llmEnabled = false, bool clipboardMode = false)
    {
        var controller = new RecordingController(_audio, _replacements);
        controller.Configure(new AppSettings
        {
            Audio = new AudioSettings { Vad = false, ClipboardMode = clipboardMode },
            Llm = new LlmSettings { Enabled = llmEnabled },
        });
        return controller;
    }

    [Fact]
    public void IsRecording_initially_false()
    {
        var controller = CreateController();
        Assert.False(controller.IsRecording);
    }

    [Fact]
    public void IsMuted_can_be_set()
    {
        var controller = CreateController();
        controller.IsMuted = true;
        Assert.True(controller.IsMuted);
    }

    [Fact]
    public void HandleToggle_without_provider_does_not_start()
    {
        var controller = CreateController();
        controller.HandleToggle();
        Assert.False(controller.IsRecording);
    }

    [Fact]
    public void HandlePttDown_without_connection_does_not_start()
    {
        var controller = CreateController();
        controller.HandlePttDown();
        Assert.False(controller.IsRecording);
    }

    [Fact]
    public void TranscriptLog_initially_empty()
    {
        var controller = CreateController();
        Assert.Empty(controller.TranscriptLog);
    }

    [Fact]
    public void Configure_updates_settings()
    {
        var controller = CreateController();
        // Should not throw
        controller.Configure(new AppSettings
        {
            Audio = new AudioSettings { Vad = true },
            Llm = new LlmSettings { Enabled = true },
        });
    }

    [Fact]
    public void SetProvider_null_does_not_throw()
    {
        var controller = CreateController();
        controller.SetProvider(null, false);
        Assert.False(controller.IsRecording);
    }

    [Fact]
    public void StopRecording_when_not_recording_does_nothing()
    {
        var controller = CreateController();
        var stateChanged = false;
        controller.RecordingStateChanged += () => stateChanged = true;
        controller.StopRecording();
        Assert.False(stateChanged);
    }
}
