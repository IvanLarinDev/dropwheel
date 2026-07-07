using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Verifies building a user preset from a rule (Save as preset).</summary>
public sealed class PresetServiceTests
{
    private static SortRule Rule(string dest, params RuleCondition[] conditions) =>
        new() { Dest = dest, All = conditions.ToList() };

    private static RuleCondition Ext(string value) =>
        new() { Field = ConditionField.Extension, Op = CompareOp.In, Value = value };

    [Fact]
    public void FromRule_captures_extensions_and_relative_dest()
    {
        var preset = PresetService.FromRule("My images", Rule("Images", Ext("png jpg")));
        Assert.NotNull(preset);
        Assert.Equal("My images", preset!.Name);
        Assert.Equal("Images", preset.Dest);
        Assert.Equal("png jpg", preset.Extensions);
    }

    [Fact]
    public void FromRule_stores_absolute_dest_as_leaf_folder()
    {
        var preset = PresetService.FromRule("Docs", Rule(@"C:\Users\me\Desktop\sorter\docs", Ext("pdf")));
        Assert.Equal("docs", preset!.Dest);
    }

    [Fact]
    public void FromRule_returns_null_without_extension_condition()
    {
        var rule = Rule("Big", new RuleCondition { Field = ConditionField.SizeMb, Op = CompareOp.Gt, Value = "10" });
        Assert.Null(PresetService.FromRule("Big", rule));
    }

    [Fact]
    public void FromRule_trims_name_and_extensions()
    {
        var preset = PresetService.FromRule("  Media  ", Rule("Media", Ext("  mp4 mkv  ")));
        Assert.Equal("Media", preset!.Name);
        Assert.Equal("mp4 mkv", preset.Extensions);
    }
}
