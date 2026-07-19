using System.IO;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

[Collection("TargetStoreState")]
public sealed class OnboardingStateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dw_onboarding_" + Guid.NewGuid().ToString("N"));

    public OnboardingStateTests()
    {
        Directory.CreateDirectory(_root);
        TargetStore.DirOverride = _root;
    }

    public void Dispose()
    {
        TargetStore.DirOverride = null;
        TempDir.Delete(_root);
    }

    [Fact]
    public void Existing_config_without_onboarding_marker_does_not_reopen_onboarding()
    {
        File.WriteAllText(TargetStore.FilePath, "{ \"Targets\": [] }");

        TargetStore.Load();

        Assert.False(OnboardingState.ShouldShow(TargetStore.Config));
    }

    [Fact]
    public void New_profile_requires_onboarding()
    {
        TargetStore.Load();

        Assert.True(OnboardingState.ShouldShow(TargetStore.Config));
    }

    [Fact]
    public void Smoke_profile_never_requires_onboarding()
    {
        var config = new AppConfig { OnboardingVersion = 0 };

        Assert.False(OnboardingState.ShouldShow(config, isSmokeProfile: true));
    }

    [Fact]
    public void Completing_onboarding_persists_current_version()
    {
        TargetStore.Load();

        OnboardingState.Complete(TargetStore.Config);
        TargetStore.Save();
        TargetStore.Load();

        Assert.False(OnboardingState.ShouldShow(TargetStore.Config));
        Assert.Equal(AppConfig.CurrentOnboardingVersion, TargetStore.Config.OnboardingVersion);
    }

    [Fact]
    public void Finish_applies_only_selected_integrations()
    {
        var applied = new List<string>();
        var setup = new OnboardingSetup(
            new AppConfig { OnboardingVersion = 0 },
            () => applied.Add("send-to"),
            () => applied.Add("startup"),
            () => { });

        setup.Finish(new OnboardingOptions(EnableExplorerSendTo: true, StartWithWindows: false));

        Assert.Equal(new[] { "send-to" }, applied);
    }

    [Fact]
    public void Finish_disables_integrations_cleared_from_reopened_onboarding()
    {
        var applied = new List<string>();
        var setup = new OnboardingSetup(
            new AppConfig { OnboardingVersion = AppConfig.CurrentOnboardingVersion },
            () => applied.Add("enable-send-to"),
            () => applied.Add("enable-startup"),
            () => applied.Add("save"),
            uninstallExplorerSendTo: () => applied.Add("disable-send-to"),
            disableStartup: () => applied.Add("disable-startup"),
            initialOptions: new OnboardingOptions(
                EnableExplorerSendTo: true,
                StartWithWindows: true));

        setup.Finish(new OnboardingOptions(
            EnableExplorerSendTo: false,
            StartWithWindows: false));

        Assert.Equal(new[] { "disable-send-to", "disable-startup", "save" }, applied);
    }

    [Fact]
    public void Finish_persists_completion_after_selected_integrations()
    {
        var config = new AppConfig { OnboardingVersion = 0 };
        int versionAtSave = -1;
        var setup = new OnboardingSetup(
            config,
            () => { },
            () => { },
            () => versionAtSave = config.OnboardingVersion);

        setup.Finish(default);

        Assert.Equal(AppConfig.CurrentOnboardingVersion, versionAtSave);
    }

    [Fact]
    public void Save_failure_keeps_onboarding_pending()
    {
        var config = new AppConfig { OnboardingVersion = 0 };
        var setup = new OnboardingSetup(
            config,
            () => { },
            () => { },
            () => throw new InvalidOperationException("disk unavailable"));

        Assert.Throws<InvalidOperationException>(() => setup.Finish(default));

        Assert.True(OnboardingState.ShouldShow(config));
    }

    [Fact]
    public void Post_persistence_notification_failure_does_not_turn_commit_into_failure()
    {
        TargetStore.Load();
        int successfulNotifications = 0;
        Action failingSubscriber = () => throw new InvalidOperationException("watcher refresh failed");
        Action successfulSubscriber = () => successfulNotifications++;
        TargetStore.Saved += failingSubscriber;
        TargetStore.Saved += successfulSubscriber;
        try
        {
            var setup = new OnboardingSetup(
                TargetStore.Config,
                () => { },
                () => { },
                TargetStore.Save);

            var failure = Record.Exception(() => setup.Finish(default));

            Assert.Null(failure);
            Assert.Equal(1, successfulNotifications);
        }
        finally
        {
            TargetStore.Saved -= failingSubscriber;
            TargetStore.Saved -= successfulSubscriber;
        }

        TargetStore.Load();
        Assert.False(OnboardingState.ShouldShow(TargetStore.Config));
    }

    [Theory]
    [InlineData("startup", "send-to,startup,undo-startup,undo-send-to")]
    [InlineData("save", "send-to,startup,save,undo-startup,undo-send-to")]
    public void Finish_failure_rolls_back_attempted_integrations_in_reverse_order(
        string failureStep,
        string expectedEffects)
    {
        var config = new AppConfig { OnboardingVersion = 0 };
        var effects = new List<string>();
        var setup = new OnboardingSetup(
            config,
            () => effects.Add("send-to"),
            () =>
            {
                effects.Add("startup");
                if (failureStep == "startup") throw new InvalidOperationException("startup failed");
            },
            () =>
            {
                effects.Add("save");
                if (failureStep == "save") throw new InvalidOperationException("save failed");
            },
            rollbackExplorerSendTo: () => effects.Add("undo-send-to"),
            rollbackStartup: () => effects.Add("undo-startup"));

        Assert.Throws<InvalidOperationException>(() => setup.Finish(
            new OnboardingOptions(EnableExplorerSendTo: true, StartWithWindows: true)));

        Assert.Equal(expectedEffects.Split(','), effects);
        Assert.True(OnboardingState.ShouldShow(config));
    }

    [Fact]
    public void Finish_failure_restores_integrations_disabled_from_reopened_onboarding()
    {
        var effects = new List<string>();
        var setup = new OnboardingSetup(
            new AppConfig { OnboardingVersion = AppConfig.CurrentOnboardingVersion },
            () => effects.Add("enable-send-to"),
            () => effects.Add("enable-startup"),
            () =>
            {
                effects.Add("save");
                throw new InvalidOperationException("save failed");
            },
            rollbackExplorerSendTo: () => effects.Add("restore-send-to"),
            rollbackStartup: () => effects.Add("restore-startup"),
            uninstallExplorerSendTo: () => effects.Add("disable-send-to"),
            disableStartup: () => effects.Add("disable-startup"),
            initialOptions: new OnboardingOptions(
                EnableExplorerSendTo: true,
                StartWithWindows: true));

        Assert.Throws<InvalidOperationException>(() => setup.Finish(new OnboardingOptions(
            EnableExplorerSendTo: false,
            StartWithWindows: false)));

        Assert.Equal(
            new[] { "disable-send-to", "disable-startup", "save", "restore-startup", "restore-send-to" },
            effects);
    }

    [Fact]
    public void Rollback_failures_do_not_mask_the_original_or_skip_remaining_rollbacks()
    {
        var config = new AppConfig { OnboardingVersion = 0 };
        var effects = new List<string>();
        var setup = new OnboardingSetup(
            config,
            () => effects.Add("send-to"),
            () => effects.Add("startup"),
            () =>
            {
                effects.Add("save");
                throw new InvalidOperationException("save failed");
            },
            rollbackExplorerSendTo: () =>
            {
                effects.Add("undo-send-to");
                throw new IOException("send-to rollback failed");
            },
            rollbackStartup: () =>
            {
                effects.Add("undo-startup");
                throw new UnauthorizedAccessException("startup rollback failed");
            });

        var failure = Assert.Throws<AggregateException>(() => setup.Finish(
            new OnboardingOptions(EnableExplorerSendTo: true, StartWithWindows: true)));

        Assert.Equal(
            new[] { "send-to", "startup", "save", "undo-startup", "undo-send-to" },
            effects);
        Assert.Collection(
            failure.InnerExceptions,
            original => Assert.Equal("save failed", original.Message),
            startup => Assert.Equal("startup rollback failed", startup.Message),
            sendTo => Assert.Equal("send-to rollback failed", sendTo.Message));
        Assert.True(OnboardingState.ShouldShow(config));
    }

    [Fact]
    public void Decide_later_completes_without_changing_system_integrations()
    {
        var config = new AppConfig { OnboardingVersion = 0 };
        var effects = new List<string>();
        var setup = new OnboardingSetup(
            config,
            () => effects.Add("send-to"),
            () => effects.Add("startup"),
            () => effects.Add("save"));

        setup.DecideLater();

        Assert.Equal(new[] { "save" }, effects);
        Assert.False(OnboardingState.ShouldShow(config));
    }

    [Fact]
    public void Decide_later_from_reopened_onboarding_preserves_current_integrations()
    {
        var effects = new List<string>();
        var setup = new OnboardingSetup(
            new AppConfig { OnboardingVersion = AppConfig.CurrentOnboardingVersion },
            () => effects.Add("enable-send-to"),
            () => effects.Add("enable-startup"),
            () => effects.Add("save"),
            uninstallExplorerSendTo: () => effects.Add("disable-send-to"),
            disableStartup: () => effects.Add("disable-startup"),
            initialOptions: new OnboardingOptions(
                EnableExplorerSendTo: true,
                StartWithWindows: true));

        setup.DecideLater();

        Assert.Equal(new[] { "save" }, effects);
    }
}
