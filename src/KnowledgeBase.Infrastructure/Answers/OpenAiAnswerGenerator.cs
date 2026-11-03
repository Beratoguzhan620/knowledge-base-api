using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeBase.Core;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Infrastructure.Answers;

/// <summary>
/// Streams <c>POST {BaseUrl}chat/completions</c> in the OpenAI shape, which Ollama, LM Studio and vLLM also speak,
/// and yields the text as it arrives.
/// </summary>
public sealed class OpenAiAnswerGenerator(HttpClient http, IOptions<ChatOptions> options) : IAnswerGenerator
{
    public string Model => options.Value.Model;

    public async IAsyncEnumerable<string> StreamAsync(AnswerRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var settings = options.Value;
        var body = new ChatRequest(
            settings.Model,
            PromptBuilder.Build(request).Select(m => new ChatMessage(m.Role, m.Content)).ToList(),
            settings.Temperature,
            settings.MaxTokens,
            Stream: true);

        using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions") { Content = JsonContent.Create(body) };
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or TimeoutException) && !ct.IsCancellationRequested)
        {
            throw new DomainException(ErrorCodes.ProviderUnavailable, $"The chat model at {http.BaseAddress} is not answering: {ex.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                throw new DomainException(ErrorCodes.ProviderUnavailable,
                    $"The chat model answered {(int)response.StatusCode}: {(detail.Length <= 200 ? detail : detail[..200] + "…")}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var data = line[5..].Trim();
                if (data == "[DONE]")
                {
                    yield break;
                }

                var chunk = JsonSerializer.Deserialize<ChatChunk>(data);
                var text = chunk?.Choices is [{ Delta.Content: { } content }, ..] ? content : null;
                if (!string.IsNullOrEmpty(text))
                {
                    yield return text;
                }
            }
        }
    }

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] List<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatChunk([property: JsonPropertyName("choices")] List<ChatChoice>? Choices);

    private sealed record ChatChoice([property: JsonPropertyName("delta")] ChatDelta Delta);

    private sealed record ChatDelta([property: JsonPropertyName("content")] string? Content);
}
