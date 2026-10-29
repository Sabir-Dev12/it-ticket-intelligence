using System.Text.RegularExpressions;
using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Domain.Auditing;
using ITTicketIntelligence.Domain.Conversations;
using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Domain.Knowledge;
using ITTicketIntelligence.Infrastructure.Persistence.Rag;
using ITTicketIntelligence.Infrastructure.Persistence.Agents;
using ITTicketIntelligence.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace ITTicketIntelligence.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    internal DbSet<Attachments.AttachmentRecord> TicketAttachments => Set<Attachments.AttachmentRecord>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();
    public DbSet<TicketPrediction> TicketPredictions => Set<TicketPrediction>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    internal DbSet<RagVectorRecord> RagVectors => Set<RagVectorRecord>();
    internal DbSet<AgentAnalysisRecord> AgentAnalyses => Set<AgentAnalysisRecord>();
    public DbSet<AIConversation> AIConversations => Set<AIConversation>();
    public DbSet<AIMessage> AIMessages => Set<AIMessage>();
    internal DbSet<Assistant.AssistantTurnRecord> AssistantTurns => Set<Assistant.AssistantTurnRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("app");
        modelBuilder.HasSequence<long>("ticket_reference_sequence", "app").StartsAt(1000);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareChanges()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is KnowledgeDocument && entry.State == EntityState.Modified &&
                !entry.Collection(nameof(KnowledgeDocument.Chunks)).IsLoaded)
                throw new InvalidOperationException("Update knowledge documents through a fully loaded chunk aggregate.");
            if (entry.Entity is KnowledgeChunk && entry.State == EntityState.Modified)
                throw new InvalidOperationException("Knowledge chunks must be replaced, not rewritten.");
            var immutable = entry.Entity is TicketComment or TicketPrediction or AIMessage or AuditLog or AgentAnalysisRecord;
            if (immutable && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Historical facts are append-only.");
            }

            if (entry.Entity is TeamMember && entry.State == EntityState.Modified)
            {
                throw new InvalidOperationException("A membership can be added or removed, but not rewritten.");
            }

            if (entry.State != EntityState.Added) continue;

            if (entry.Entity is AuditLog audit)
            {
                entry.Property("CreatedAt").CurrentValue = audit.OccurredAt;
            }

            if (immutable || entry.Entity is TeamMember or KnowledgeChunk)
            {
                entry.Property("UpdatedAt").CurrentValue = entry.Property("CreatedAt").CurrentValue;
            }

            if (entry.Entity is AIMessage message)
            {
                var parent = ChangeTracker.Entries<AIConversation>()
                    .SingleOrDefault(conversation => conversation.Entity.Id == message.ConversationId);
                if (parent is null || (parent.State != EntityState.Added && !parent.Collection(c => c.Messages).IsLoaded))
                {
                    throw new InvalidOperationException("Add messages through a fully loaded conversation aggregate.");
                }

                var position = parent.Entity.Messages.ToList().FindIndex(candidate => candidate.Id == message.Id) + 1;
                if (position == 0) throw new InvalidOperationException("The message is not part of its conversation.");
                entry.Property("Position").CurrentValue = position;
            }
        }
    }
}
