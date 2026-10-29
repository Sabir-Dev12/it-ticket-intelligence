using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Domain.Auditing;
using ITTicketIntelligence.Domain.Conversations;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Domain.Knowledge;
using ITTicketIntelligence.Domain.Tickets;
using ITTicketIntelligence.Domain.ValueObjects;
using ITTicketIntelligence.Infrastructure;
using ITTicketIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.Persistence;

[Collection("Postgres")]
public sealed class PersistenceTests(PostgresFixture fixture)
{
    private static DateTimeOffset Now => new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migration_CreatesTheExpectedTablesTimestampsAndConstraints()
    {
        await using var db = fixture.CreateContext();
        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Equal(11, applied.Count());
        Assert.EndsWith("PgVectorKnowledgeJobs", applied.Last());
        Assert.False(db.Database.HasPendingModelChanges());

        var tables = await db.Database.SqlQueryRaw<string>("SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'app' AND table_type = 'BASE TABLE' ORDER BY table_name").ToListAsync();
        Assert.Equal(new[] { "ai_conversations", "ai_messages", "assistant_turns", "audit_logs", "knowledge_chunks", "knowledge_documents", "knowledge_index_jobs", "rag_vectors", "team_members", "teams", "ticket_agent_analyses", "ticket_attachments", "ticket_comments", "ticket_predictions", "tickets", "users" }, tables);

        var timestamps = await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_schema = 'app' AND column_name IN ('created_at', 'updated_at') AND data_type = 'timestamp with time zone'").SingleAsync();
        Assert.Equal(30, timestamps);
        var foreignKeys = await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM information_schema.table_constraints WHERE table_schema = 'app' AND constraint_type = 'FOREIGN KEY'").SingleAsync();
        Assert.Equal(22, foreignKeys);
    }

    [Fact]
    public async Task FullGraph_RoundTripsThroughTheScopedUnitOfWorkAndRepositories()
    {
        using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        Assert.Same(db, unit);
        var ticketRepository = scope.ServiceProvider.GetRequiredService<ITicketRepository>();
        var conversationRepository = scope.ServiceProvider.GetRequiredService<IAIConversationRepository>();

        var user = NewUser();
        var team = new Team(Guid.NewGuid(), "Database " + Guid.NewGuid().ToString("N"), Now);
        team.AddMember(user.Id, Now);
        var ticket = NewTicket(user.Id);
        ticket.SetClassification(TicketCategory.Database, TicketPriority.High, Now);
        ticket.AssignTo(team.Id, Now);
        ticket.AddComment(new TicketComment(Guid.NewGuid(), ticket.Id, user.Id, "Investigation started.", Now));
        ticket.RecordPrediction(TicketPrediction.ForCategory(Guid.NewGuid(), ticket.Id, TicketCategory.Database, new PredictionConfidence(.91), "category-v1", ticket.Revision, Now));
        ticket.RecordPrediction(TicketPrediction.ForPriority(Guid.NewGuid(), ticket.Id, TicketPriority.High, new PredictionConfidence(.87), "priority-v1", ticket.Revision, Now));
        ticket.RecordPrediction(TicketPrediction.ForTeamRouting(Guid.NewGuid(), ticket.Id, team.Id, new PredictionConfidence(.92), "routing-v1", ticket.Revision, Now));
        var document = new KnowledgeDocument(Guid.NewGuid(), "SQL connections", "Verify the service and endpoint.", "knowledge/" + Guid.NewGuid() + ".md", TicketCategory.Database, Now, user.Id);
        var conversation = new AIConversation(Guid.NewGuid(), user.Id, Now, ticket.Id);
        var first = new AIMessage(Guid.NewGuid(), conversation.Id, AIMessageRole.User, "Investigate this ticket.", Now);
        var second = new AIMessage(Guid.NewGuid(), conversation.Id, AIMessageRole.Assistant, "A diagnostic is required.", Now);
        conversation.AddMessage(first);
        conversation.AddMessage(second);
        var audit = new AuditLog(Guid.NewGuid(), user.Id, "TicketCreated", "Ticket", ticket.Id, Now);

        db.Users.Add(user);
        db.Teams.Add(team);
        ticketRepository.Add(ticket);
        db.KnowledgeDocuments.Add(document);
        conversationRepository.Add(conversation);
        db.AuditLogs.Add(audit);
        await unit.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await ticketRepository.GetByIdAsync(ticket.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ticket.Reference, loaded.Reference);
        Assert.Equal(team.Id, loaded.AssignedTeamId);
        Assert.Single(loaded.Comments);
        Assert.Equal(3, loaded.Predictions.Count);
        Assert.Equal(.91, loaded.Predictions.Single(p => p.Type == PredictionType.Category).Confidence.Value);
        var loadedTeam = await db.Teams.Include(t => t.Members).SingleAsync(t => t.Id == team.Id);
        Assert.Contains(user.Id, loadedTeam.MemberIds);
        Assert.Equal(user.Email, (await db.Users.SingleAsync(u => u.Id == user.Id)).Email);
        Assert.Equal(document.Content, (await db.KnowledgeDocuments.SingleAsync(d => d.Id == document.Id)).Content);
        var loadedConversation = await conversationRepository.GetByIdAsync(conversation.Id);
        Assert.NotNull(loadedConversation);
        Assert.Equal(new[] { first.Id, second.Id }, loadedConversation.Messages.Select(m => m.Id));
        Assert.Equal(ticket.Id, loadedConversation.TicketId);
        Assert.Equal(Now, await db.AuditLogs.Where(a => a.Id == audit.Id).Select(a => EF.Property<DateTimeOffset>(a, "CreatedAt")).SingleAsync());
        Assert.Equal(Now, await db.TicketPredictions.Where(p => p.TicketId == ticket.Id).Select(p => EF.Property<DateTimeOffset>(p, "UpdatedAt")).FirstAsync());
    }

