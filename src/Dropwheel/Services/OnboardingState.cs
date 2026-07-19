using System.Runtime.ExceptionServices;
using Dropwheel.Models;

namespace Dropwheel.Services;

internal static class OnboardingState
{
    public static bool ShouldShow(AppConfig config, bool isSmokeProfile = false) =>
        !isSmokeProfile && config.OnboardingVersion < AppConfig.CurrentOnboardingVersion;

    public static void Complete(AppConfig config) =>
        config.OnboardingVersion = AppConfig.CurrentOnboardingVersion;
}

internal readonly record struct OnboardingOptions(
    bool EnableExplorerSendTo,
    bool StartWithWindows);

internal sealed class OnboardingSetup(
    AppConfig config,
    Action installExplorerSendTo,
    Action enableStartup,
    Action save,
    Action? rollbackExplorerSendTo = null,
    Action? rollbackStartup = null)
{
    public void DecideLater() => Finish(default);

    public void Finish(OnboardingOptions options)
    {
        var previousVersion = config.OnboardingVersion;
        var rollback = new Stack<Action>();
        try
        {
            if (options.EnableExplorerSendTo)
            {
                if (rollbackExplorerSendTo is not null) rollback.Push(rollbackExplorerSendTo);
                installExplorerSendTo();
            }
            if (options.StartWithWindows)
            {
                if (rollbackStartup is not null) rollback.Push(rollbackStartup);
                enableStartup();
            }
            OnboardingState.Complete(config);
            save();
        }
        catch (Exception failure)
        {
            config.OnboardingVersion = previousVersion;
            var rollbackFailures = new List<Exception>();
            while (rollback.TryPop(out var undo))
            {
                try { undo(); }
                catch (Exception rollbackFailure) { rollbackFailures.Add(rollbackFailure); }
            }
            if (rollbackFailures.Count > 0)
            {
                rollbackFailures.Insert(0, failure);
                throw new AggregateException(
                    "Onboarding setup failed and rollback was incomplete.",
                    rollbackFailures);
            }
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }
}
