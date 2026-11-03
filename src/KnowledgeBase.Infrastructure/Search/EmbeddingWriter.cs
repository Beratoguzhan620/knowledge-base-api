using Npgsql;
using Pgvector;

namespace KnowledgeBase.Infrastructure.Search;

/// <summary>Writes the embeddings of a document's chunks, in one round trip per batch.</summary>
public sealed class EmbeddingWriter(NpgsqlDataSource dataSource)
{
    public async Task WriteAsync(IReadOnlyList<(Guid ChunkId, float[] Embedding)> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return;
        }

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var batch = new NpgsqlBatch(connection);
        foreach (var (chunkId, embedding) in rows)
        {
            var command = new NpgsqlBatchCommand("UPDATE chunks SET embedding = $1 WHERE id = $2");
            command.Parameters.Add(new NpgsqlParameter { Value = new Vector(embedding), DataTypeName = "vector" });
            command.Parameters.Add(new NpgsqlParameter { Value = chunkId });
            batch.BatchCommands.Add(command);
        }

        await batch.ExecuteNonQueryAsync(ct);
    }
}
