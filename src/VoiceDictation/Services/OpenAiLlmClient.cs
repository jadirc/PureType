using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Serilog;

namespace VoiceDictation.Services;

public class OpenAiLlmClient : ILlmClient
{
    private readonly HttpClient _http = new();
    private readonly string _baseUrl;
    private readonly string _model;

    public OpenAiLlmClient(string apiKey, string baseUrl, string model)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> ProcessAsync(string systemPrompt, string text, CancellationToken ct = default)
    {
        var body = new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = text }
            },
            temperature = 0.3
        };

        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync($"{_baseUrl}/chat/completions", content, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var result = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        Log.Debug("OpenAI LLM response: {Result}", result);
        return result ?? text;
    }
}
