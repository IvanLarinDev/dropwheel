using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Verifies SortRules → Rules migration preserves order, moves catch-all last,
/// stays equivalent to the legacy resolver, and is idempotent.</summary>
public sealed class SortMigrationTests
{
    [Fact]
    public void Preserves_order_and_moves_catch_all_last()
    {
        var legacy = new Dictionary<string, string>
        {
            ["jpg png"] = "Images",
            ["*"] = "Other",
            ["pdf"] = "Docs",
        };
        var rules = SortMigration.ToRules(legacy);
        Assert.Equal(3, rules.Count);
        Assert.Equal("Images", rules[0].Dest);
        Assert.Equal("Docs", rules[1].Dest);
        Assert.Equal("Other", rules[2].Dest);
        Assert.Empty(rules[2].All);
        Assert.Equal(ConditionField.Extension, rules[0].All[0].Field);
        Assert.Equal("jpg png", rules[0].All[0].Value);
    }

    [Fact]
    public void Migrate_fills_Rules_and_clears_SortRules()
    {
        var t = new TargetItem
        {
            Path = "C:\\x",
            SortRules = new() { ["jpg"] = "Images" },
        };
        Assert.True(SortMigration.Migrate(t));
        Assert.Null(t.SortRules);
        Assert.NotNull(t.Rules);
        Assert.Single(t.Rules!);
    }

    [Fact]
    public void Migrate_is_idempotent()
    {
        var t = new TargetItem { Path = "C:\\x", SortRules = new() { ["jpg"] = "Images" } };
        SortMigration.Migrate(t);
        Assert.False(SortMigration.Migrate(t));
    }

    [Fact]
    public void Migrated_rules_route_like_legacy()
    {
        var legacy = new TargetItem
        {
            Path = "C:\\x",
            SortRules = new() { ["jpg png"] = "Images", ["*"] = "Other" },
        };
        var migrated = new TargetItem { Path = "C:\\x", Rules = SortMigration.ToRules(legacy.SortRules!) };
        var files = new[] { "a.jpg", "b.xyz" };
        var legacyPlan = SortService.Plan(legacy, files);
        var newPlan = SortService.Plan(migrated, files);
        Assert.Equal(legacyPlan.Keys.OrderBy(k => k), newPlan.Keys.OrderBy(k => k));
    }
}
