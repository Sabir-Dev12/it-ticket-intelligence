namespace ITTicketIntelligence.Application.Knowledge.Retrieval;
// Reuses the established validation, profile selection and metadata verification pipeline.
public sealed class SemanticSearchService(IRagRetriever retrieval)
{
    public Task<RetrievedContext> SearchAsync(RagQuery query,CancellationToken ct=default)=>retrieval.RetrieveAsync(query,ct);
}
