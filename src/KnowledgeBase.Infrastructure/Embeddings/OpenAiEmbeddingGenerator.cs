using System.Net.Http.Json;
using System.Text.Json.Serialization;
using KnowledgeBase.Core;
using Microsoft.Extensions.Options;

namespace KnowledgeBase.Infrastructure.Embeddings;

/// <summary>
/// Calls <c>POST {BaseUrl}embeddings</c> in the OpenAI shape, which Ollama, LM Studio and vLLM also speak.
/// A service that cannot be reached is reported as <see cref="ErrorCodes.ProviderUnavailable"/>; the indexer retries.
/// </summary>
public sealed class OpenAiEmbeddingGenerator(HttpClient http, IOptions<EmbeddingOptions> options) : IEmbeddingGenerator
{
    public string Model => options.Value.Model;

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, EmbeddingPurpose purpose, CancellationToken ct)
    {
        var settings = options.Value;
        var prefix = purpose == EmbeddingPurpose.Query ? settings.QueryPrefix : settings.DocumentPrefix;
        var request = new EmbeddingRequest(settings.Model, texts.Select(t => prefix + t).ToArray(),
            settings.RequestDimensions ? EmbeddingOptions.Dimensions : null);

        EmbeddingResponse? body;
        try
        {
            using var response = await http.PostAsJsonAsync("embeddings", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                throw new DomainException(ErrorCodes.ProviderUnavailable,
                    $"The embedding service answered {(int)response.StatusCode}: {Shorten(detail)}");
            }

            body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(ct);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or TimeoutException) && !ct.IsCancellationRequested)
        {
            throw new DomainException(ErrorCodes.ProviderUnavailable, $"The embedding service at {http.BaseAddress} is not answering: {ex.Message}");
        }

        var vectors = body?.Data?.OrderBy(d => d.Index).Select(d => d.Embedding).ToArray();
        if (vectors is null || vectors.Length != texts.Count)
        {
            throw new DomainException(ErrorCodes.ProviderUnavailable, "The embedding service gave back a different number of vectors than it was asked for.");
        }

        if (vectors.FirstOrDefault(v => v.Length != EmbeddingOptions.Dimensions) is { } wrong)
        {
            throw new InvalidOperationException(
                $"The model '{settings.Model}' gives vectors of {wrong.Length} numbers; the knowledge base stores {EmbeddingOptions.Dimensions}. " +
                "Choose a model of that size, or one that shortens its vectors on request (set Embeddings:RequestDimensions).");
        }

        return vectors;
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..200] + "…";

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string[] Input,
        [property: JsonPropertyName("dimensions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Dimensions);

    private sealed record EmbeddingResponse([property: JsonPropertyName("data")] List<EmbeddingItem>? Data);

    private sealed record EmbeddingItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
