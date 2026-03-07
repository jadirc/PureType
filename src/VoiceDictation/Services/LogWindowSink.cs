using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace VoiceDictation.Services;

public class LogWindowSink : ILogEventSink
{
    private readonly MessageTemplateTextFormatter _formatter =
        new("{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

    private Action<string>? _callback;

    public void SetCallback(Action<string> callback) => _callback = callback;

    public void Emit(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        _formatter.Format(logEvent, writer);
        _callback?.Invoke(writer.ToString().TrimEnd());
    }
}
