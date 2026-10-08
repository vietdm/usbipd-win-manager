namespace UsbipdManager.Core.Models;

/// <summary>
/// A device the user switched ON. It is matched by port (<see cref="BusId"/>) AND identity (<see cref="DeviceKey"/>):
/// another device on the same port, or the same device on another port, is not managed.
/// </summary>
public sealed record ManagedDevice(string BusId, string DeviceKey, string Description, DateTimeOffset AddedAt);
