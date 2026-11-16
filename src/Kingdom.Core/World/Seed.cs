using System;
using System.Text;

namespace Kingdom.Core.World;

/// <summary>
/// A world seed: letters and digits only, case-insensitive (stored upper-case).
/// The same seed always produces the same world.
/// </summary>
public readonly struct Seed : IEquatable<Seed>
{
    public const int MaxLength = 16;
    public const int RandomLength = 8;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <summary>Normalized text, e.g. "K7QX2M9A".</summary>
    public string Text { get; }

    /// <summary>64-bit value derived from <see cref="Text"/> (FNV-1a), used to seed all generators.</summary>
    public ulong Value { get; }

    private Seed(string text)
    {
        Text = text;
        Value = Fnv1a(text);
    }

    /// <summary>
    /// Parses user input. Whitespace around the input is ignored, letters are upper-cased.
    /// Fails on empty input, input longer than <see cref="MaxLength"/>, or any character that is not A-Z / 0-9.
    /// </summary>
    public static bool TryParse(string? input, out Seed seed, out string error)
    {
        seed = default;
        string text = (input ?? string.Empty).Trim().ToUpperInvariant();
        if (text.Length == 0)
        {
            error = "Seed is empty.";
            return false;
        }
        if (text.Length > MaxLength)
        {
            error = $"Seed is longer than {MaxLength} characters.";
            return false;
        }
        foreach (char c in text)
        {
            if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
            {
                error = "Seed may only contain letters and digits.";
                return false;
            }
        }
        seed = new Seed(text);
        error = string.Empty;
        return true;
    }

    /// <summary>Parses input and throws on invalid input. Convenient in tests and tools.</summary>
    public static Seed Parse(string input)
    {
        if (!TryParse(input, out var seed, out var error))
            throw new ArgumentException(error, nameof(input));
        return seed;
    }

    /// <summary>A new random seed of <see cref="RandomLength"/> characters.</summary>
    public static Seed NewRandom(Random? random = null)
    {
        random ??= Random.Shared;
        var sb = new StringBuilder(RandomLength);
        for (int i = 0; i < RandomLength; i++)
            sb.Append(Alphabet[random.Next(Alphabet.Length)]);
        return new Seed(sb.ToString());
    }

    private static ulong Fnv1a(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    public bool Equals(Seed other) => Text == other.Text;
    public override bool Equals(object? obj) => obj is Seed s && Equals(s);
    public override int GetHashCode() => Text?.GetHashCode() ?? 0;
    public override string ToString() => Text;
}
