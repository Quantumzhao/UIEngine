namespace UIEngine.Core;

/// <summary>Identifies a host-configured source of normalized change notifications.</summary>
public interface IObservationAdapter
{
    bool CanObserve(Type objectType);
}
