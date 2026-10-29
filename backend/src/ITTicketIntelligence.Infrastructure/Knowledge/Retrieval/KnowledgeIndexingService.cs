using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace ITTicketIntelligence.Infrastructure.Knowledge.Retrieval;
public sealed class KnowledgeIndexingService(AppDbContext db,IEmbeddingService embeddings,IRagIndexer indexer,ILogger<KnowledgeIndexingService> logger):IKnowledgeIndexQueue,IKnowledgePermissions
{
    public async Task SetRestrictedAsync(Guid documentId,bool restricted,Guid actor,CancellationToken ct)
    {
        var document=await db.KnowledgeDocuments.SingleOrDefaultAsync(d=>d.Id==documentId,ct)??throw new ResourceNotFoundException("Document");
        document.SetRestricted(restricted,DateTimeOffset.UtcNow);
        db.AuditLogs.Add(new Domain.Auditing.AuditLog(Guid.NewGuid(),actor,"Knowledge.AccessChanged","KnowledgeDocument",documentId,DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct);
    }
    public async Task<KnowledgeIndexJob> EnqueueAsync(Guid documentId,CancellationToken ct)
    {
        if(!await db.KnowledgeDocuments.AnyAsync(d=>d.Id==documentId,ct))throw new ResourceNotFoundException("Document");
        var id=Guid.NewGuid();var profile=embeddings.Profile.Id;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.knowledge_index_jobs(id,document_id,profile,state,attempts) VALUES ({id},{documentId},{profile},'Queued',0)",ct);
        return new(id,documentId,profile,"Queued",0);
    }
    public async Task<KnowledgeIndexJob?> GetAsync(Guid id,CancellationToken ct)=>(await db.Database.SqlQuery<KnowledgeIndexJob>($"SELECT id AS \"Id\",document_id AS \"DocumentId\",profile AS \"Profile\",state AS \"State\",attempts AS \"Attempts\" FROM app.knowledge_index_jobs WHERE id={id}").ToListAsync(ct)).SingleOrDefault();
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("UPDATE app.knowledge_index_jobs SET state='Failed',lease_until=NULL WHERE state='Running' AND lease_until<now() AND attempts>=3",ct);
        var rows=await db.Database.SqlQueryRaw<KnowledgeIndexJob>("""
            UPDATE app.knowledge_index_jobs SET state='Running',attempts=attempts+1,lease_until=now()+interval '10 minutes'
            WHERE id=(SELECT id FROM app.knowledge_index_jobs WHERE (state='Queued' OR (state='Running' AND lease_until<now())) AND attempts<3 ORDER BY created_at FOR UPDATE SKIP LOCKED LIMIT 1)
            RETURNING id AS "Id",document_id AS "DocumentId",profile AS "Profile",state AS "State",attempts AS "Attempts"
            """).ToListAsync(ct);
        if(rows.Count==0)return false;var job=rows[0];var state="Succeeded";
        try { if(job.Profile!=embeddings.Profile.Id)state="RequiresReindex";else await indexer.IndexDocumentAsync(job.DocumentId,ct); }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(Exception error){state=job.Attempts<3?"Queued":"Failed";logger.LogWarning("Index job {JobId} failed with {ErrorType}",job.Id,error.GetType().Name);}
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.knowledge_index_jobs SET state={state},lease_until=NULL WHERE id={job.Id} AND attempts={job.Attempts}",ct);
        return true;
    }
}
