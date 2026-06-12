using FluentAssertions;
using PlanningPoker.Core;
using Xunit;

namespace PlanningPoker.Core.Tests;

public class ReactionPolicyTests
{
    [Theory]
    [InlineData("👍")]
    [InlineData("🎉")]
    [InlineData("🚀")]
    public void Allowlisted_emoji_are_permitted(string emoji) =>
        ReactionPolicy.IsAllowed(emoji).Should().BeTrue();

    [Theory]
    [InlineData("💩")]          // not on the list
    [InlineData("not-an-emoji")] // arbitrary text can't be smuggled through
    [InlineData("")]
    [InlineData(null)]
    public void Anything_off_the_allowlist_is_rejected(string? value) =>
        ReactionPolicy.IsAllowed(value).Should().BeFalse();

    [Fact]
    public void The_allowlist_is_non_empty_and_matches_IsAllowed()
    {
        ReactionPolicy.Allowed.Should().NotBeEmpty();
        ReactionPolicy.Allowed.Should().OnlyContain(e => ReactionPolicy.IsAllowed(e));
    }
}
