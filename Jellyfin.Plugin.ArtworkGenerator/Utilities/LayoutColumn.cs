using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Utilities;

/// <summary>
/// Which edge of its bounds a <see cref="LayoutColumn"/> packs its blocks against.
/// </summary>
public enum LayoutAnchor
{
    /// <summary>Blocks sit against the top edge, filling downward.</summary>
    Top,

    /// <summary>Blocks sit against the bottom edge, filling upward.</summary>
    Bottom,

    /// <summary>Blocks sit centered between the two edges.</summary>
    Center
}

/// <summary>
/// Places a run of measured blocks vertically inside a rectangle, separated by a uniform gap.
/// </summary>
/// <remarks>
/// Styles used to position each element by re-deriving its neighbour's arithmetic: the logo
/// subtracted its own copy of the typography height calculation, the episode code subtracted its
/// own copy of the title's. Two copies of one measurement drift apart as soon as either side
/// changes, which is how elements ended up overlapping. Here nothing is placed until every block
/// has been measured, so a slot is a fact rather than a subtraction the caller had to remember,
/// and <see cref="Remaining"/> is whatever genuinely survives rather than a guess.
/// </remarks>
public sealed class LayoutColumn
{
    private readonly List<Block> _blocks = new();
    private readonly SKRect _bounds;
    private readonly float _spacing;
    private readonly LayoutAnchor _anchor;

    public LayoutColumn(SKRect bounds, float spacing, LayoutAnchor anchor = LayoutAnchor.Bottom)
    {
        _bounds = bounds;
        _spacing = Math.Max(0f, spacing);
        _anchor = anchor;
    }

    /// <summary>
    /// Gets the total height the placed blocks occupy, including the gaps between them.
    /// Zero when nothing has been added.
    /// </summary>
    public float Consumed
    {
        get
        {
            if (_blocks.Count == 0)
            {
                return 0f;
            }

            return _blocks.Sum(b => b.Height) + (_spacing * (_blocks.Count - 1));
        }
    }

    /// <summary>
    /// Gets the top edge of the first block, which is where the anchor puts the whole stack.
    /// </summary>
    private float StartY => _anchor switch
    {
        LayoutAnchor.Bottom => _bounds.Bottom - Consumed,
        LayoutAnchor.Center => _bounds.MidY - (Consumed / 2f),
        _ => _bounds.Top
    };

    /// <summary>
    /// Gets the part of the bounds no block occupies, already inset by one gap so anything drawn
    /// there keeps its distance from the stack. Empty (zero height) when the blocks fill the bounds.
    /// A centered stack leaves a band on each side, and the taller one is returned, since a caller
    /// asking for what is left wants the space it can actually use.
    /// </summary>
    public SKRect Remaining
    {
        get
        {
            if (_blocks.Count == 0)
            {
                return _bounds;
            }

            var used = Consumed + _spacing;

            if (_anchor == LayoutAnchor.Center)
            {
                var above = Math.Max(0f, StartY - _spacing - _bounds.Top);
                var below = Math.Max(0f, _bounds.Bottom - (StartY + Consumed + _spacing));

                return above >= below
                    ? SKRect.Create(_bounds.Left, _bounds.Top, _bounds.Width, above)
                    : SKRect.Create(_bounds.Left, _bounds.Bottom - below, _bounds.Width, below);
            }

            return _anchor == LayoutAnchor.Bottom
                ? SKRect.Create(_bounds.Left, _bounds.Top, _bounds.Width, Math.Max(0f, _bounds.Height - used))
                : SKRect.Create(_bounds.Left, Math.Min(_bounds.Bottom, _bounds.Top + used), _bounds.Width, Math.Max(0f, _bounds.Height - used));
        }
    }

    /// <summary>
    /// Adds a block, in visual top-to-bottom order. Blocks with no height are skipped, so a caller
    /// can add an element unconditionally and let a hidden or dropped one simply take no space.
    /// </summary>
    public LayoutColumn Add(string key, float height)
    {
        if (height > 0f && !string.IsNullOrEmpty(key))
        {
            _blocks.Add(new Block(key, height));
        }

        return this;
    }

    /// <summary>
    /// Returns the rectangle allotted to a block. Throws when the key was never added, which is a
    /// caller bug rather than a layout condition — an element that may be absent is added with a
    /// zero height and queried with <see cref="TryGetSlot"/>.
    /// </summary>
    public SKRect Slot(string key)
    {
        if (!TryGetSlot(key, out var rect))
        {
            throw new KeyNotFoundException(
                string.Create(CultureInfo.InvariantCulture, $"No layout block was added for '{key}'."));
        }

        return rect;
    }

    /// <summary>
    /// Returns the rectangle allotted to a block, or false when it was never added or had no height.
    /// </summary>
    public bool TryGetSlot(string key, out SKRect rect)
    {
        rect = SKRect.Empty;

        var index = _blocks.FindIndex(b => string.Equals(b.Key, key, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        var y = StartY;

        for (var i = 0; i < index; i++)
        {
            y += _blocks[i].Height + _spacing;
        }

        rect = SKRect.Create(_bounds.Left, y, _bounds.Width, _blocks[index].Height);
        return true;
    }

    private readonly record struct Block(string Key, float Height);
}
