using System.Reflection;

namespace Kingdom.Core;

/// <summary>Static information about the game build.</summary>
public static class GameInfo
{
    /// <summary>Working title until the game gets a real name.</summary>
    public const string Title = "Kingdom Game";

    /// <summary>Version taken from Directory.Build.props, e.g. "0.0.1".</summary>
    public static string Version { get; } =
        typeof(GameInfo).Assembly.GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "0.0.0";
}
