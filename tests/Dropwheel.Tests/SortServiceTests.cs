using System.IO;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Verifies the rich rules engine: routing by extension/size/age/name/regex,
/// first-match-wins, catch-all, and absolute destinations.</summary>
public sealed class SortServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dw_sort_" + Guid.NewGuid().ToString("N"));

    public SortServiceTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (DirectoryNotFoundException) { } }

    private string MakeFile(string name, long bytes = 0, DateTime? written = null)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new byte[bytes]);
        if (written is { } w) File.SetLastWriteTime(path, w);
        return path;
    }

    private static TargetItem Sorter(string root, params SortRule[] rules) =>
        new() { Path = root, Rules = rules.ToList() };

    private static SortRule Rule(string dest, ConditionField field, CompareOp op, string value) =>
        new() { Dest = dest, All = { new RuleCondition { Field = field, Op = op, Value = value } } };

    [Fact]
    public void Extension_routes_to_matching_folder()
    {
        var jpg = MakeFile("a.jpg");
        var pdf = MakeFile("b.pdf");
        var t = Sorter(_root,
            Rule("Images", ConditionField.Extension, CompareOp.In, "jpg png"),
            Rule("Docs", ConditionField.Extension, CompareOp.In, "pdf"));
        var plan = SortService.Plan(t, new[] { jpg, pdf });
        Assert.Contains(jpg, plan[Path.Combine(_root, "Images")]);
        Assert.Contains(pdf, plan[Path.Combine(_root, "Docs")]);
    }

    [Fact]
    public void Extension_value_tolerates_commas_and_leading_dots()
    {
        var png = MakeFile("a.png");
        var webp = MakeFile("b.webp");
        var t = Sorter(_root, Rule("Images", ConditionField.Extension, CompareOp.In, ".png, jpg, .webp"));
        var plan = SortService.Plan(t, new[] { png, webp });
        Assert.Contains(png, plan[Path.Combine(_root, "Images")]);
        Assert.Contains(webp, plan[Path.Combine(_root, "Images")]);
    }

    [Fact]
    public void SizeMb_greater_than_matches_large_file()
    {
        var big = MakeFile("big.bin", 12L * 1024 * 1024);
        var small = MakeFile("small.bin", 1L * 1024 * 1024);
        var t = Sorter(_root, Rule("Big", ConditionField.SizeMb, CompareOp.Gt, "10"));
        var plan = SortService.Plan(t, new[] { big, small });
        Assert.Contains(big, plan[Path.Combine(_root, "Big")]);
        Assert.Contains(small, plan[_root]);
    }

    [Fact]
    public void AgeDays_greater_than_matches_old_file()
    {
        var old = MakeFile("old.txt", written: DateTime.Now.AddDays(-40));
        var fresh = MakeFile("fresh.txt", written: DateTime.Now);
        var t = Sorter(_root, Rule("Archive", ConditionField.AgeDays, CompareOp.Gt, "30"));
        var plan = SortService.Plan(t, new[] { old, fresh });
        Assert.Contains(old, plan[Path.Combine(_root, "Archive")]);
        Assert.Contains(fresh, plan[_root]);
    }

    [Fact]
    public void NameContains_and_NameRegex_match()
    {
        var inv = MakeFile("invoice_2024.pdf");
        var img = MakeFile("IMG_0001.jpg");
        var t = Sorter(_root,
            Rule("Invoices", ConditionField.NameContains, CompareOp.Contains, "invoice"),
            Rule("Camera", ConditionField.NameRegex, CompareOp.Matches, "^IMG_\\d+"));
        var plan = SortService.Plan(t, new[] { inv, img });
        Assert.Contains(inv, plan[Path.Combine(_root, "Invoices")]);
        Assert.Contains(img, plan[Path.Combine(_root, "Camera")]);
    }

    [Fact]
    public void First_matching_rule_wins()
    {
        var f = MakeFile("a.jpg");
        var t = Sorter(_root,
            Rule("First", ConditionField.Extension, CompareOp.In, "jpg"),
            Rule("Second", ConditionField.Extension, CompareOp.In, "jpg"));
        var plan = SortService.Plan(t, new[] { f });
        Assert.Contains(f, plan[Path.Combine(_root, "First")]);
    }

    [Fact]
    public void Catch_all_rule_captures_unmatched()
    {
        var f = MakeFile("a.xyz");
        var t = Sorter(_root,
            Rule("Images", ConditionField.Extension, CompareOp.In, "jpg"),
            new SortRule { Dest = "Other" });
        var plan = SortService.Plan(t, new[] { f });
        Assert.Contains(f, plan[Path.Combine(_root, "Other")]);
    }

    [Fact]
    public void No_match_and_no_catch_all_goes_to_root()
    {
        var f = MakeFile("a.xyz");
        var t = Sorter(_root, Rule("Images", ConditionField.Extension, CompareOp.In, "jpg"));
        var plan = SortService.Plan(t, new[] { f });
        Assert.Contains(f, plan[_root]);
    }

    [Fact]
    public void File_without_extension_never_matches_extension_rule()
    {
        var f = MakeFile("README");
        var t = Sorter(_root, Rule("Images", ConditionField.Extension, CompareOp.In, "jpg png"));
        var plan = SortService.Plan(t, new[] { f });
        Assert.Contains(f, plan[_root]);
    }

    [Fact]
    public void Absolute_destination_is_used_verbatim()
    {
        var f = MakeFile("a.jpg");
        var abs = Path.Combine(Path.GetTempPath(), "dw_abs_" + Guid.NewGuid().ToString("N"));
        var t = Sorter(_root, Rule(abs, ConditionField.Extension, CompareOp.In, "jpg"));
        var plan = SortService.Plan(t, new[] { f });
        Assert.Contains(f, plan[abs]);
    }

    [Fact]
    public void Legacy_SortRules_still_work()
    {
        var jpg = MakeFile("a.jpg");
        var t = new TargetItem
        {
            Path = _root,
            SortRules = new() { ["jpg png"] = "Images", ["*"] = "Other" },
        };
        var plan = SortService.Plan(t, new[] { jpg });
        Assert.Contains(jpg, plan[Path.Combine(_root, "Images")]);
    }

    [Fact]
    public void Invalid_regex_rule_does_not_throw_and_lets_file_fall_through()
    {
        var img = MakeFile("IMG_0001.jpg");
        var t = Sorter(_root,
            Rule("Broken", ConditionField.NameRegex, CompareOp.Matches, "([unclosed"),
            Rule("Camera", ConditionField.NameRegex, CompareOp.Matches, "^IMG_"));
        // Битое правило не должно ронять планирование и не должно ловить файл.
        var plan = SortService.Plan(t, new[] { img });
        Assert.Contains(img, plan[Path.Combine(_root, "Camera")]);
    }

    [Fact]
    public void MatchedRuleIndex_distinguishes_rules_with_the_same_destination()
    {
        // Два правила с одинаковым Dest: индекс должен указывать на реально сработавшее правило.
        var rules = new List<SortRule>
        {
            Rule("Media", ConditionField.Extension, CompareOp.In, "jpg"),
            Rule("Media", ConditionField.Extension, CompareOp.In, "mp4"),
        };
        Assert.Equal(0, SortService.MatchedRuleIndex(rules, "a.jpg"));
        Assert.Equal(1, SortService.MatchedRuleIndex(rules, "b.mp4"));
        Assert.Equal(-1, SortService.MatchedRuleIndex(rules, "c.txt"));
    }

    // ── Token paths from NameRegex groups ──────────────────────────────────

    [Fact]
    public void Named_groups_build_a_nested_destination()
    {
        var f = MakeFile("ep001_sq001_sh001_playblast_v001.mov");
        var t = Sorter(_root, Rule("episodes\\${ep}\\${sq}\\${sh}",
            ConditionField.NameRegex, CompareOp.Matches, @"(?<ep>ep\d+)_(?<sq>sq\d+)_(?<sh>sh\d+)"));
        var plan = SortService.Plan(t, new[] { f });
        Assert.Contains(f, plan[Path.Combine(_root, "episodes", "ep001", "sq001", "sh001")]);
    }

    [Fact]
    public void Token_without_a_matching_group_sends_file_to_root()
    {
        // Правило совпало по группе ep, но в пути есть ${zzz}, которого нет — файл в корень.
        var f = MakeFile("ep001_sq001.mov");
        var t = Sorter(_root, Rule("episodes\\${ep}\\${zzz}",
            ConditionField.NameRegex, CompareOp.Matches, @"(?<ep>ep\d+)"));
        var plan = SortService.Plan(t, new[] { f });
        Assert.Contains(f, plan[_root]);
    }

    [Fact]
    public void Optional_group_that_captures_nothing_sends_file_to_root()
    {
        // Группа sh необязательна и не захватилась — путь не собрать, файл в корень.
        var f = MakeFile("ep001_sq001.mov");
        var t = Sorter(_root, Rule("episodes\\${ep}\\${sh}",
            ConditionField.NameRegex, CompareOp.Matches, @"(?<ep>ep\d+)_sq\d+(?:_(?<sh>sh\d+))?"));
        var plan = SortService.Plan(t, new[] { f });
        Assert.Contains(f, plan[_root]);
    }

    [Fact]
    public void Dest_without_placeholders_is_unchanged_by_the_token_engine()
    {
        // Регресс: обычный Dest со скобочными группами в условии, но без ${…}, ведёт себя как раньше.
        var f = MakeFile("ep001_x.mov");
        var t = Sorter(_root, Rule("Plain",
            ConditionField.NameRegex, CompareOp.Matches, @"(?<ep>ep\d+)"));
        var plan = SortService.Plan(t, new[] { f });
        Assert.Contains(f, plan[Path.Combine(_root, "Plain")]);
    }

    [Fact]
    public void ExpandTemplate_fills_groups_and_reports_success()
    {
        var rule = Rule("${ep}\\${sq}", ConditionField.NameRegex, CompareOp.Matches,
            @"(?<ep>ep\d+)_(?<sq>sq\d+)");
        var result = SortService.ExpandTemplate(rule, "ep001_sq002_x.mov", out bool ok);
        Assert.True(ok);
        Assert.Equal(Path.Combine("ep001", "sq002"), result);
    }

    [Fact]
    public void ExpandTemplate_strips_illegal_path_chars_from_a_captured_value()
    {
        // Захват содержит запрещённые в имени папки символы — они вычищаются.
        var rule = Rule("${x}", ConditionField.NameRegex, CompareOp.Matches, @"(?<x>.+)");
        var result = SortService.ExpandTemplate(rule, "a<b>c", out bool ok);
        Assert.True(ok);
        Assert.Equal("abc", result);
    }

    [Fact]
    public void ExpandTemplate_flags_a_token_it_cannot_fill()
    {
        var rule = Rule("${ep}\\${missing}", ConditionField.NameRegex, CompareOp.Matches, @"(?<ep>ep\d+)");
        _ = SortService.ExpandTemplate(rule, "ep001_x.mov", out bool ok);
        Assert.False(ok);
    }

    [Fact]
    public void AvailableTokens_lists_named_groups_and_TokensIn_lists_used_placeholders()
    {
        var rule = Rule("${ep}\\${sq}", ConditionField.NameRegex, CompareOp.Matches,
            @"(?<ep>ep\d+)_(?<sq>sq\d+)");
        Assert.Equal(new[] { "ep", "sq" }, SortService.AvailableTokens(rule).OrderBy(s => s));
        Assert.Equal(new[] { "ep", "sq" }, SortService.TokensIn(rule.Dest).OrderBy(s => s));
    }
}
