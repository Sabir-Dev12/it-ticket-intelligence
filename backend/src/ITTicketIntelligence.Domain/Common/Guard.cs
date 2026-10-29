namespace ITTicketIntelligence.Domain.Common;

internal static class Guard
{
    public static Guid Id(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException($"{name} must not be an empty identifier.");
        }

        return value;
    }

    public static Guid? OptionalId(Guid? value, string name)
    {
        return value.HasValue ? Id(value.Value, name) : null;
    }

    public static string RequiredText(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{name} is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{name} must not exceed {maxLength} characters.");
        }

        return trimmed;
    }

    public static string? OptionalText(string? value, string name, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) ? null : RequiredText(value, name, maxLength);
    }

    public static T Defined<T>(T value, string name) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new DomainException($"{name} has an undefined value.");
        }

        return value;
    }

    public static DateTimeOffset Timestamp(DateTimeOffset value, string name)
    {
        if (value == default)
        {
            throw new DomainException($"{name} is required.");
        }

        return value.ToUniversalTime();
    }

    public static DateTimeOffset NotBefore(DateTimeOffset value, DateTimeOffset baseline, string name)
    {
        var timestamp = Timestamp(value, name);
        if (timestamp < baseline)
        {
            throw new DomainException($"{name} must not precede the last change.");
        }

        return timestamp;
    }
}
