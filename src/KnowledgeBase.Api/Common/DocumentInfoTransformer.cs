using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace KnowledgeBase.Api.Common;

/// <summary>The landing text of the API reference.</summary>
internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Knowledge Base",
            Version = "1.0",
            Description = """
                A team knowledge base: upload Markdown, text and PDF files to a workspace, search them, and ask
                questions that are answered from those files with numbered citations.

                **Getting started**

                1. Create an account at `POST /api/auth/register`; it comes with a workspace of your own.
                2. Paste the access token into *Authentication*.
                3. Upload a file to `POST /api/workspaces/{workspaceId}/documents`, then search or ask.

                Everything in a workspace is visible only to its members. Readers search and ask, editors also add
                and delete documents, owners also manage members.

                **Errors** follow RFC 9457 problem details with a stable `code`, for example `validation_failed`
                or `duplicate_document`. Every response carries an `X-Correlation-Id`.
                """,
            License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://github.com/Beratoguzhan620/knowledge-base-api/blob/main/LICENSE") },
        };
        return Task.CompletedTask;
    }
}
