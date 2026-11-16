using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Kingdom.Core;

namespace Kingdom.Core.Tests;

/// <summary>
/// Minimal test runner without NuGet dependencies.
/// Add a test by adding a (name, action) entry to <see cref="Tests"/>.
/// </summary>
public static class Program
{
    private static readonly List<(string Name, Action Run)> Tests = new()
    {
        ("GameInfo.Version is a real MAJOR.MINOR.PATCH version", () =>
            Assert.True(
                Regex.IsMatch(GameInfo.Version, @"^\d+\.\d+\.\d+$") && GameInfo.Version != "0.0.0",
                $"unexpected version '{GameInfo.Version}'")),
        ("GameInfo.Title is set", () =>
            Assert.True(!string.IsNullOrWhiteSpace(GameInfo.Title), "Title is empty")),
    };

    public static int Main()
    {
        int failed = 0;
        foreach (var (name, run) in Tests)
        {
            try
            {
                run();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"FAIL  {name}: {ex.Message}");
            }
        }
        Console.WriteLine($"{Tests.Count - failed}/{Tests.Count} passed");
        return failed == 0 ? 0 : 1;
    }
}

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"expected '{expected}', got '{actual}'");
    }
}
