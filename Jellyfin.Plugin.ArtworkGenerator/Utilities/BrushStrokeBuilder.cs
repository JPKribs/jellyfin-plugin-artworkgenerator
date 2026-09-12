using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Jellyfin.Plugin.ArtworkGenerator.Utilities
{
    public class BrushStrokeBuilder
    {
        // The tallest a stroke may be relative to its own length. A brush stroke reads as a stroke
        // only while it is longer than it is thick; past this it reads as a blob. A wide poster
        // never reaches this cap, so raising it thickens the strokes on a tall poster alone.
        private const float MaxHeightToLengthRatio = 0.46f;

        private readonly Random _random;

        public BrushStrokeBuilder(int seed)
        {
            _random = new Random(seed);
        }

        // BuildStrokePath
        // Builds the stroke geometry. Exactly one SKPath is returned; any other path allocated
        // along the way is released, including when stroke building or Simplify throws part way
        // through — these are native handles, so they cannot be left to the success path alone.
        public SKPath BuildStrokePath(SKRect bounds, float unit)
        {
            SKPath? combined = new SKPath { FillType = SKPathFillType.Winding };
            SKPath? simplified = null;

            try
            {
                return BuildStrokePathCore(bounds, unit, ref combined, ref simplified);
            }
            finally
            {
                simplified?.Dispose();
                combined?.Dispose();
            }
        }

        // BuildStrokePathCore
        // Hands ownership of whichever path it returns back to the caller by nulling the
        // corresponding ref, so the caller's finally disposes only the one left behind.
        private SKPath BuildStrokePathCore(SKRect bounds, float unit, ref SKPath? combined, ref SKPath? simplified)
        {
            var working = combined!;

            // The strokes are the composition, so they take the middle of the frame and stay there.
            // They used to be squeezed into whatever room the text left above itself, which tied
            // the artwork to the text's placement and collapsed the band when the text moved up.
            float margin = bounds.Height * 0.06f;
            float usableTop = bounds.Top + margin;
            float usableHeight = bounds.Height - (margin * 2f);

            int strokeCount = _random.Next(2) == 0 ? 2 : 3;
            float perStrokeHeight = strokeCount == 2 ? unit * 0.48f : unit * 0.38f;

            // A stroke's height comes from the short side, but its length is always the width. On a
            // tall poster that leaves each stroke half as tall as it is long, so the stack cannot
            // fit and the tilt turns it into a cross. Capping the height against the stroke's own
            // length keeps the painted proportion in both shapes. On a wide poster the cap is never
            // reached, so landscape output is unchanged, and the cap consumes no random numbers, so
            // the stroke layout for a given item is untouched there.
            perStrokeHeight = MathF.Min(perStrokeHeight, bounds.Width * 1.08f * MaxHeightToLengthRatio);

            // Per-gap spacing ranges from 5% of the short side in overlap to the same in gap (signed).
            // If the resulting stack would exceed the usable area, force more overlap.
            float desiredOverlap = ((float)_random.NextDouble() * 2f - 1f) * unit * 0.05f;
            float minOverlap = (strokeCount * perStrokeHeight - usableHeight) / (strokeCount - 1);
            float overlap = MathF.Max(desiredOverlap, minOverlap);

            float spacing = perStrokeHeight - overlap;
            float totalSpan = perStrokeHeight + (strokeCount - 1) * spacing;
            float topMargin = MathF.Max(0f, (usableHeight - totalSpan) * 0.5f);
            float firstCenterY = usableTop + topMargin + perStrokeHeight * 0.5f;

            for (int s = 0; s < strokeCount; s++)
            {
                float centerY = firstCenterY + s * spacing
                    + (float)(_random.NextDouble() - 0.5f) * perStrokeHeight * 0.03f;
                AppendBrushStroke(working, bounds, centerY, perStrokeHeight, s);
            }

            // Final normalization: resolve any subtle self-intersections from sharp
            // wobble/drift combinations that could leave winding-0 carve-outs. With
            // all subpaths already wound CW this is a no-op for clean inputs and a
            // cleanup for edge cases.
            simplified = new SKPath();

            if (working.Simplify(simplified) && !simplified.IsEmpty)
            {
                var result = simplified;
                simplified = null;
                return result;
            }

            combined = null;
            return working;
        }

        // AppendBrushStroke
        // Draws a single brush stroke as a dense body of loaded paint with bristle
        // filaments fraying out along the top and bottom edges and at the tips.
        private void AppendBrushStroke(SKPath combined, SKRect bounds, float centerY, float strokeHeight, int strokeIndex)
        {
            float startX = bounds.Left - bounds.Width * 0.04f;
            float endX = bounds.Right + bounds.Width * 0.04f;

            var centerline = BuildCenterline(startX, endX, centerY, strokeHeight, strokeIndex);

            AppendStrokeBody(combined, centerline, strokeHeight, strokeIndex);

            int bristleCount = 55 + _random.Next(25);

            for (int b = 0; b < bristleCount; b++)
            {
                float bristleNorm = (b / (float)(bristleCount - 1)) * 2f - 1f;
                float absNorm = MathF.Abs(bristleNorm);

                float lengthFalloff = absNorm > 0.75f ? (absNorm - 0.75f) / 0.25f : 0f;

                float startJitter = (float)_random.NextDouble() * bounds.Width * 0.05f
                    + bounds.Width * 0.18f * lengthFalloff * (float)_random.NextDouble();
                float endJitter = (float)_random.NextDouble() * bounds.Width * 0.05f
                    + bounds.Width * 0.20f * lengthFalloff * (float)_random.NextDouble();

                float bStart = startX + startJitter;
                float bEnd = endX - endJitter;
                if (bEnd - bStart < bounds.Width * 0.08f) continue;

                float bristleOffset = bristleNorm * strokeHeight * 0.48f;
                bristleOffset += (float)(_random.NextDouble() - 0.5f) * strokeHeight * 0.04f;

                float thicknessRoll = (float)_random.NextDouble();
                float bristleWidth = strokeHeight * (0.010f + thicknessRoll * 0.022f);
                bristleWidth *= 1f - absNorm * 0.55f;
                if (bristleWidth < strokeHeight * 0.004f) bristleWidth = strokeHeight * 0.004f;

                float baseGapChance = absNorm > 0.6f
                    ? 0.02f + (absNorm - 0.6f) / 0.4f * 0.20f
                    : 0.005f;

                var segments = BuildBristleSegments(
                    bStart, bEnd, centerline, bristleOffset, baseGapChance,
                    strokeHeight, strokeIndex * 10007 + b);

                foreach (var segment in segments)
                {
                    AppendOffsetRibbon(combined, segment, bristleWidth * 0.5f);
                }
            }
        }

        // AppendStrokeBody
        // Draws the dense "loaded paint" core of the stroke as a closed ribbon following
        // the centerline. Tapered at the tips so it sits inside the frayed bristle edges.
        // Vertices are added clockwise (top L→R, bottom R→L) for consistent winding.
        private void AppendStrokeBody(SKPath combined, List<SKPoint> centerline, float strokeHeight, int seed)
        {
            if (centerline.Count < 4) return;

            var rng = new Random(seed * 7919 + 31);

            float maxHalf = strokeHeight * 0.30f;
            float bodyStartT = 0.06f + (float)rng.NextDouble() * 0.06f;
            float bodyEndT = 0.84f + (float)rng.NextDouble() * 0.10f;

            float startX = centerline[0].X;
            float endX = centerline[centerline.Count - 1].X;
            float length = endX - startX;
            if (length <= 0f) return;

            var top = new List<SKPoint>(centerline.Count);
            var bottom = new List<SKPoint>(centerline.Count);

            float edgePhaseTop = (float)rng.NextDouble() * MathF.PI * 2f;
            float edgePhaseBottom = (float)rng.NextDouble() * MathF.PI * 2f;

            for (int i = 0; i < centerline.Count; i++)
            {
                float t = (centerline[i].X - startX) / length;
                if (t < bodyStartT || t > bodyEndT) continue;

                float localT = (t - bodyStartT) / (bodyEndT - bodyStartT);

                float taper = localT < 0.18f
                    ? MathF.Pow(localT / 0.18f, 0.55f)
                    : localT > 0.82f
                        ? MathF.Pow((1f - localT) / 0.18f, 0.85f)
                        : 1f;

                float widthMod = 0.85f + 0.15f * MathF.Sin(localT * 9f + seed);
                float halfH = maxHalf * widthMod * taper;

                // Scale jitter by halfH so the tips (where halfH→0) get no jitter and
                // can't fold the top edge below the bottom edge — that fold creates a
                // bowtie self-intersection which carves a white sliver out of the body.
                float jitterScale = halfH / maxHalf;
                float jitterTop = (MathF.Sin(localT * 22f + edgePhaseTop) * strokeHeight * 0.025f
                    + ((float)rng.NextDouble() - 0.5f) * strokeHeight * 0.018f) * jitterScale;
                float jitterBot = (MathF.Sin(localT * 19f + edgePhaseBottom) * strokeHeight * 0.025f
                    + ((float)rng.NextDouble() - 0.5f) * strokeHeight * 0.018f) * jitterScale;

                top.Add(new SKPoint(centerline[i].X, centerline[i].Y - halfH + jitterTop));
                bottom.Add(new SKPoint(centerline[i].X, centerline[i].Y + halfH + jitterBot));
            }

            if (top.Count < 2) return;

            combined.MoveTo(top[0]);
            for (int i = 1; i < top.Count; i++) combined.LineTo(top[i]);
            for (int i = bottom.Count - 1; i >= 0; i--) combined.LineTo(bottom[i]);
            combined.Close();
        }

        // AppendOffsetRibbon
        // Adds a closed ribbon to `combined` representing one polyline thickened by
        // halfWidth on each side. We compute per-vertex perpendiculars from the tangent
        // and walk top forward then bottom backward, guaranteeing clockwise winding.
        // This replaces SKPaint.GetFillPath, whose winding is unpredictable for wobbly
        // polylines and was causing XOR-style carve-outs in the brush body.
        private void AppendOffsetRibbon(SKPath combined, List<SKPoint> pts, float halfWidth)
        {
            if (pts.Count < 2 || halfWidth <= 0f) return;

            int n = pts.Count;
            var top = new SKPoint[n];
            var bot = new SKPoint[n];

            for (int i = 0; i < n; i++)
            {
                SKPoint tangent;
                if (i == 0)
                {
                    tangent = new SKPoint(pts[1].X - pts[0].X, pts[1].Y - pts[0].Y);
                }
                else if (i == n - 1)
                {
                    tangent = new SKPoint(pts[i].X - pts[i - 1].X, pts[i].Y - pts[i - 1].Y);
                }
                else
                {
                    tangent = new SKPoint(pts[i + 1].X - pts[i - 1].X, pts[i + 1].Y - pts[i - 1].Y);
                }

                float mag = MathF.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y);
                if (mag < 1e-6f)
                {
                    top[i] = pts[i];
                    bot[i] = pts[i];
                    continue;
                }

                // Perpendicular pointing "up" in screen coords (negative Y) so the
                // ribbon is wound clockwise like the body polygon — otherwise their
                // overlap would cancel under Winding fill.
                float px = tangent.Y / mag;
                float py = -tangent.X / mag;

                top[i] = new SKPoint(pts[i].X + px * halfWidth, pts[i].Y + py * halfWidth);
                bot[i] = new SKPoint(pts[i].X - px * halfWidth, pts[i].Y - py * halfWidth);
            }

            combined.MoveTo(top[0]);
            for (int i = 1; i < n; i++) combined.LineTo(top[i]);
            for (int i = n - 1; i >= 0; i--) combined.LineTo(bot[i]);
            combined.Close();
        }

        // BuildCenterline
        // Samples the stroke's spine as a slow undulating curve made of two stacked sine
        // waves — enough motion to look painted, not enough to look like a snake.
        private List<SKPoint> BuildCenterline(float startX, float endX, float centerY, float strokeHeight, int seed)
        {
            int samples = 120;
            var points = new List<SKPoint>(samples + 1);

            float amp1 = strokeHeight * (0.10f + (float)_random.NextDouble() * 0.10f);
            float amp2 = strokeHeight * (0.04f + (float)_random.NextDouble() * 0.06f);
            float freq1 = 0.8f + (float)_random.NextDouble() * 0.8f;
            float freq2 = 2.5f + (float)_random.NextDouble() * 1.8f;
            float phase1 = (float)_random.NextDouble() * MathF.PI * 2f;
            float phase2 = (float)_random.NextDouble() * MathF.PI * 2f;

            float tilt = (float)(_random.NextDouble() - 0.5) * strokeHeight * 0.6f;

            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                float x = startX + (endX - startX) * t;
                float y = centerY
                    + tilt * (t - 0.5f) * 2f
                    + MathF.Sin(t * MathF.PI * freq1 + phase1) * amp1
                    + MathF.Sin(t * MathF.PI * freq2 + phase2) * amp2;
                points.Add(new SKPoint(x, y));
            }
            return points;
        }

        // BuildBristleSegments
        // Walks one bristle's path point by point, emitting one polyline per contiguous
        // pen-down stretch. Breaks where the bristle skips the canvas (dry brush). Each
        // returned polyline is later thickened into a ribbon with consistent winding.
        private List<List<SKPoint>> BuildBristleSegments(
            float bStart,
            float bEnd,
            List<SKPoint> centerline,
            float yOffset,
            float baseGapChance,
            float strokeHeight,
            int seed)
        {
            var result = new List<List<SKPoint>>();
            var rng = new Random(seed);

            float wobbleAmp = strokeHeight * (0.008f + (float)rng.NextDouble() * 0.022f);
            float wobbleFreq = 3.5f + (float)rng.NextDouble() * 7f;
            float wobblePhase = (float)rng.NextDouble() * MathF.PI * 2f;

            float driftAmp = strokeHeight * (0.01f + (float)rng.NextDouble() * 0.02f);
            float driftFreq = 0.5f + (float)rng.NextDouble() * 1.2f;
            float driftPhase = (float)rng.NextDouble() * MathF.PI * 2f;

            int samples = 80;
            List<SKPoint>? current = null;
            int consecutiveGaps = 0;

            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                float x = bStart + (bEnd - bStart) * t;

                float cy = InterpolateCenterline(centerline, x);
                float wobble = MathF.Sin(t * wobbleFreq + wobblePhase) * wobbleAmp;
                float drift = MathF.Sin(t * driftFreq + driftPhase) * driftAmp;
                float y = cy + yOffset + wobble + drift;

                float endDist = MathF.Min(t, 1f - t);
                float tipBoost = endDist < 0.18f ? (0.18f - endDist) / 0.18f : 0f;
                float gapChance = baseGapChance + tipBoost * tipBoost * 0.6f;

                if (consecutiveGaps > 6) gapChance = 0f;

                if (rng.NextDouble() < gapChance)
                {
                    if (current != null)
                    {
                        if (current.Count >= 2) result.Add(current);
                        current = null;
                    }
                    consecutiveGaps++;
                    continue;
                }

                consecutiveGaps = 0;
                if (current == null)
                {
                    current = new List<SKPoint>();
                }
                current.Add(new SKPoint(x, y));
            }

            if (current != null && current.Count >= 2) result.Add(current);

            return result;
        }

        // InterpolateCenterline
        // Linear lookup of the precomputed centerline at an arbitrary x. The centerline is
        // sorted by x and densely sampled, so linear is good enough and avoids cubic cost.
        private float InterpolateCenterline(List<SKPoint> points, float x)
        {
            if (points.Count == 0) return 0f;
            if (x <= points[0].X) return points[0].Y;
            if (x >= points[points.Count - 1].X) return points[points.Count - 1].Y;

            int lo = 0;
            int hi = points.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (points[mid].X <= x) lo = mid;
                else hi = mid;
            }

            float span = points[hi].X - points[lo].X;
            float t = span > 0f ? (x - points[lo].X) / span : 0f;
            return points[lo].Y * (1f - t) + points[hi].Y * t;
        }

    }
}
