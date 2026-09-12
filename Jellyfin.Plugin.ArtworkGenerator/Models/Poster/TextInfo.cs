using System;

namespace Jellyfin.Plugin.ArtworkGenerator.Models
{
    public struct TextInfo : IEquatable<TextInfo>
    {
        private const float Epsilon = 0.0001f;

        public float Height { get; set; }

        public float Width { get; set; }

        public float CenterX { get; set; }

        public float Y { get; set; }

        // Equals
        // Compares this TextInfo with another for value equality using epsilon comparison.
        public readonly bool Equals(TextInfo other)
        {
            return Math.Abs(Height - other.Height) < Epsilon &&
                   Math.Abs(Width - other.Width) < Epsilon &&
                   Math.Abs(CenterX - other.CenterX) < Epsilon &&
                   Math.Abs(Y - other.Y) < Epsilon;
        }

        // Equals
        // Compares this TextInfo with an object for value equality.
        public override readonly bool Equals(object? obj)
        {
            return obj is TextInfo other && Equals(other);
        }

        // GetHashCode
        // Returns a hash code based on the TextInfo properties.
        public override readonly int GetHashCode()
        {
            return HashCode.Combine(Height, Width, CenterX, Y);
        }

        public static bool operator ==(TextInfo left, TextInfo right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TextInfo left, TextInfo right)
        {
            return !left.Equals(right);
        }
    }
}
