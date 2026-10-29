using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Tickets.Commands;
using ITTicketIntelligence.Application.Tickets.DTOs;
using ITTicketIntelligence.Application.Tickets.Queries;
using ITTicketIntelligence.Domain.Auditing;
using ITTicketIntelligence.Domain.Common;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Tickets;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Tickets;

public class TicketManagementTests
{
    private static DateTimeOffset Now => new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_UsesServerReferenceAndClock_AndSavesItsAudit()
    {
        var test = new Scenario();
        using var cancellation = new CancellationTokenSource();

        var result = await test.Commands.HandleAsync(new CreateTicketCommand(" VPN unavailable ", " The VPN client cannot connect. ", test.UserId), cancellation.Token);

        Assert.Equal("IT-2000", result.Reference);
        Assert.Equal("VPN unavailable", result.Title);
        Assert.Equal(TicketStatus.Open, result.Status);
        Assert.Equal(Now, result.CreatedAt);
        Assert.Equal(1, result.Revision);
        Assert.Null(result.Category);
        Assert.Single(test.Repository.Tickets);
        var audit = Assert.Single(test.Audit.Entries);
        Assert.Equal("TicketCreated", audit.Action);
        Assert.Equal(test.UserId, audit.UserId);
        Assert.Equal(result.Id, audit.EntityId);
        Assert.Equal(1, test.Unit.Saves);
        Assert.Equal(cancellation.Token, test.Unit.LastToken);
        Assert.Equal(cancellation.Token, test.Repository.LastToken);
    }

    [Theory]
    [InlineData("", "Description")]
    [InlineData("Title", " ")]
    [InlineData(null, "Description")]
    public async Task Create_RejectsInvalidInputBeforePersistence(string? title, string description)
    {
        var test = new Scenario();
        await Assert.ThrowsAsync<RequestValidationException>(() => test.Commands.HandleAsync(new CreateTicketCommand(title!, description, test.UserId)));
        Assert.Equal(0, test.Repository.ReferenceRequests);
        Assert.Equal(0, test.Unit.Saves);
        Assert.Empty(test.Audit.Entries);
    }

