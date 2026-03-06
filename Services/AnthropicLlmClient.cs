using System.Net.Http;
using System.Text;
using System.Text.Json;
using Serilog;

namespace VoiceDictation.Services;

public class AnthropicLlmClient : ILlmClient
{
    private readonly HttpClient _http = new();
    private readonly string _model;

    public AnthropicLlmClient(string apiKey, string model)
    {
        _model = model;
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public async Task<string> ProcessAsync(string systemPrompt, string text, CancellationToken ct = default)
    {
        var body = new
        {
            model = _model,
            max_tokens = 4096,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = text }
            }
        };

        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("https://api.anthropic.com/v1/messages", content, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var result = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();

        Log.Debug("Anthropic LLM response: {Result}", result);
        return result ?? text;
    }
}
