using Microsoft.EntityFrameworkCore;
using TheDiscussion.Core.Discussions;

namespace TheDiscussion.Storage.Postgres;

/// <summary>EF Core model of the discussion schema. Any change requires a new migration (see README).</summary>
public sealed class DiscussionDbContext(DbContextOptions<DiscussionDbContext> options) : DbContext(options)
{
    internal DbSet<DiscussionEntity> Discussions => Set<DiscussionEntity>();

    internal DbSet<StatementEntity> Statements => Set<StatementEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DiscussionEntity>(discussion =>
        {
            discussion.ToTable("discussions");
            discussion.HasKey(d => d.Id);
            discussion.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();
            discussion.Property(d => d.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            discussion.Property(d => d.TopicTag).HasColumnName("topic_tag").IsRequired();
            discussion.Property(d => d.CreatedAt).HasColumnName("created_at");
            discussion.Property(d => d.Sequence).HasColumnName("sequence").UseIdentityAlwaysColumn();
            discussion.HasIndex(d => d.Sequence).IsUnique();
            discussion.HasMany(d => d.Statements)
                .WithOne()
                .HasForeignKey(s => s.DiscussionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StatementEntity>(statement =>
        {
            statement.ToTable("statements");
            statement.HasKey(s => s.Id);
            statement.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            statement.Property(s => s.DiscussionId).HasColumnName("discussion_id");
            statement.Property(s => s.ParentId).HasColumnName("parent_id");
            statement.Property(s => s.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(32);
            statement.Property(s => s.Text).HasColumnName("text").HasMaxLength(5000);
            statement.Property(s => s.GroupType).HasColumnName("group_type").HasConversion<string>().HasMaxLength(32);
            statement.Property(s => s.CreatedAt).HasColumnName("created_at");
            statement.Property(s => s.EditedAt).HasColumnName("edited_at");
            statement.Property(s => s.Sequence).HasColumnName("sequence").UseIdentityAlwaysColumn();

            // Deleting a statement removes everything beneath it, at any depth, within the same SQL statement (atomic).
            statement.HasOne<StatementEntity>()
                .WithMany()
                .HasForeignKey(s => s.ParentId)
                .OnDelete(DeleteBehavior.Cascade);

            statement.HasIndex(s => new { s.DiscussionId, s.Sequence });

            // Exactly one thesis per discussion.
            statement.HasIndex(s => s.DiscussionId)
                .IsUnique()
                .HasFilter("kind = 'Thesis'")
                .HasDatabaseName("ix_statements_one_thesis_per_discussion");
        });
    }
}
