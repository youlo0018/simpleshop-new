using ProductService.Application.Services;
using ProductService.Domain.Entities;
using Xunit;

namespace Collaboration.Domain.Tests;

public sealed class SearchSynonymExpanderTests
{
    [Fact]
    public void Equivalent_rule_expands_both_directions()
    {
        var variants = SearchSynonymExpander.Expand(
            "眼睫毛",
            [Rule("睫毛,眼睫毛", SearchSynonymRuleTypes.Equivalent)]);

        Assert.Equal(["眼睫毛", "睫毛"], variants.Select(a => a.Text));
        Assert.Equal(1.0, variants[0].Boost);
        Assert.Equal(0.9, variants[1].Boost);
    }

    [Fact]
    public void One_way_rule_does_not_expand_in_reverse()
    {
        var forward = SearchSynonymExpander.Expand(
            "苹果手机",
            [Rule("苹果手机,iPhone", SearchSynonymRuleTypes.OneWay)]);
        var reverse = SearchSynonymExpander.Expand(
            "iPhone",
            [Rule("苹果手机,iPhone", SearchSynonymRuleTypes.OneWay)]);

        Assert.Contains(forward, a => a.Text == "iPhone");
        Assert.DoesNotContain(reverse, a => a.Text == "苹果手机");
    }

    [Fact]
    public void Correction_rule_keeps_high_weight()
    {
        var variants = SearchSynonymExpander.Expand(
            "蓝球",
            [Rule("蓝球,篮球", SearchSynonymRuleTypes.Correction)]);

        Assert.Equal(1.0, variants.Single(a => a.Text == "篮球").Boost);
    }

    [Fact]
    public void Expansion_rule_uses_low_weight()
    {
        var variants = SearchSynonymExpander.Expand(
            "手机",
            [Rule("手机,数码", SearchSynonymRuleTypes.Expansion)]);

        Assert.Equal(0.35, variants.Single(a => a.Text == "数码").Boost);
    }

    [Fact]
    public void Scope_requires_matching_platform_category_or_brand()
    {
        var platform = Rule(
            "专属词,腕表", SearchSynonymRuleTypes.OneWay,
            SearchSynonymScopeTypes.Platform, 42);
        var category = Rule(
            "专属词,口红", SearchSynonymRuleTypes.OneWay,
            SearchSynonymScopeTypes.Category, 7);
        var brand = Rule(
            "专属词,香水", SearchSynonymRuleTypes.OneWay,
            SearchSynonymScopeTypes.Brand, 9);

        Assert.True(SearchSynonymExpander.IsApplicable(platform, 42, [], []));
        Assert.False(SearchSynonymExpander.IsApplicable(platform, 41, [], []));
        Assert.True(SearchSynonymExpander.IsApplicable(category, 1, [7], []));
        Assert.False(SearchSynonymExpander.IsApplicable(category, 1, [8], []));
        Assert.True(SearchSynonymExpander.IsApplicable(brand, 1, [], [9]));
        Assert.False(SearchSynonymExpander.IsApplicable(brand, 1, [], [10]));
    }

    [Fact]
    public void Disabled_rule_is_ignored()
    {
        var rule = Rule("测试词,腕表", SearchSynonymRuleTypes.OneWay);
        rule.Status = SearchSynonymStatuses.Disabled;

        var variants = SearchSynonymExpander.Expand("测试词", [rule]);

        Assert.Single(variants);
        Assert.Equal("测试词", variants[0].Text);
    }

    private static SearchSynonym Rule(
        string terms,
        int ruleType,
        int scopeType = SearchSynonymScopeTypes.Global,
        long scopeId = 0)
        => new()
        {
            Id = 1,
            Terms = terms,
            RuleType = ruleType,
            ScopeType = scopeType,
            ScopeId = scopeId,
            Priority = 100,
            Status = SearchSynonymStatuses.Enabled
        };
}