    [Fact]
    public async Task Create_RejectsUnknownAndInactiveUsers()
    {
        var test = new Scenario();
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => test.Commands.HandleAsync(new CreateTicketCommand("Title", "Description", Guid.NewGuid())));
        test.References.Users[test.UserId] = new TicketUserReference(test.UserId, false);
        await Assert.ThrowsAsync<ConflictException>(() => test.Commands.HandleAsync(new CreateTicketCommand("Title", "Description", test.UserId)));
        Assert.Empty(test.Repository.Tickets);
    }

    [Fact]
    public async Task Update_ReplacesTheClassification_AndAdvancesTheRevisionOnce()
    {
        var test = new Scenario();
        var ticket = test.AddTicket();
        ticket.SetClassification(TicketCategory.Network, TicketPriority.High, Now.AddMinutes(-1));
        var revision = ticket.Revision;

        var result = await test.Commands.HandleAsync(new UpdateTicketCommand(ticket.Id, "Updated title", "Updated description", null, null, revision));

        Assert.Equal(revision + 1, result.Revision);
        Assert.Null(result.Category);
        Assert.Null(result.Priority);
        Assert.Equal("Updated title", result.Title);
        Assert.Equal("TicketUpdated", Assert.Single(test.Audit.Entries).Action);
    }

    [Fact]
    public async Task InvalidClassification_DoesNotPartiallyUpdateTheTicket()
    {
        var test = new Scenario();
        var ticket = test.AddTicket();
        var title = ticket.Title;

        await Assert.ThrowsAsync<RequestValidationException>(() => test.Commands.HandleAsync(new UpdateTicketCommand(ticket.Id,
            "Changed title", "Changed description", TicketCategory.Database, null, ticket.Revision)));
        await Assert.ThrowsAsync<RequestValidationException>(() => test.Commands.HandleAsync(new UpdateTicketCommand(ticket.Id,
            "Changed title", "Changed description", (TicketCategory)999, TicketPriority.Low, ticket.Revision)));

        Assert.Equal(title, ticket.Title);
        Assert.Equal(1, ticket.Revision);
        Assert.Equal(0, test.Unit.Saves);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("status")]
    [InlineData("assignment")]
    [InlineData("comment")]
    [InlineData("archive")]
    [InlineData("restore")]
    public async Task EveryMutation_RejectsAStaleRevision(string operation)
    {
        var test = new Scenario();
        var ticket = test.AddTicket();
        ticket.StartWork(Now.AddMinutes(-1));

        await Assert.ThrowsAsync<ConflictException>(() => Mutate(test, ticket.Id, operation, 1));

        Assert.Equal(0, test.Unit.Saves);
        Assert.Empty(test.Audit.Entries);
        Assert.Equal(2, ticket.Revision);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("status")]
    [InlineData("assignment")]
    [InlineData("comment")]
    [InlineData("archive")]
    [InlineData("restore")]
    public async Task EveryMutation_RequiresAPositiveRevision(string operation)
    {
        var test = new Scenario();
        var ticket = test.AddTicket();
        await Assert.ThrowsAsync<RequestValidationException>(() => Mutate(test, ticket.Id, operation, 0));
        Assert.Equal(0, test.Unit.Saves);
    }

    [Fact]
    public async Task StatusChanges_UseTheDomainLifecycleAndRequireAResolution()
    {
        var test = new Scenario();
        var ticket = test.AddTicket();
        await Assert.ThrowsAsync<DomainException>(() => test.Commands.HandleAsync(new ChangeTicketStatusCommand(ticket.Id, TicketStatus.Closed, null, ticket.Revision)));
        await test.Commands.HandleAsync(new ChangeTicketStatusCommand(ticket.Id, TicketStatus.InProgress, null, ticket.Revision));
        await Assert.ThrowsAsync<RequestValidationException>(() => test.Commands.HandleAsync(new ChangeTicketStatusCommand(ticket.Id, TicketStatus.Resolved, null, ticket.Revision)));
        await test.Commands.HandleAsync(new ChangeTicketStatusCommand(ticket.Id, TicketStatus.Resolved, "Endpoint restored.", ticket.Revision));
        await test.Commands.HandleAsync(new ChangeTicketStatusCommand(ticket.Id, TicketStatus.InProgress, null, ticket.Revision));

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Null(ticket.Resolution);
        Assert.Equal(3, test.Unit.Saves);
    }

    [Fact]
    public async Task Assignment_RejectsMissingInactiveOrEmptyTeams_AndAllowsUnassignment()
    {
        var test = new Scenario();
        var ticket = test.AddTicket();
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => test.Commands.HandleAsync(new AssignTicketTeamCommand(ticket.Id, Guid.NewGuid(), 1)));
        await Assert.ThrowsAsync<RequestValidationException>(() => test.Commands.HandleAsync(new AssignTicketTeamCommand(ticket.Id, Guid.Empty, 1)));
        var teamId = Guid.NewGuid();
        test.References.Teams[teamId] = new TicketTeamReference(teamId, false);
        await Assert.ThrowsAsync<ConflictException>(() => test.Commands.HandleAsync(new AssignTicketTeamCommand(ticket.Id, teamId, 1)));
        test.References.Teams[teamId] = new TicketTeamReference(teamId, true);
        await test.Commands.HandleAsync(new AssignTicketTeamCommand(ticket.Id, teamId, ticket.Revision));
        Assert.Equal(teamId, ticket.AssignedTeamId);
        await test.Commands.HandleAsync(new AssignTicketTeamCommand(ticket.Id, null, ticket.Revision));
        Assert.Null(ticket.AssignedTeamId);
        Assert.Equal(2, test.Unit.Saves);
    }

    [Fact]
    public async Task Comment_ValidatesTheAuthor_AndReturnsTheNewTicketRevision()
    {
        var test = new Scenario();
        var ticket = test.AddTicket();
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => test.Commands.HandleAsync(new AddTicketCommentCommand(ticket.Id, Guid.NewGuid(), "Message", 1)));
        var result = await test.Commands.HandleAsync(new AddTicketCommentCommand(ticket.Id, test.UserId, " Investigation started. ", 1));
        Assert.Equal("Investigation started.", result.Comment.Message);
        Assert.Equal(2, result.TicketRevision);
        Assert.Single(ticket.Comments);
        Assert.Equal(test.UserId, Assert.Single(test.Audit.Entries).UserId);
    }

    [Fact]
    public async Task ArchiveAndRestore_PreserveHistory_AndDoNotDuplicateAuditsForNoOps()
    {
        var test = new Scenario();
        var ticket = test.AddTicket();
        ticket.AddComment(new TicketComment(Guid.NewGuid(), ticket.Id, test.UserId, "Keep this history.", Now.AddMinutes(-1)));
        await test.Commands.HandleAsync(new ArchiveTicketCommand(ticket.Id, ticket.Revision));
        var revision = ticket.Revision;
        await test.Commands.HandleAsync(new ArchiveTicketCommand(ticket.Id, revision));
        Assert.Equal(revision, ticket.Revision);
        Assert.Equal(1, test.Unit.Saves);
        Assert.True(ticket.IsArchived);
        await Assert.ThrowsAsync<DomainException>(() => test.Commands.HandleAsync(new UpdateTicketCommand(ticket.Id, "Title", "Description", null, null, ticket.Revision)));
        await test.Commands.HandleAsync(new RestoreTicketCommand(ticket.Id, ticket.Revision));
        await test.Commands.HandleAsync(new RestoreTicketCommand(ticket.Id, ticket.Revision));
        Assert.False(ticket.IsArchived);
        Assert.Single(ticket.Comments);
        Assert.Equal(2, test.Unit.Saves);
        Assert.Equal(2, test.Audit.Entries.Count);
    }

    [Fact]
    public async Task Queries_ReturnNotFoundForMissingTickets_AndRejectEmptyIdentifiers()
    {
        var test = new Scenario();
        var handler = new TicketQueryHandler(test.Repository);
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => handler.HandleAsync(new GetTicketQuery(Guid.NewGuid())));
        await Assert.ThrowsAsync<RequestValidationException>(() => handler.HandleAsync(new GetTicketQuery(Guid.Empty)));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1000001, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task List_RejectsUnboundedPagination(int page, int pageSize)
    {
        var test = new Scenario();
        await Assert.ThrowsAsync<RequestValidationException>(() => new TicketQueryHandler(test.Repository).HandleAsync(new GetTicketsQuery(page, pageSize)));
        Assert.Equal(0, test.Repository.PageRequests);
    }

    [Fact]
    public async Task List_RejectsInvalidFilters_AndForwardsValidQueryAndCancellation()
    {
        var test = new Scenario();
        var handler = new TicketQueryHandler(test.Repository);
        await Assert.ThrowsAsync<RequestValidationException>(() => handler.HandleAsync(new GetTicketsQuery(Search: new string('x', 201))));
        await Assert.ThrowsAsync<RequestValidationException>(() => handler.HandleAsync(new GetTicketsQuery(Status: (TicketStatus)999)));
        await Assert.ThrowsAsync<RequestValidationException>(() => handler.HandleAsync(new GetTicketsQuery(AssignedTeamId: Guid.Empty)));
        using var cancellation = new CancellationTokenSource();
        var query = new GetTicketsQuery(Search: "vpn", IncludeArchived: true);
        await handler.HandleAsync(query, cancellation.Token);
        Assert.Equal(query, test.Repository.LastQuery);
        Assert.Equal(cancellation.Token, test.Repository.LastToken);
    }

    [Fact]
    public async Task CancelledOperations_DoNotTouchPersistence()
    {
        var test = new Scenario();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => test.Commands.HandleAsync(new CreateTicketCommand("Title", "Description", test.UserId), cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => new TicketQueryHandler(test.Repository).HandleAsync(new GetTicketsQuery(), cancellation.Token));
        Assert.Equal(0, test.Unit.Saves);
        Assert.Equal(0, test.Repository.ReferenceRequests);
    }

    private static Task Mutate(Scenario test, Guid id, string operation, int revision) => operation switch
    {
        "update" => test.Commands.HandleAsync(new UpdateTicketCommand(id, "Title", "Description", null, null, revision)),
        "status" => test.Commands.HandleAsync(new ChangeTicketStatusCommand(id, TicketStatus.InProgress, null, revision)),
        "assignment" => test.Commands.HandleAsync(new AssignTicketTeamCommand(id, null, revision)),
        "comment" => test.Commands.HandleAsync(new AddTicketCommentCommand(id, test.UserId, "Message", revision)),
        "archive" => test.Commands.HandleAsync(new ArchiveTicketCommand(id, revision)),
        _ => test.Commands.HandleAsync(new RestoreTicketCommand(id, revision))
    };

    private sealed class Scenario
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public FakeTickets Repository { get; } = new();
        public FakeReferences References { get; } = new();
        public FakeAudit Audit { get; } = new();
        public FakeUnit Unit { get; } = new();
        public TicketCommandHandler Commands { get; }

        public Scenario()
        {
            References.Users[UserId] = new TicketUserReference(UserId, true);
            Commands = new TicketCommandHandler(Repository, References, Audit, Unit, new FixedTime(),
                new ITTicketIntelligence.Application.Realtime.TicketEventNotifier(new ITTicketIntelligence.Application.Realtime.NullTicketEventSink(),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<ITTicketIntelligence.Application.Realtime.TicketEventNotifier>.Instance));
        }

        public Ticket AddTicket()
        {
            var ticket = new Ticket(Guid.NewGuid(), "IT-1", "Original title", "Original description", UserId, Now.AddMinutes(-2));
            Repository.Add(ticket);
            return ticket;
        }
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeTickets : ITicketRepository
    {
        public Dictionary<Guid, Ticket> Tickets { get; } = [];
        public CancellationToken LastToken { get; private set; }
        public GetTicketsQuery? LastQuery { get; private set; }
        public int ReferenceRequests { get; private set; }
        public int PageRequests { get; private set; }
        public void Add(Ticket ticket) => Tickets.Add(ticket.Id, ticket);
        public Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            return Task.FromResult(Tickets.GetValueOrDefault(id));
        }
        public Task<string> NextReferenceAsync(CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            ReferenceRequests++;
            return Task.FromResult("IT-2000");
        }
        public Task<PagedResult<TicketSummaryDto>> GetPageAsync(GetTicketsQuery query, CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            LastQuery = query;
            PageRequests++;
            return Task.FromResult(new PagedResult<TicketSummaryDto>([], 0, query.Page, query.PageSize));
        }
    }

    private sealed class FakeReferences : ITicketReferenceData
    {
        public Dictionary<Guid, TicketUserReference> Users { get; } = [];
        public Dictionary<Guid, TicketTeamReference> Teams { get; } = [];
        public Task<TicketUserReference?> FindUserAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Users.GetValueOrDefault(id));
        public Task<TicketTeamReference?> FindTeamAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Teams.GetValueOrDefault(id));
    }

    private sealed class FakeAudit : IAuditLogWriter
    {
        public List<AuditLog> Entries { get; } = [];
        public void Add(AuditLog audit) => Entries.Add(audit);
    }

    private sealed class FakeUnit : IUnitOfWork
    {
        public int Saves { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            LastToken = cancellationToken;
            return Task.FromResult(1);
        }
    }
}
