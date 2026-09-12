using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Utilities;

/// <summary>
/// What the layers of one poster have already claimed, so a later one can keep out of the way.
/// </summary>
/// <remarks>
/// The canvas and the overlay cover the whole frame and claim nothing. Everything drawn on top of
/// them — text, a logo, a graphic, the shape a design cuts out of its overlay — competes for the
/// same space, and each used to place itself without knowing where the others had gone. The text is
/// measured before anything is drawn and claims its area first, because it is the one element a
/// poster cannot afford to lose.
/// </remarks>
public sealed class PosterLayout
{
    private readonly List<SKRect> _claimed = new();

    /// <summary>Gets the areas already spoken for, in the order they were claimed.</summary>
    public IReadOnlyList<SKRect> Claimed => _claimed;

    /// <summary>Gets a value indicating whether anything has been claimed.</summary>
    public bool IsEmpty => _claimed.Count == 0;

    /// <summary>
    /// Claims an area. An empty rectangle claims nothing, so a caller can hand over whatever it
    /// measured without checking first.
    /// </summary>
    public void Claim(SKRect area)
    {
        if (area.Width > 0 && area.Height > 0)
        {
            _claimed.Add(area);
        }
    }

    /// <summary>Whether an area runs into anything already claimed.</summary>
    public bool Collides(SKRect area)
    {
        foreach (var claim in _claimed)
        {
            if (area.IntersectsWith(claim))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Moves an area vertically until it clears what is already claimed, keeping it inside the
    /// bounds. Tries above the obstruction first, then below, and gives the original back when
    /// neither fits, since a poster with something slightly overlapped beats one with it missing.
    /// </summary>
    public SKRect Avoid(SKRect area, SKRect bounds)
    {
        if (!Collides(area))
        {
            return area;
        }

        foreach (var claim in _claimed)
        {
            if (!area.IntersectsWith(claim))
            {
                continue;
            }

            var above = SKRect.Create(area.Left, claim.Top - area.Height, area.Width, area.Height);
            if (above.Top >= bounds.Top && !Collides(above))
            {
                return above;
            }

            var below = SKRect.Create(area.Left, claim.Bottom, area.Width, area.Height);
            if (below.Bottom <= bounds.Bottom && !Collides(below))
            {
                return below;
            }
        }

        return area;
    }
}
