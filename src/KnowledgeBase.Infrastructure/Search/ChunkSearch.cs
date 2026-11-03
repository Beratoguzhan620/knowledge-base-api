using KnowledgeBase.Infrastructure.Embeddings;
using Npgsql;
using Pgvector;

namespace KnowledgeBase.Infrastructure.Search;

public enum SearchMode
{
    /// <summary>Meaning and words together, merged by reciprocal rank fusion.</summary>
    Hybrid,

    /// <summary>Only the nearest embeddings.</summary>
    Vector,

    /// <summary>Only PostgreSQL full-text search.</summary>
    Keyword,
}

/// <summary>A passage found for a question, with where it ranked in each kind of search.</summary>
public sealed record SearchHit(
    Guid ChunkId, Guid DocumentId, string DocumentTitle, int Ordinal, string? Heading, int? Page, string Text,
    double Score, int? VectorRank, int? KeywordRank, double? Similarity);

/// <summary>
/// Finds the passages of one workspace that best match a question. Two searches run in one SQL statement: the
/// nearest embeddings (pgvector, HNSW index, cosine distance) and PostgreSQL full-text search in each document's
/// language. Their rankings are merged with reciprocal rank fusion, so a passage that both find comes first, and
/// one that only matches the exact words (a product code, a name) is still found. Only documents that are
/// <c>Ready</c> are searched, and every query is bound to the workspace.
/// </summary>
public sealed class ChunkSearch(NpgsqlDataSource dataSource, IEmbeddingGenerator embeddings)
{
    /// <summary>The usual constant of reciprocal rank fusion: high enough that one list cannot drown the other.</summary>
    public const int FusionK = 60;

    /// <summary>How many candidates each search contributes before they are merged.</summary>
    public const int Candidates = 40;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(Guid workspaceId, string question, int limit, SearchMode mode, CancellationToken ct)
    {
        var embedding = mode == SearchMode.Keyword
            ? null
            : new Vector((await embeddings.EmbedAsync([question], EmbeddingPurpose.Query, ct))[0]);

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // Filtered vector search: keep scanning the HNSW index until enough rows of this workspace are found.
        await using (var settings = new NpgsqlCommand("SET LOCAL hnsw.ef_search = 100; SET LOCAL hnsw.iterative_scan = relaxed_order;", connection, transaction))
        {
            await settings.ExecuteNonQueryAsync(ct);
        }

        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        command.Parameters.AddWithValue("workspace", workspaceId);
        command.Parameters.AddWithValue("question", question);
        command.Parameters.AddWithValue("candidates", Candidates);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("k", FusionK);
        command.Parameters.AddWithValue("useVector", mode != SearchMode.Keyword);
        command.Parameters.AddWithValue("useKeyword", mode != SearchMode.Vector);
        // Vectors of another model live in another space; until those documents are indexed again only keywords find them.
        command.Parameters.AddWithValue("model", embeddings.Model);
        command.Parameters.Add(new NpgsqlParameter { ParameterName = "embedding", Value = (object?)embedding ?? DBNull.Value, DataTypeName = "vector" });

        var hits = new List<SearchHit>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            hits.Add(new SearchHit(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.GetString(6),
                reader.GetDouble(7),
                reader.IsDBNull(8) ? null : (int)reader.GetInt64(8),
                reader.IsDBNull(9) ? null : (int)reader.GetInt64(9),
                reader.IsDBNull(10) ? null : reader.GetDouble(10)));
        }

        return hits;
    }

    private const string Sql = """
        WITH vector_hits AS (
            SELECT c.id,
                   row_number() OVER (ORDER BY c.embedding <=> @embedding) AS rank,
                   1 - (c.embedding <=> @embedding) AS similarity
            FROM chunks c
            JOIN documents d ON d.id = c.document_id
            WHERE @useVector AND c.workspace_id = @workspace AND d.status = 'Ready' AND d.embedding_model = @model
              AND c.embedding IS NOT NULL
            ORDER BY c.embedding <=> @embedding
            LIMIT @candidates
        ),
        keyword_hits AS (
            SELECT c.id,
                   row_number() OVER (ORDER BY ts_rank_cd(c.search, q.query) DESC, c.id) AS rank
            FROM chunks c
            JOIN documents d ON d.id = c.document_id
            -- Any of the question's words, not all of them: a question is worded differently from the answer.
            -- Passages with more of the words, closer together, rank higher; quoted phrases stay phrases.
            CROSS JOIN LATERAL (SELECT replace((CASE c.language
                WHEN 'english' THEN websearch_to_tsquery('english'::regconfig, @question)
                WHEN 'turkish' THEN websearch_to_tsquery('turkish'::regconfig, @question)
                ELSE websearch_to_tsquery('simple'::regconfig, @question) END)::text, ' & ', ' | ')::tsquery AS query) q
            WHERE @useKeyword AND c.workspace_id = @workspace AND d.status = 'Ready' AND c.search @@ q.query
            ORDER BY ts_rank_cd(c.search, q.query) DESC, c.id
            LIMIT @candidates
        ),
        fused AS (
            SELECT coalesce(v.id, k.id) AS id,
                   coalesce(1.0 / (@k + v.rank), 0) + coalesce(1.0 / (@k + k.rank), 0) AS score,
                   v.rank AS vector_rank, k.rank AS keyword_rank, v.similarity
            FROM vector_hits v
            FULL JOIN keyword_hits k ON k.id = v.id
        )
        SELECT c.id, c.document_id, d.title, c.ordinal, c.heading, c.page, c.text,
               f.score::float8, f.vector_rank, f.keyword_rank, f.similarity::float8
        FROM fused f
        JOIN chunks c ON c.id = f.id
        JOIN documents d ON d.id = c.document_id
        ORDER BY f.score DESC, f.vector_rank NULLS LAST, c.id
        LIMIT @limit
        """;
}
