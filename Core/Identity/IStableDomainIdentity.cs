namespace UIEngine.Core;

/// <summary>Supplies a stable semantic identity for a domain object.</summary>
public interface IStableDomainIdentity
{
    string? DomainIdentity { get; }
}
