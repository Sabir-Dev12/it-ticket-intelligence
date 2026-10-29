using System.Net.Mail;
using ITTicketIntelligence.Domain.Common;

namespace ITTicketIntelligence.Domain.ValueObjects;

public sealed record EmailAddress
{
    private EmailAddress(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static EmailAddress Create(string value)
    {
        var normalized = Guard.RequiredText(value, nameof(value), 254).ToLowerInvariant();
        if (!MailAddress.TryCreate(normalized, out var parsed) || parsed.Address != normalized)
        {
            throw new DomainException("A plain, valid email address is required.");
        }

        return new EmailAddress(normalized);
    }

    public override string ToString() => Value;
}
