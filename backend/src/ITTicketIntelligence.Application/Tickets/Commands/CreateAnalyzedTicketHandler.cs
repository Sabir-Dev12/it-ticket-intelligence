using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Application.Intelligence;
using ITTicketIntelligence.Application.Tickets.DTOs;
using ITTicketIntelligence.Application.Tickets.Queries;
using Microsoft.Extensions.Logging;
namespace ITTicketIntelligence.Application.Tickets.Commands;
public sealed class CreateAnalyzedTicketHandler(ICommandHandler<CreateTicketCommand,TicketDto> create,ITicketIntelligencePipeline intelligence,IQueryHandler<GetTicketQuery,TicketDto> read,ILogger<CreateAnalyzedTicketHandler> logger):ICommandHandler<CreateTicketCommand,TicketDto>
{
    public async Task<TicketDto> HandleAsync(CreateTicketCommand command,CancellationToken cancellationToken=default)
    {
        var ticket=await create.HandleAsync(command,cancellationToken);
        try {await intelligence.AnalyzeAsync(ticket.Id,cancellationToken);return await read.HandleAsync(new(ticket.Id),cancellationToken);}
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){throw;}
        catch(Exception e)when(e is not OutOfMemoryException)
        {
            logger.LogWarning("Ticket {TicketId} saved; initial predictions unavailable ({ErrorType})",ticket.Id,e.GetType().Name);
            // Creation already committed. Never fabricate predictions or create a duplicate ticket on retry.
            return ticket;
        }
    }
}
