namespace ITTicketIntelligence.Application.Knowledge.Retrieval;
public sealed record KnowledgeIndexJob(Guid Id,Guid DocumentId,string Profile,string State,int Attempts);
public interface IKnowledgeIndexQueue
{
    Task<KnowledgeIndexJob> EnqueueAsync(Guid documentId,CancellationToken ct);
    Task<KnowledgeIndexJob?> GetAsync(Guid id,CancellationToken ct);
    Task<bool> ProcessNextAsync(CancellationToken ct);
}
public interface IKnowledgeAccessContext
{
    bool CanSearch { get; }
    bool IsAdmin { get; }
    Guid? UserId { get; }
}
public sealed class DenyKnowledgeAccess : IKnowledgeAccessContext
{ public bool CanSearch=>false;public bool IsAdmin=>false;public Guid? UserId=>null; }
public interface IKnowledgePermissions
{ Task SetRestrictedAsync(Guid documentId,bool restricted,Guid actor,CancellationToken ct); }
