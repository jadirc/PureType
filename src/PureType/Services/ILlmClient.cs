namespace PureType.Services;

public interface ILlmClient
{
    Task<string> ProcessAsync(string systemPrompt, string text, CancellationToken ct = default);
}
