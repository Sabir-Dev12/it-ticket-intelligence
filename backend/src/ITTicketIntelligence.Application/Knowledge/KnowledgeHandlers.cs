using ITTicketIntelligence.Application.Abstractions.Persistence;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Domain.Knowledge;

namespace ITTicketIntelligence.Application.Knowledge;

public sealed class KnowledgeCommandHandler(IKnowledgeRepository repository, IKnowledgeDocumentProcessor processor,
    IUnitOfWork unitOfWork, TimeProvider clock) : ICommandHandler<CreateKnowledgeCommand, KnowledgeDocumentDto>,
    ICommandHandler<UpdateKnowledgeCommand, KnowledgeDocumentDto>, ICommandHandler<DeleteKnowledgeCommand, bool>
{
    public async Task<KnowledgeDocumentDto> HandleAsync(CreateKnowledgeCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        new KnowledgeValidation().Content(command.Title, command.Content, command.Source, command.Category, command.Kind, command.Format)
            .Require(command.CreatedById != Guid.Empty, "createdById", "L'auteur ne peut pas être un identifiant vide.").ThrowIfInvalid();
        if (command.CreatedById is { } author)
        {
            var active = await repository.IsAuthorActiveAsync(author, cancellationToken)
                ?? throw new ResourceNotFoundException("Auteur");
            if (!active) throw new ConflictException("L'auteur est désactivé.");
        }
        var now = clock.GetUtcNow();
        var document = new KnowledgeDocument(Guid.NewGuid(), command.Title, command.Content, command.Source, command.Category,
            now, command.CreatedById, command.Kind, command.Format);
        var prepared = await Prepare(document.Id, 1, command.Title, command.Content, command.Source, command.Category,
            command.Kind, command.Format, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Apply(document, prepared, now);
        repository.Add(document);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return document.ToDto();
    }

    public async Task<KnowledgeDocumentDto> HandleAsync(UpdateKnowledgeCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        new KnowledgeValidation().Content(command.Title, command.Content, command.Source, command.Category, command.Kind, command.Format).ThrowIfInvalid();
        var document = await Current(command.Id, command.Version, cancellationToken);
        if (document.Version == int.MaxValue) throw new ConflictException("La limite de version du document est atteinte.");
        // Prepare before mutating the tracked aggregate. A processing error leaves the old document intact.
        var prepared = await Prepare(document.Id, document.Version + 1, command.Title, command.Content, command.Source,
            command.Category, command.Kind, command.Format, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var now = clock.GetUtcNow();
        document.Update(command.Title, command.Content, command.Source, command.Category, now, command.Kind, command.Format);
        Apply(document, prepared, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return document.ToDto();
    }

    public async Task<bool> HandleAsync(DeleteKnowledgeCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = await Current(command.Id, command.Version, cancellationToken);
        repository.Remove(document);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<KnowledgeDocument> Current(Guid id, int version, CancellationToken token)
    {
        new KnowledgeValidation().Id(id).Version(version).ThrowIfInvalid();
        var document = await repository.GetByIdAsync(id, token) ?? throw new ResourceNotFoundException("Document");
        if (document.Version != version) throw new ConflictException("Le document a changé. Rechargez sa version courante.");
        return document;
    }
    private Task<PreparedKnowledgeDocument> Prepare(Guid id, int version, string title, string content, string source,
        Domain.Enums.TicketCategory category, Domain.Enums.KnowledgeDocumentKind kind, Domain.Enums.KnowledgeContentFormat format,
        CancellationToken token) => processor.PrepareAsync(new(id, version, title, content, source, category, kind, format), token);
    private static void Apply(KnowledgeDocument document, PreparedKnowledgeDocument prepared, DateTimeOffset now) =>
        document.ApplyPreparation(prepared.CleanedContent, prepared.ProcessingVersion,
            prepared.Chunks.Select(chunk => new KnowledgeChunk(Guid.NewGuid(), document.Id, document.Version,
                chunk.Metadata.Position, chunk.Content, chunk.Metadata.StartOffset, chunk.Metadata.EndOffset, chunk.Metadata.ContentHash, now)).ToList(), now);
}

public sealed class KnowledgeQueryHandler(IKnowledgeRepository repository, Retrieval.IKnowledgeAccessContext? access = null) : IQueryHandler<GetKnowledgeQuery, KnowledgeDocumentDto>,
    IQueryHandler<GetKnowledgePageQuery, KnowledgePage>
{
    public async Task<KnowledgeDocumentDto> HandleAsync(GetKnowledgeQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        new KnowledgeValidation().Id(query.Id).ThrowIfInvalid();
        var document = await repository.GetByIdAsync(query.Id, cancellationToken) ?? throw new ResourceNotFoundException("Document");
        if(access is not null && (!access.CanSearch || (document.IsRestricted && !access.IsAdmin && (!access.UserId.HasValue || document.CreatedById!=access.UserId))))throw new ResourceNotFoundException("Document");
        return document.ToDto();
    }
    public Task<KnowledgePage> HandleAsync(GetKnowledgePageQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        new KnowledgeValidation().Require(query.Page is > 0 and <= 1000000, "page", "Page entre 1 et 1000000.")
            .Require(query.PageSize is >= 1 and <= 100, "pageSize", "Taille entre 1 et 100.")
            .Require(query.Search is null || query.Search.Length <= 200, "search", "Recherche limitée à 200 caractères.")
            .Require(!query.Category.HasValue || Enum.IsDefined(query.Category.Value), "category", "Catégorie non reconnue.")
            .Require(!query.Kind.HasValue || Enum.IsDefined(query.Kind.Value), "kind", "Type non reconnu.").ThrowIfInvalid();
        return repository.GetPageAsync(query, cancellationToken);
    }
}
