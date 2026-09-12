using System.Collections.Generic;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.ArtworkGenerator.Tests;

/// <summary>
/// Tests for the vertical layout primitive the poster styles place elements with.
/// </summary>
public class LayoutColumnTests
{
    private static readonly SKRect Bounds = SKRect.Create(0, 0, 1000, 1000);

    [Fact]
    public void BottomAnchored_PacksBlocksAgainstTheBottomInOrder()
    {
        var stack = new LayoutColumn(Bounds, 10f, LayoutAnchor.Bottom);
        stack.Add("code", 100f).Add("title", 200f);

        // 100 + 10 + 200 = 310 tall, so the run starts at 1000 - 310 = 690.
        Assert.Equal(690f, stack.Slot("code").Top, 3);
        Assert.Equal(790f, stack.Slot("code").Bottom, 3);
        Assert.Equal(800f, stack.Slot("title").Top, 3);
        Assert.Equal(1000f, stack.Slot("title").Bottom, 3);
        Assert.Equal(310f, stack.Consumed, 3);
    }

    [Fact]
    public void TopAnchored_PacksBlocksAgainstTheTopInOrder()
    {
        var stack = new LayoutColumn(Bounds, 10f, LayoutAnchor.Top);
        stack.Add("first", 100f).Add("second", 200f);

        Assert.Equal(0f, stack.Slot("first").Top, 3);
        Assert.Equal(110f, stack.Slot("second").Top, 3);
    }

    [Fact]
    public void CenterAnchored_PacksBlocksAroundTheMiddleInOrder()
    {
        var stack = new LayoutColumn(Bounds, 10f, LayoutAnchor.Center);
        stack.Add("code", 100f).Add("title", 200f);

        // 310 tall centered in 1000 starts at 500 - 155 = 345.
        Assert.Equal(345f, stack.Slot("code").Top, 3);
        Assert.Equal(455f, stack.Slot("title").Top, 3);
        Assert.Equal(655f, stack.Slot("title").Bottom, 3);
    }

    /// <summary>
    /// A centered run leaves a band above and below, and a caller asking what is left wants the one
    /// it can actually draw in.
    /// </summary>
    [Fact]
    public void CenterAnchored_RemainingIsTheTallerOfTheTwoBands()
    {
        var stack = new LayoutColumn(SKRect.Create(0, 0, 1000, 1000), 10f, LayoutAnchor.Center);
        stack.Add("only", 100f);

        var remaining = stack.Remaining;

        // Symmetric bands here, so the upper one wins the tie and is 450 - 10 tall.
        Assert.Equal(0f, remaining.Top, 3);
        Assert.Equal(440f, remaining.Height, 3);
    }

    /// <summary>
    /// The whole point: whatever is left is a fact, not a subtraction a caller had to remember.
    /// </summary>
    [Fact]
    public void Remaining_ExcludesTheBlocksAndOneGap()
    {
        var stack = new LayoutColumn(Bounds, 10f, LayoutAnchor.Bottom);
        stack.Add("code", 100f).Add("title", 200f);

        var remaining = stack.Remaining;
        Assert.Equal(0f, remaining.Top, 3);
        Assert.Equal(680f, remaining.Bottom, 3);      // 690 minus one 10px gap
        Assert.True(remaining.Bottom <= stack.Slot("code").Top);
    }

    [Fact]
    public void Blocks_NeverOverlapForAnySpacing()
    {
        foreach (var spacing in new[] { 0f, 1f, 25f, 120f })
        {
            var stack = new LayoutColumn(Bounds, spacing, LayoutAnchor.Bottom);
            stack.Add("a", 80f).Add("b", 90f).Add("c", 70f);

            var a = stack.Slot("a");
            var b = stack.Slot("b");
            var c = stack.Slot("c");

            Assert.True(a.Bottom <= b.Top, $"a/b overlap at spacing {spacing}");
            Assert.True(b.Bottom <= c.Top, $"b/c overlap at spacing {spacing}");
            Assert.True(stack.Remaining.Bottom <= a.Top, $"remaining runs into a at spacing {spacing}");
        }
    }

    [Fact]
    public void ZeroHeightBlocks_TakeNoSpaceAndAreNotSlots()
    {
        var stack = new LayoutColumn(Bounds, 10f, LayoutAnchor.Bottom);
        stack.Add("hidden", 0f).Add("title", 200f);

        Assert.False(stack.TryGetSlot("hidden", out _));
        Assert.Equal(200f, stack.Consumed, 3);
        Assert.Equal(800f, stack.Slot("title").Top, 3);
    }

    [Fact]
    public void EmptyStack_LeavesTheBoundsIntact()
    {
        var stack = new LayoutColumn(Bounds, 10f, LayoutAnchor.Bottom);

        Assert.Equal(0f, stack.Consumed, 3);
        Assert.Equal(Bounds, stack.Remaining);
    }

    [Fact]
    public void Overfull_ClampsRemainingRatherThanInverting()
    {
        var stack = new LayoutColumn(Bounds, 10f, LayoutAnchor.Bottom);
        stack.Add("huge", 5000f);

        Assert.True(stack.Remaining.Height >= 0f);
    }

    [Fact]
    public void Slot_ThrowsForAnUnknownKey()
    {
        var stack = new LayoutColumn(Bounds, 10f, LayoutAnchor.Bottom);
        stack.Add("title", 100f);

        Assert.Throws<KeyNotFoundException>(() => stack.Slot("nope"));
    }
}
