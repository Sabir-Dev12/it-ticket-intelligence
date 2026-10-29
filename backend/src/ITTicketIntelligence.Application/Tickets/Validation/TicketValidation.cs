using ITTicketIntelligence.Application.Common;

namespace ITTicketIntelligence.Application.Tickets.Validation;

internal sealed class TicketValidation
{
    private readonly Dictionary<string, string[]> errors = [];

    public TicketValidation Id(Guid id, string name)
    {
        if (id == Guid.Empty) errors[name] = ["L’identifiant ne peut pas être vide."];
        return this;
    }

    public TicketValidation OptionalId(Guid? id, string name)
    {
        if (id.HasValue) Id(id.Value, name);
        return this;
    }

    public TicketValidation Text(string? text, string name, int maximum)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > maximum || text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            errors[name] = [$"Le texte est obligatoire et limité à {maximum} caractères."];
        return this;
    }

    public TicketValidation OptionalText(string? text, string name, int maximum)
    {
        if (text?.Length > maximum) errors[name] = [$"Maximum {maximum} caractères."];
        return this;
    }

    public TicketValidation Enum<T>(T value, string name) where T : struct, Enum
    {
        if (!System.Enum.IsDefined(value)) errors[name] = ["Valeur non reconnue."];
        return this;
    }

    public TicketValidation OptionalEnum<T>(T? value, string name) where T : struct, Enum
    {
        if (value.HasValue) Enum(value.Value, name);
        return this;
    }

    public TicketValidation Require(bool condition, string name, string message)
    {
        if (!condition) errors[name] = [message];
        return this;
    }

    public TicketValidation Revision(int revision) => Require(revision > 0, "revision", "Une révision positive est requise.");

    public void ThrowIfInvalid()
    {
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }
}
