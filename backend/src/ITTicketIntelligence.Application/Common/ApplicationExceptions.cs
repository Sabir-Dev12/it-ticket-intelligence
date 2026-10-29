namespace ITTicketIntelligence.Application.Common;

public sealed class RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("La requête contient des valeurs invalides.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class ResourceNotFoundException(string resource) : Exception($"{resource} introuvable.");

public sealed class ConflictException(string message) : Exception(message);
