namespace KnowledgeBase.IntegrationTests.Support;

public sealed record SearchHitDto(
    Guid ChunkId, Guid DocumentId, string DocumentTitle, int Ordinal, string? Heading, int? Page, string Text,
    double Score, int? VectorRank, int? KeywordRank, double? Similarity);

public sealed record SearchDto(string Query, string Mode, string EmbeddingModel, List<SearchHitDto> Hits);

public static class Corpus
{
    public const string Returns = """
        # Returns policy

        Customers can send back any product within 14 days of delivery, unopened coffee included.

        ## Refunds

        Refunds are paid back to the original card within 3 working days of the parcel reaching our warehouse.

        ## Exchanges

        A grinder or kettle can be exchanged for another model once per order, free of charge.
        """;

    public const string Shipping = """
        # Shipping

        Orders placed before 14:00 leave the warehouse the same day.

        ## Carriers

        Parcels travel with Yurtiçi Kargo; the tracking code arrives by email.

        ## Costs

        Shipping is free above 750 TL; below that it costs 49.90 TL.
        """;

    public const string Brewing = """
        # Brewing guide

        ## V60

        Use 15 grams of coffee for 250 ml of water at 93 degrees, ground medium fine.

        ## French press

        Use a coarse grind and let it steep for four minutes before pressing.

        ## Product codes

        The Kenya AA beans are sold as KHV-004 and the Colombian beans as KHV-002.
        """;

    public static async Task<Dictionary<string, Guid>> AddAllAsync(Person person, Guid? workspaceId = null) => new()
    {
        ["returns"] = (await person.AddDocumentAsync("returns.md", Returns, workspaceId)).Id,
        ["shipping"] = (await person.AddDocumentAsync("shipping.md", Shipping, workspaceId)).Id,
        ["brewing"] = (await person.AddDocumentAsync("brewing.md", Brewing, workspaceId)).Id,
    };
}
