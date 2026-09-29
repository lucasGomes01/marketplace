namespace Users.Domain;

public sealed record Email
{
    public const int MaxLength = 254;

    public string Value { get; }

    private Email(string value) => Value = value;

    public static Email Create(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();

        if (!IsValid(normalized))
        {
            throw new DomainException($"'{value}' is not a valid email.", "email");
        }

        return new Email(normalized);
    }

    public override string ToString() => Value;

    private static bool IsValid(string email)
    {
        if (email.Length is 0 or > MaxLength || email.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var parts = email.Split('@');
        if (parts is not [var local, var domain] || local.Length == 0)
        {
            return false;
        }

        var labels = domain.Split('.');
        return labels.Length >= 2 && labels.All(label => label.Length > 0);
    }
}
