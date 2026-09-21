namespace UIEngine.Core;

/// <summary>Identifies a domain object semantically across compatible replacement or restart.</summary>
public readonly record struct DomainIdentity
{
    public DomainIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A domain identity cannot be empty or whitespace.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