    [Fact]
    public async Task TeamMembership_CanBeAddedAndRemovedAfterReloading()
    {
        await using var db = fixture.CreateContext();
        var user = NewUser();
        var team = new Team(Guid.NewGuid(), "Team " + Guid.NewGuid().ToString("N"), Now);
        db.Users.Add(user);
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loaded = await db.Teams.Include(t => t.Members).SingleAsync(t => t.Id == team.Id);
        loaded.AddMember(user.Id, Now.AddMinutes(1));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        loaded = await db.Teams.Include(t => t.Members).SingleAsync(t => t.Id == team.Id);
        Assert.Contains(user.Id, loaded.MemberIds);
        loaded.RemoveMember(user.Id, Now.AddMinutes(2));
        await db.SaveChangesAsync();
        Assert.False(await db.TeamMembers.AnyAsync(member => member.TeamId == team.Id));
    }

    [Fact]
    public async Task Conversation_PreservesInsertionOrderForEqualTimestampsAfterAppending()
    {
        using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IAIConversationRepository>();
        var user = NewUser();
        var conversation = new AIConversation(Guid.NewGuid(), user.Id, Now);
        var first = new AIMessage(Guid.NewGuid(), conversation.Id, AIMessageRole.User, "First", Now);
        conversation.AddMessage(first);
        db.Users.Add(user);
        repository.Add(conversation);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await repository.GetByIdAsync(conversation.Id);
        Assert.NotNull(loaded);
        var second = new AIMessage(Guid.NewGuid(), conversation.Id, AIMessageRole.Assistant, "Second", Now);
        loaded.AddMessage(second);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        loaded = await repository.GetByIdAsync(conversation.Id);
        Assert.NotNull(loaded);
        Assert.Equal(new[] { first.Id, second.Id }, loaded.Messages.Select(message => message.Id));
    }

