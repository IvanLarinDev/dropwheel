namespace Dropwheel.Tests;

/// <summary>Real Windows integration tests share native shell and process-wide state, so they must
/// not run in parallel with other test collections.</summary>
[CollectionDefinition("WindowsIntegration", DisableParallelization = true)]
public sealed class WindowsIntegrationCollection { }
