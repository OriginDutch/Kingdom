using Godot;
using Kingdom.Core;

namespace Kingdom.Game;

/// <summary>
/// Entry scene. For now it only proves that Godot can load C# code
/// and call into Kingdom.Core: the label shows the version from the core library.
/// </summary>
public partial class Main : Node3D
{
    public override void _Ready()
    {
        var label = GetNode<Label>("UI/VersionLabel");
        label.Text = $"{GameInfo.Title} v{GameInfo.Version}";
        GD.Print($"{GameInfo.Title} v{GameInfo.Version} started");
    }
}