    [Fact]
    public async Task UniqueNormalizedEmailAndTeamName_AreEnforcedByPostgres()
    {
        var user = NewUser();
        await using (var db = fixture.CreateContext())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.CreateContext())
        {
            db.Users.Add(new User(Guid.NewGuid(), "Duplicate", EmailAddress.Create(user.Email.Value.ToUpperInvariant()), UserRole.User, Now));
            await AssertDatabaseError(db, PostgresErrorCodes.UniqueViolation);
        }
        var name = "Team " + Guid.NewGuid().ToString("N");
        await using (var db = fixture.CreateContext())
        {
            db.Teams.Add(new Team(Guid.NewGuid(), name, Now));
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.CreateContext())
        {
            db.Teams.Add(new Team(Guid.NewGuid(), name.ToUpperInvariant(), Now));
            await AssertDatabaseError(db, PostgresErrorCodes.UniqueViolation);
        }
    }

    [Fact]
    public async Task ForeignKeys_RejectUnknownUsersAndDeletionOfReferencedUsers()
    {
        await using (var db = fixture.CreateContext())
        {
            db.Tickets.Add(NewTicket(Guid.NewGuid()));
            await AssertDatabaseError(db, PostgresErrorCodes.ForeignKeyViolation);
        }
        var (user, _) = await CreateTicket();
        await using (var db = fixture.CreateContext())
        {
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == user.Id));
            await AssertDatabaseError(db, PostgresErrorCodes.ForeignKeyViolation);
        }
    }

    [Fact]
    public async Task Deactivation_PersistsWithoutHidingHistoricalRelationships()
    {
        var (user, ticket) = await CreateTicket();
        await using var db = fixture.CreateContext();
        var loaded = await db.Users.SingleAsync(u => u.Id == user.Id);
        loaded.Deactivate(Now.AddMinutes(1));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.False((await db.Users.SingleAsync(u => u.Id == user.Id)).IsActive);
        Assert.True(await db.Tickets.AnyAsync(t => t.Id == ticket.Id && t.CreatedById == user.Id));
    }

    [Fact]
    public async Task ConcurrentTicketUpdates_RejectTheStaleWriter()
    {
        var (_, ticket) = await CreateTicket();
        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();
        var one = await first.Tickets.SingleAsync(t => t.Id == ticket.Id);
        var two = await second.Tickets.SingleAsync(t => t.Id == ticket.Id);
        one.SetClassification(TicketCategory.Database, TicketPriority.High, Now.AddMinutes(1));
        two.SetClassification(TicketCategory.Network, TicketPriority.Low, Now.AddMinutes(2));
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Theory]
    [InlineData("status = 'Unknown'")]
    [InlineData("category = 'Database', priority = NULL")]
    [InlineData("revision = 0")]
    [InlineData("status = 'Resolved'")]
    [InlineData("title = '   '")]
    [InlineData("updated_at = created_at - interval '1 second'")]
    public async Task TicketChecks_RejectInvalidSqlWrites(string assignment)
    {
        var (_, ticket) = await CreateTicket();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        // assignment comes exclusively from the fixed InlineData cases above.
        await using var command = new NpgsqlCommand($"UPDATE app.tickets SET {assignment} WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", ticket.Id);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Theory]
    [InlineData("confidence = -0.1")]
    [InlineData("confidence = 1.1")]
    [InlineData("confidence = 'NaN'::double precision")]
    [InlineData("type = 'Priority'")]
    public async Task PredictionChecks_RejectInvalidInserts(string assignment)
    {
        var (_, ticket) = await CreateTicket();
        await using var db = fixture.CreateContext();
        // INSERT through a SELECT allows testing the checks without updating an append-only row.
        var sql = "INSERT INTO app.ticket_predictions (id, ticket_id, type, confidence, model_version, input_revision, predicted_category, created_at, updated_at) " +
            "SELECT @id, @ticket, vals.type, vals.confidence, 'v1', 1, 'Database', @now, @now FROM " +
            (assignment == "type = 'Priority'" ? "(SELECT 'Priority'::text AS type, 0.8::double precision AS confidence) vals" :
            $"(SELECT 'Category'::text AS type, {assignment["confidence = ".Length..]} AS confidence) vals");
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("ticket", ticket.Id);
        command.Parameters.AddWithValue("now", Now);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task Audit_IsAppendOnlyThroughEfAndDirectSql()
    {
        var (_, ticket) = await CreateTicket();
        var audit = new AuditLog(Guid.NewGuid(), null, "Created", "Ticket", ticket.Id, Now);
        await using (var db = fixture.CreateContext())
        {
            db.AuditLogs.Add(audit);
            await db.SaveChangesAsync();
            db.Entry(audit).Property(a => a.Action).CurrentValue = "Rewritten";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            db.AuditLogs.Remove(await db.AuditLogs.SingleAsync(a => a.Id == audit.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        foreach (var sql in new[] { "UPDATE app.audit_logs SET action = 'Rewritten' WHERE id = @id", "DELETE FROM app.audit_logs WHERE id = @id" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", audit.Id);
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal("55000", error.SqlState);
        }
    }

    private ServiceProvider CreateProvider()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fixture.ConnectionString
        }).Build();
        return new ServiceCollection().AddInfrastructure(config).BuildServiceProvider(validateScopes: true);
    }

    private async Task<(User User, Ticket Ticket)> CreateTicket()
    {
        await using var db = fixture.CreateContext();
        var user = NewUser();
        var ticket = NewTicket(user.Id);
        db.Users.Add(user);
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return (user, ticket);
    }

    private static User NewUser() => new(Guid.NewGuid(), "Sara", EmailAddress.Create(Guid.NewGuid().ToString("N") + "@example.com"), UserRole.Agent, Now);
    private static Ticket NewTicket(Guid userId) => new(Guid.NewGuid(), "IT-" + Guid.NewGuid().ToString("N")[..16], "Database unavailable", "The application cannot connect to its database.", userId, Now);

    private static async Task AssertDatabaseError(AppDbContext db, string sqlState)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(sqlState, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }
}
