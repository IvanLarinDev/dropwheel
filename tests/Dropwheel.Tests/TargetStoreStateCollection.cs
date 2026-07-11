namespace Dropwheel.Tests;

/// <summary>Test classes that mutate the global TargetStore state (Config and DirOverride) share this
/// collection so xUnit never runs them in parallel with each other — otherwise one class's DirOverride
/// reset races another's on-disk path assertions.</summary>
[CollectionDefinition("TargetStoreState")]
public sealed class TargetStoreStateCollection { }
