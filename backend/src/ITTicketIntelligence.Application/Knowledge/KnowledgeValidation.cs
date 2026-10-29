using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Application.Knowledge;

internal sealed class KnowledgeValidation
{
    private readonly Dictionary<string, string[]> errors = [];
    public KnowledgeValidation Require(bool condition, string field, string message)
    { if (!condition) errors[field] = [message]; return this; }
    public KnowledgeValidation Id(Guid id) => Require(id != Guid.Empty, "id", "Un identifiant non vide est requis.");
    public KnowledgeValidation Version(int version) => Require(version > 0, "version", "Une version positive est requise.");
    public KnowledgeValidation Content(string? title, string? content, string? source, TicketCategory category,
        KnowledgeDocumentKind kind, KnowledgeContentFormat format) =>
        Require(!string.IsNullOrWhiteSpace(title) && title.Length <= 160 && !title.Contains('\0'), "title", "Titre obligatoire sans caractère NUL, maximum 160 caractères.")
        .Require(!string.IsNullOrWhiteSpace(content) && content.Length <= 100000 && !content.Contains('\0'), "content", "Contenu obligatoire sans caractère NUL, maximum 100000 caractères.")
        .Require(!string.IsNullOrWhiteSpace(source) && source.Length <= 512 && !source.Contains('\0'), "source", "Source obligatoire sans caractère NUL, maximum 512 caractères.")
        .Require(Enum.IsDefined(category), "category", "Catégorie non reconnue.")
        .Require(Enum.IsDefined(kind), "kind", "Type de document non reconnu.")
        .Require(Enum.IsDefined(format), "format", "Seuls PlainText et Markdown sont pris en charge.");
    public void ThrowIfInvalid() { if (errors.Count != 0) throw new RequestValidationException(errors); }
}
