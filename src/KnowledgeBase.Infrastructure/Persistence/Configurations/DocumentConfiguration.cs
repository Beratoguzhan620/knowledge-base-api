using KnowledgeBase.Core.Documents;
using KnowledgeBase.Core.Users;
using KnowledgeBase.Core.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeBase.Infrastructure.Persistence.Configurations;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents", table =>
        {
            table.HasCheckConstraint("ck_documents_language", "language IN ('english', 'turkish', 'simple')");
            table.HasCheckConstraint("ck_documents_counts", "chunk_count > 0 AND size_bytes > 0 AND attempts >= 0");
        });
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Title).HasMaxLength(Document.TitleMaxLength);
        builder.Property(d => d.FileName).HasMaxLength(Document.FileNameMaxLength);
        builder.Property(d => d.Format).HasConversion<string>().HasMaxLength(10);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(d => d.Language).HasMaxLength(10);
        builder.Property(d => d.Error).HasMaxLength(Document.ErrorMaxLength);
        builder.Property(d => d.EmbeddingModel).HasMaxLength(100);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(d => d.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.UploadedBy).OnDelete(DeleteBehavior.Restrict);

        // The same file twice in one workspace is refused; in two workspaces it is fine.
        builder.HasIndex(d => new { d.WorkspaceId, d.Sha256 }).IsUnique();
        builder.HasIndex(d => new { d.WorkspaceId, d.CreatedAt });
        // What the indexer polls: queued documents whose time has come, and indexing ones whose lease ran out.
        builder.HasIndex(d => d.NextAttemptAt).HasFilter("status IN ('Queued', 'Indexing')");
    }
}

internal sealed class DocumentFileConfiguration : IEntityTypeConfiguration<DocumentFile>
{
    public void Configure(EntityTypeBuilder<DocumentFile> builder)
    {
        builder.ToTable("document_files");
        builder.HasKey(f => f.DocumentId);
        builder.HasOne<Document>().WithOne().HasForeignKey<DocumentFile>(f => f.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ChunkConfiguration : IEntityTypeConfiguration<Chunk>
{
    public void Configure(EntityTypeBuilder<Chunk> builder)
    {
        builder.ToTable("chunks");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Heading).HasMaxLength(Chunk.HeadingMaxLength);
        builder.Property(c => c.Language).HasMaxLength(10);
        builder.HasOne<Document>().WithMany().HasForeignKey(c => c.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.DocumentId, c.Ordinal }).IsUnique();
        builder.HasIndex(c => c.WorkspaceId);
    }
}
