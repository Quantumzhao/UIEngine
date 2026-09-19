namespace UIEngine.Core;

/// <summary>Identifies a domain object semantically across compatible replacement or restart.</summary>
public readonly record struct DomainIdentity
{
    public DomainIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
