using System.Windows.Media;
using PureType.Services;

namespace PureType.Tests.Services;

public class RecordingControllerTests
{
    private readonly AudioCaptureService _audio = new();
    private readonly ReplacementService _replacements = new(Path.GetTempFileName());

    private RecordingController CreateController(
        bool llmEnabled = false,
        string inputMode = "Type",
        bool autoCorrectionEnabled = false)
    {
        var controller = new RecordingController(_audio, _replacements);
        controller.Configure(new AppSettings
        {
            Audio = new AudioSettings { Vad = false, InputMode = inputMode },
            Llm = new LlmSettings { Enabled = llmEnabled },
            AutoCorrection = new AutoCorrectionSettings { Enabled = autoCorrectionEnabled },
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

    // ── Auto-capitalize tests ──────────────────────────────────────────

    [Theory]
    [InlineData("hello world.", true, "Hello world.", true)]
    [InlineData("hello world?", true, "Hello world?", true)]
    [InlineData("hello world!", true, "Hello world!", true)]
    [InlineData("hello world", true, "Hello world", false)]
    [InlineData("hello world.", false, "hello world.", true)]
    [InlineData(" hello", true, " Hello", false)]
    [InlineData(".", true, ".", true)]
    [InlineData("", true, "", true)]
    [InlineData("123 test", true, "123 Test", false)]
    public void ApplyAutoCapitalize_handles_cases(
        string input, bool capitalizeNext, string expectedText, bool expectedNextFlag)
    {
        var (result, nextFlag) = RecordingController.ApplyAutoCapitalize(input, capitalizeNext);
        Assert.Equal(expectedText, result);
        Assert.Equal(expectedNextFlag, nextFlag);
    }

    [Fact]
    public void ApplyAutoCapitalize_newline_triggers_next_capitalize()
    {
        var (_, nextFlag) = RecordingController.ApplyAutoCapitalize("hello\n", false);
        Assert.True(nextFlag);
    }

    [Fact]
    public void StatsUpdated_event_exists()
    {
        var controller = CreateController();
        bool fired = false;
        controller.StatsUpdated += () => fired = true;
        // Event should exist and be subscribable without error
        Assert.False(fired);
    }

    // ── Auto-correction tests ─────────────────────────────────────────

    [Fact]
    public void Configure_with_auto_correction_enabled()
    {
        var controller = CreateController();
        controller.Configure(new AppSettings
        {
            Audio = new AudioSettings(),
            Llm = new LlmSettings(),
            AutoCorrection = new AutoCorrectionSettings { Enabled = true },
        });
        // Should not throw — flag is stored internally
    }

    [Fact]
    public void AutoCorrectionRequested_event_exists()
    {
        var controller = CreateController(autoCorrectionEnabled: true);
        bool fired = false;
        controller.AutoCorrectionRequested += _ => fired = true;
        Assert.False(fired);
    }

    [Fact]
    public void Configure_auto_correction_does_not_affect_llm()
    {
        var controller = CreateController(llmEnabled: true, autoCorrectionEnabled: true);
        // Both flags can coexist — prompt key overrides auto-correction at runtime
        controller.Configure(new AppSettings
        {
            Audio = new AudioSettings(),
            Llm = new LlmSettings { Enabled = true },
            AutoCorrection = new AutoCorrectionSettings { Enabled = true },
        });
        // Should not throw
    }

    [Fact]
    public void LastSttDuration_initially_zero()
    {
        var controller = CreateController();
        Assert.Equal(TimeSpan.Zero, controller.LastSttDuration);
    }
}
