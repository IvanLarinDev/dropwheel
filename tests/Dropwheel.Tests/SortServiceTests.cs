using System.IO;
using System.Diagnostics;
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

    private string MakeDir(string name, DateTime? created = null)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        if (created is { } c) Directory.SetCreationTime(path, c);
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
    public void Negated_condition_matches_when_the_underlying_test_fails()
    {
        var draft = MakeFile("draft_report.pdf");
        var final = MakeFile("report.pdf");
        var t = Sorter(_root, new SortRule
        {
            Dest = "Final",
            All = { new RuleCondition { Field = ConditionField.NameContains, Value = "draft", Negate = true } },
        });
        var plan = SortService.Plan(t, new[] { draft, final });
        Assert.Contains(final, plan[Path.Combine(_root, "Final")]); // no "draft" → negated condition holds
        Assert.Contains(draft, plan[_root]);                        // has "draft" → not caught
    }

    [Fact]
    public void Negated_extension_also_catches_a_file_with_no_extension()
    {
        var noext = MakeFile("README");
        var t = Sorter(_root, new SortRule
        {
            Dest = "Other",
            All = { new RuleCondition { Field = ConditionField.Extension, Op = CompareOp.In, Value = "jpg png", Negate = true } },
        });
        var plan = SortService.Plan(t, new[] { noext });
        Assert.Contains(noext, plan[Path.Combine(_root, "Other")]);
    }

    [Fact]
    public void Clone_copies_the_negate_flag()
    {
        var original = new RuleCondition { Field = ConditionField.NameContains, Value = "x", Negate = true };
        Assert.True(original.Clone().Negate);
    }

    [Fact]
    public void Disabled_rule_is_skipped_during_sorting()
    {
        var jpg = MakeFile("a.jpg");
        var t = Sorter(_root, new SortRule
        {
            Dest = "Images",
            Enabled = false,
            All = { new RuleCondition { Field = ConditionField.Extension, Op = CompareOp.In, Value = "jpg" } },
        });
        var plan = SortService.Plan(t, new[] { jpg });
        Assert.Contains(jpg, plan[_root]); // rule off → file falls through to the root
    }

    [Fact]
    public void SortRule_without_an_enabled_field_loads_as_enabled()
    {
        // A config written before this feature has no "Enabled" key; it must load on, not silently off.
        var rule = System.Text.Json.JsonSerializer.Deserialize<SortRule>("{\"Dest\":\"X\"}");
        Assert.True(rule!.Enabled);
    }

    [Fact]
    public void Rule_clone_copies_the_enabled_flag()
    {
        var r = new SortRule { Dest = "X", Enabled = false };
        Assert.False(r.Clone().Enabled);
    }

    [Fact]
    public void Rule_clone_is_an_independent_deep_copy()
    {
        // Duplicating a rule must not share condition objects with the original.
        var original = new SortRule
        {
            Dest = "A",
            All = { new RuleCondition { Field = ConditionField.NameContains, Value = "x" } },
        };
        var copy = original.Clone();
        copy.Dest = "B";
        copy.All[0].Value = "y";
        Assert.Equal("A", original.Dest);
        Assert.Equal("x", original.All[0].Value);
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
        // A broken rule must not crash planning and must not catch the file.
        var plan = SortService.Plan(t, new[] { img });
        Assert.Contains(img, plan[Path.Combine(_root, "Camera")]);
    }

    [Fact]
    public void Catastrophic_regex_times_out_and_later_rules_can_match()
    {
        var file = new string('a', 5000) + "!_marker.txt";
        var rules = new List<SortRule>
        {
            Rule("Bad", ConditionField.NameRegex, CompareOp.Matches, "^(a+)+$"),
            Rule("Fallback", ConditionField.NameContains, CompareOp.Contains, "marker"),
        };

        var sw = Stopwatch.StartNew();
        var idx = SortService.MatchedRuleIndex(rules, file);

        Assert.Equal(1, idx);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Slow_regex_rule_times_out_and_lets_file_fall_through()
    {
        var file = MakeFile(new string('a', 40) + "!.txt");
        var t = Sorter(_root,
            Rule("Slow", ConditionField.NameRegex, CompareOp.Matches, "^(a+)+$"),
            Rule("Text", ConditionField.Extension, CompareOp.In, "txt"));

        var plan = SortService.Plan(t, new[] { file });

        Assert.Contains(file, plan[Path.Combine(_root, "Text")]);
    }

    [Fact]
    public void MatchedRuleIndex_distinguishes_rules_with_the_same_destination()
    {
        // Two rules with the same Dest: the index must point at the rule that actually matched.
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
        // The rule matched on group ep, but the path has ${zzz}, which doesn't exist — file to root.
        var f = MakeFile("ep001_sq001.mov");
        var t = Sorter(_root, Rule("episodes\\${ep}\\${zzz}",
            ConditionField.NameRegex, CompareOp.Matches, @"(?<ep>ep\d+)"));
        var plan = SortService.Plan(t, new[] { f });
        Assert.Contains(f, plan[_root]);
    }

    [Fact]
    public void Optional_group_that_captures_nothing_sends_file_to_root()
    {
        // Group sh is optional and didn't capture — the path can't be built, file goes to root.
        var f = MakeFile("ep001_sq001.mov");
        var t = Sorter(_root, Rule("episodes\\${ep}\\${sh}",
            ConditionField.NameRegex, CompareOp.Matches, @"(?<ep>ep\d+)_sq\d+(?:_(?<sh>sh\d+))?"));
        var plan = SortService.Plan(t, new[] { f });
        Assert.Contains(f, plan[_root]);
    }

    [Fact]
    public void Dest_without_placeholders_is_unchanged_by_the_token_engine()
    {
        // Regression: a plain Dest with capture groups in the condition but no ${…} behaves as before.
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
        // The capture contains characters illegal in a folder name — they are stripped.
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

    [Fact]
    public void ExpandTemplate_fills_builtin_date_with_iso_default()
    {
        var rule = new SortRule { Dest = "${date}" };
        var result = SortService.ExpandTemplate(rule, "x.txt", new DateTime(2026, 7, 13, 9, 30, 0), out bool ok);
        Assert.True(ok);
        Assert.Equal("2026-07-13", result);
    }

    [Fact]
    public void ExpandTemplate_applies_a_custom_date_format()
    {
        var rule = new SortRule { Dest = "${date:dd-MM-yy}" };
        var result = SortService.ExpandTemplate(rule, "x.txt", new DateTime(2026, 7, 13), out bool ok);
        Assert.True(ok);
        Assert.Equal("13-07-26", result);
    }

    [Fact]
    public void ExpandTemplate_fills_year_and_month_components()
    {
        var rule = new SortRule { Dest = "${year}\\${month}" };
        var result = SortService.ExpandTemplate(rule, "x.txt", new DateTime(2026, 7, 13), out bool ok);
        Assert.True(ok);
        Assert.Equal(Path.Combine("2026", "07"), result);
    }

    [Fact]
    public void Builtin_token_shadows_a_same_named_regex_group()
    {
        // A group literally named "date" must not override the built-in date token.
        var rule = new SortRule
        {
            Dest = "${date}",
            All = { new RuleCondition { Field = ConditionField.NameRegex, Op = CompareOp.Matches, Value = @"(?<date>\d+)" } },
        };
        var result = SortService.ExpandTemplate(rule, "12345.txt", new DateTime(2026, 7, 13), out bool ok);
        Assert.True(ok);
        Assert.Equal("2026-07-13", result);
    }

    [Fact]
    public void ExpandTemplate_flags_an_invalid_date_format()
    {
        var rule = new SortRule { Dest = "${date:%}" };
        _ = SortService.ExpandTemplate(rule, "x.txt", new DateTime(2026, 7, 13), out bool ok);
        Assert.False(ok);
    }

    [Fact]
    public void Ext_token_uses_the_lowercased_file_extension()
    {
        var rule = new SortRule { Dest = "by-type\\${ext}" };
        var result = SortService.ExpandTemplate(rule, "photo.JPG", new DateTime(2026, 7, 13), out bool ok);
        Assert.True(ok);
        Assert.Equal(Path.Combine("by-type", "jpg"), result);
    }

    [Fact]
    public void File_date_tokens_route_by_the_files_own_last_write_date()
    {
        var f = MakeFile("clip.mov", written: new DateTime(2020, 3, 15, 10, 0, 0));
        var t = Sorter(_root, new SortRule { Dest = "${fyear}\\${fmonth}" }); // catch-all
        var plan = SortService.Plan(t, new[] { f });
        Assert.Contains(f, plan[Path.Combine(_root, "2020", "03")]);
    }

    [Fact]
    public void File_date_token_on_a_missing_file_falls_back_to_root()
    {
        var rule = new SortRule { Dest = "${fyear}" };
        _ = SortService.ExpandTemplate(rule, "no-such-file.txt", new DateTime(2026, 7, 13), out bool ok);
        Assert.False(ok);
    }

    [Fact]
    public void IsValidDateFormat_accepts_good_and_rejects_bad()
    {
        Assert.True(SortService.IsValidDateFormat("dd-MM-yy"));
        Assert.True(SortService.IsValidDateFormat(null));
        Assert.True(SortService.IsValidDateFormat(""));
        Assert.False(SortService.IsValidDateFormat("%"));
    }

    [Fact]
    public void ParseTokens_returns_name_and_optional_format()
    {
        var tokens = SortService.ParseTokens(@"${year}\${date:dd-MM-yy}");
        Assert.Equal(("year", (string?)null), tokens[0]);
        Assert.Equal(("date", "dd-MM-yy"), tokens[1]);
    }

    [Fact]
    public void Week_token_uses_two_digit_iso_week()
    {
        // 2021-01-04 is the Monday of ISO week 1 of 2021; 2021-12-31 falls in ISO week 52.
        var rule = new SortRule { Dest = "${year}\\W${week}" };
        Assert.Equal(Path.Combine("2021", "W01"),
            SortService.ExpandTemplate(rule, "x.txt", new DateTime(2021, 1, 4), out _));
        Assert.Equal(Path.Combine("2021", "W52"),
            SortService.ExpandTemplate(rule, "x.txt", new DateTime(2021, 12, 31), out _));
    }

    [Fact]
    public void Quarter_token_maps_month_to_Q1_through_Q4()
    {
        var rule = new SortRule { Dest = "${quarter}" };
        Assert.Equal("Q1", SortService.ExpandTemplate(rule, "x.txt", new DateTime(2026, 2, 1), out _));
        Assert.Equal("Q3", SortService.ExpandTemplate(rule, "x.txt", new DateTime(2026, 7, 13), out _));
        Assert.Equal("Q4", SortService.ExpandTemplate(rule, "x.txt", new DateTime(2026, 12, 31), out _));
    }

    [Fact]
    public void Initial_token_uppercases_the_first_letter_and_buckets_non_letters_under_hash()
    {
        var rule = new SortRule { Dest = "${initial}" };
        var now = new DateTime(2026, 7, 13);
        Assert.Equal("P", SortService.ExpandTemplate(rule, "photo.jpg", now, out _));
        Assert.Equal("X", SortService.ExpandTemplate(rule, "_xray.dat", now, out _));
        Assert.Equal("#", SortService.ExpandTemplate(rule, "123.txt", now, out _));
    }

    [Fact]
    public void Stem_token_uses_the_file_name_without_extension()
    {
        var rule = new SortRule { Dest = "versions\\${stem}" };
        var result = SortService.ExpandTemplate(rule, "report.final.pdf", new DateTime(2026, 7, 13), out bool ok);
        Assert.True(ok);
        Assert.Equal(Path.Combine("versions", "report.final"), result);
    }

    [Fact]
    public void File_quarter_token_routes_by_the_files_own_date()
    {
        var f = MakeFile("statement.pdf", written: new DateTime(2020, 11, 5));
        var t = Sorter(_root, new SortRule { Dest = "${fyear}\\${fquarter}" }); // catch-all
        var plan = SortService.Plan(t, new[] { f });
        Assert.Contains(f, plan[Path.Combine(_root, "2020", "Q4")]);
    }

    [Fact]
    public void TokenTakesFormat_for_date_tokens_and_size()
    {
        Assert.True(SortService.TokenTakesFormat("date"));
        Assert.True(SortService.TokenTakesFormat("fmonth"));
        Assert.True(SortService.TokenTakesFormat("cmonth"));
        Assert.True(SortService.TokenTakesFormat("size"));
        Assert.False(SortService.TokenTakesFormat("week"));
        Assert.False(SortService.TokenTakesFormat("quarter"));
        Assert.False(SortService.TokenTakesFormat("initial"));
        Assert.False(SortService.TokenTakesFormat("ext"));
    }

    [Fact]
    public void IsValidTokenFormat_checks_date_formats_and_size_specs()
    {
        Assert.True(SortService.IsValidTokenFormat("date", "dd-MM-yy"));
        Assert.False(SortService.IsValidTokenFormat("date", "%"));
        Assert.True(SortService.IsValidTokenFormat("size", null));           // bare ${size}
        Assert.True(SortService.IsValidTokenFormat("size", "tiny 1, huge"));
        Assert.False(SortService.IsValidTokenFormat("size", "tiny 10, small 5"));
        Assert.True(SortService.IsValidTokenFormat("ext", "whatever"));      // ignored
    }

    [Fact]
    public void Creation_date_tokens_route_by_the_files_creation_date()
    {
        var f = MakeFile("photo.jpg");
        File.SetCreationTime(f, new DateTime(2019, 4, 20, 8, 0, 0));
        var t = Sorter(_root, new SortRule { Dest = "${cyear}\\${cmonth}" }); // catch-all
        var plan = SortService.Plan(t, new[] { f });
        Assert.Contains(f, plan[Path.Combine(_root, "2019", "04")]);
    }

    [Fact]
    public void Creation_and_modified_date_tokens_are_independent()
    {
        // A file created in one month but last modified in another must split by the token used.
        var f = MakeFile("clip.mov", written: new DateTime(2022, 8, 9));
        File.SetCreationTime(f, new DateTime(2020, 1, 2));
        Assert.Equal("2020",
            SortService.ExpandTemplate(new SortRule { Dest = "${cyear}" }, f, out _));
        Assert.Equal("2022",
            SortService.ExpandTemplate(new SortRule { Dest = "${fyear}" }, f, out _));
    }

    [Fact]
    public void Creation_date_token_on_a_missing_file_falls_back_to_root()
    {
        _ = SortService.ExpandTemplate(new SortRule { Dest = "${cyear}" }, "no-such-file.txt",
            new DateTime(2026, 7, 13), out bool ok);
        Assert.False(ok);
    }

    [Fact]
    public void SizeBucketOf_uses_the_default_buckets_without_a_spec()
    {
        Assert.Equal("tiny", SortService.SizeBucketOf(0));
        Assert.Equal("tiny", SortService.SizeBucketOf(0.5));
        Assert.Equal("small", SortService.SizeBucketOf(1));
        Assert.Equal("small", SortService.SizeBucketOf(9.99));
        Assert.Equal("medium", SortService.SizeBucketOf(10));
        Assert.Equal("medium", SortService.SizeBucketOf(99));
        Assert.Equal("large", SortService.SizeBucketOf(100));
        Assert.Equal("large", SortService.SizeBucketOf(999));
        Assert.Equal("huge", SortService.SizeBucketOf(1000));
        Assert.Equal("huge", SortService.SizeBucketOf(5000));
    }

    [Fact]
    public void SizeBucketOf_honours_a_custom_spec()
    {
        const string spec = "tiny 0.5, small 10, medium 100, large 1000, huge";
        Assert.Equal("tiny", SortService.SizeBucketOf(0.2, spec));
        Assert.Equal("small", SortService.SizeBucketOf(0.5, spec));  // 0.5 is not below 0.5 → next bucket
        Assert.Equal("small", SortService.SizeBucketOf(9, spec));
        Assert.Equal("large", SortService.SizeBucketOf(500, spec));
        Assert.Equal("huge", SortService.SizeBucketOf(5000, spec));  // bound-less catch-all
    }

    [Fact]
    public void SizeBucketOf_returns_null_when_no_bucket_catches_the_size()
    {
        // No bound-less catch-all and the size exceeds every limit.
        Assert.Null(SortService.SizeBucketOf(50, "tiny 1, small 10"));
    }

    [Fact]
    public void ParseSizeSpec_rejects_malformed_specs()
    {
        Assert.NotNull(SortService.ParseSizeSpec("tiny 0.5, small 10, huge"));
        Assert.Null(SortService.ParseSizeSpec(""));                    // empty
        Assert.Null(SortService.ParseSizeSpec("tiny, small 10"));      // non-final bucket without a limit
        Assert.Null(SortService.ParseSizeSpec("tiny 10, small 5"));    // limits not ascending
        Assert.Null(SortService.ParseSizeSpec("tiny 0"));              // non-positive limit
        Assert.Null(SortService.ParseSizeSpec("tiny x, small 10"));    // non-numeric limit
        Assert.Null(SortService.ParseSizeSpec("tiny 1 2, small 10"));  // too many tokens in a bucket
    }

    [Fact]
    public void Size_token_routes_a_file_by_its_size_on_disk()
    {
        var tiny = MakeFile("icon.png", bytes: 40 * 1024);          // 40 KB → tiny
        var small = MakeFile("clip.mp4", bytes: 3L * 1024 * 1024);  // 3 MB → small
        var t = Sorter(_root, new SortRule { Dest = "by-size\\${size}" }); // catch-all
        var plan = SortService.Plan(t, new[] { tiny, small });
        Assert.Contains(tiny, plan[Path.Combine(_root, "by-size", "tiny")]);
        Assert.Contains(small, plan[Path.Combine(_root, "by-size", "small")]);
    }

    [Fact]
    public void Size_token_uses_a_custom_spec_from_the_destination()
    {
        var f = MakeFile("clip.mp4", bytes: 3L * 1024 * 1024);      // 3 MB
        var t = Sorter(_root, new SortRule { Dest = "${size: wee 1, big 100, huge}" });
        var plan = SortService.Plan(t, new[] { f });
        Assert.Contains(f, plan[Path.Combine(_root, "big")]);       // 1 <= 3 < 100 → big
    }

    [Fact]
    public void Size_token_on_a_missing_file_falls_back_to_root()
    {
        _ = SortService.ExpandTemplate(new SortRule { Dest = "${size}" }, "no-such-file.txt",
            new DateTime(2026, 7, 13), out bool ok);
        Assert.False(ok);
    }

    [Fact]
    public void Size_token_with_an_invalid_spec_falls_back_to_root()
    {
        var f = MakeFile("clip.mp4", bytes: 3L * 1024 * 1024);
        var t = Sorter(_root, new SortRule { Dest = "${size: tiny 10, small 5}" }); // descending → invalid
        var plan = SortService.Plan(t, new[] { f });
        Assert.Contains(f, plan[_root]);
    }

    [Fact]
    public void Size_output_folder_is_left_in_place_when_sorting_folders()
    {
        // A watched sorter routing by size must not re-file its own bucket folders.
        var bucket = MakeDir(Path.Combine("by-size", "small"));
        var t = Sorter(_root, new SortRule { Dest = "by-size\\${size}", Scope = RuleScope.Both });
        var moves = SortService.MovePlan(t, new[] { bucket });
        Assert.Empty(moves);
    }

    // ── Folder sorting ─────────────────────────────────────────────────────

    [Fact]
    public void Folder_date_tokens_route_by_the_folders_own_creation_date()
    {
        var dir = MakeDir("Vacation", created: new DateTime(2019, 4, 20));
        var t = Sorter(_root, new SortRule { Dest = "${cyear}\\${cmonth}", Scope = RuleScope.Both });
        var plan = SortService.Plan(t, new[] { dir });
        Assert.Contains(dir, plan[Path.Combine(_root, "2019", "04")]);
    }

    [Fact]
    public void Scope_files_leaves_folders_alone_and_scope_folders_leaves_files_alone()
    {
        var file = MakeFile("a.jpg");
        var dir = MakeDir("sub");

        var foldersOnly = Sorter(_root, new SortRule { Dest = "Archived", Scope = RuleScope.Folders });
        var p1 = SortService.Plan(foldersOnly, new[] { file, dir });
        Assert.Contains(dir, p1[Path.Combine(_root, "Archived")]);
        Assert.Contains(file, p1[_root]); // the file is not caught, stays at root

        var filesOnly = Sorter(_root, new SortRule { Dest = "Archived", Scope = RuleScope.Files });
        var p2 = SortService.Plan(filesOnly, new[] { file, dir });
        Assert.Contains(file, p2[Path.Combine(_root, "Archived")]);
        Assert.Contains(dir, p2[_root]); // the folder is not caught, stays at root
    }

    [Fact]
    public void Default_rule_scope_does_not_catch_folders()
    {
        var dir = MakeDir("stuff");
        var t = Sorter(_root, new SortRule { Dest = "X" }); // default Scope = Files, catch-all
        var plan = SortService.Plan(t, new[] { dir });
        Assert.Contains(dir, plan[_root]);
    }

    [Fact]
    public void A_folder_is_never_moved_into_its_own_subtree()
    {
        var dir = MakeDir("Photos");
        var t = Sorter(_root, new SortRule { Dest = "Photos", Scope = RuleScope.Both }); // dest == the folder itself
        var plan = SortService.MovePlan(t, new[] { dir });
        Assert.Empty(plan);
    }

    [Fact]
    public void A_folder_already_named_like_the_destination_shape_is_left_in_place()
    {
        // Regression against the watch loop: the sorter's own dated output folder must not be re-filed,
        // even though its creation date is "now" and would otherwise resolve elsewhere.
        var dated = MakeDir("2019_04_20");
        var t = Sorter(_root, new SortRule { Dest = "${cyear}_${cmonth}_${cday}", Scope = RuleScope.Both });
        var plan = SortService.MovePlan(t, new[] { dated });
        Assert.Empty(plan);
    }

    [Fact]
    public void A_normally_named_folder_is_not_mistaken_for_a_dated_folder()
    {
        var dir = MakeDir("Report", created: new DateTime(2021, 9, 9));
        var t = Sorter(_root, new SortRule { Dest = "${cyear}_${cmonth}_${cday}", Scope = RuleScope.Both });
        var plan = SortService.MovePlan(t, new[] { dir });
        Assert.Contains(Path.Combine(_root, "2021_09_09"), plan.Keys);
    }

    [Fact]
    public void ScopeIncludes_maps_scope_to_item_kind()
    {
        Assert.True(SortService.ScopeIncludes(RuleScope.Files, isDirectory: false));
        Assert.False(SortService.ScopeIncludes(RuleScope.Files, isDirectory: true));
        Assert.True(SortService.ScopeIncludes(RuleScope.Folders, isDirectory: true));
        Assert.False(SortService.ScopeIncludes(RuleScope.Folders, isDirectory: false));
        Assert.True(SortService.ScopeIncludes(RuleScope.Both, isDirectory: true));
        Assert.True(SortService.ScopeIncludes(RuleScope.Both, isDirectory: false));
    }
}
