using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using Kingdom.Core;
using Kingdom.Core.World;
using Kingdom.Game.World;

namespace Kingdom.Game;

/// <summary>
/// Milestone 1 entry scene: generate a world from a seed, fly around, click to found your village,
/// then one bot picks its own site.
/// </summary>
public partial class Main : Node3D
{
    private const string PlayerVillageName = "Your village";
    private const string BotVillageName = "Rival village";
    private const int BotId = 1;

    private WorldView _worldView = null!;
    private RtsCamera _cameraRig = null!;

    private LineEdit _seedInput = null!;
    private Button _generateButton = null!;
    private Button _randomButton = null!;
    private Label _status = null!;

    private WorldMap? _map;
    private readonly List<VillageSite> _villages = new();
    private bool _busy;
    private Vector2 _mouseDownPosition;
    private bool _leftPressedInWorld;

    public override void _Ready()
    {
        GD.Print($"{GameInfo.Title} v{GameInfo.Version} started");

        _worldView = new WorldView { Name = "WorldView" };
        AddChild(_worldView);

        _cameraRig = new RtsCamera { Name = "CameraRig" };
        AddChild(_cameraRig);

        BuildUi();
        ConfigureEnvironment();

        var seed = Seed.NewRandom();
        _seedInput.Text = seed.Text;
        _ = GenerateAsync(seed);
    }

    // ------------------------------------------------------------------ UI

    private void BuildUi()
    {
        var layer = new CanvasLayer { Name = "UI" };
        AddChild(layer);

        var panel = new PanelContainer { Position = new Vector2(12, 12) };
        layer.AddChild(panel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 10);
        panel.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        margin.AddChild(box);

        box.AddChild(new Label { Text = $"{GameInfo.Title} v{GameInfo.Version}" });

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        box.AddChild(row);

        row.AddChild(new Label { Text = "Seed" });
        _seedInput = new LineEdit
        {
            MaxLength = Seed.MaxLength,
            CustomMinimumSize = new Vector2(170, 0),
            PlaceholderText = "letters and digits",
        };
        // Monospace font where 0/O and 1/I look clearly different (Consolas ships with Windows).
        _seedInput.AddThemeFontOverride("font", new SystemFont
        {
            FontNames = new[] { "Consolas", "Cascadia Mono", "DejaVu Sans Mono", "monospace" },
        });
        _seedInput.TextChanged += OnSeedTextChanged;
        _seedInput.TextSubmitted += _ =>
        {
            _seedInput.ReleaseFocus(); // give the keyboard back to the camera
            OnGeneratePressed();
        };
        row.AddChild(_seedInput);

        // FocusMode None: buttons must not keep keyboard focus (arrows/space would drive the UI instead of the camera).
        _generateButton = new Button { Text = "Generate", FocusMode = Control.FocusModeEnum.None };
        _generateButton.Pressed += OnGeneratePressed;
        row.AddChild(_generateButton);

        _randomButton = new Button { Text = "New random seed", FocusMode = Control.FocusModeEnum.None };
        _randomButton.Pressed += OnRandomPressed;
        row.AddChild(_randomButton);

        var copyButton = new Button { Text = "Copy", FocusMode = Control.FocusModeEnum.None, TooltipText = "Copy the seed to the clipboard" };
        copyButton.Pressed += () =>
        {
            if (_seedInput.Text.Length == 0) { SetStatus("No seed to copy.", isError: true); return; }
            DisplayServer.ClipboardSet(_seedInput.Text);
            SetStatus($"Seed {_seedInput.Text} copied to the clipboard.");
        };
        row.AddChild(copyButton);

        var pasteButton = new Button { Text = "Paste", FocusMode = Control.FocusModeEnum.None, TooltipText = "Paste a seed from the clipboard (then press Generate)" };
        pasteButton.Pressed += () =>
        {
            // Clean first, so separators like dashes don't count against the length limit.
            string pasted = CleanSeedText(DisplayServer.ClipboardGet());
            if (pasted.Length == 0) { SetStatus("The clipboard has no letters or digits to use as a seed.", isError: true); return; }
            if (pasted.Length > Seed.MaxLength)
            {
                SetStatus($"That seed is longer than {Seed.MaxLength} characters.", isError: true);
                return;
            }
            _seedInput.Text = pasted;
            SetStatus($"Seed {pasted} pasted. Press Generate to build it.");
        };
        row.AddChild(pasteButton);

        _status = new Label
        {
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(520, 0),
        };
        box.AddChild(_status);

        box.AddChild(new Label
        {
            Text = "WASD / arrows: move   Shift: faster   Wheel: zoom   Right mouse or Q/E: rotate\n" +
                   "Left click: found your village (click again to move it)",
            Modulate = new Color(1, 1, 1, 0.7f),
        });
    }

    /// <summary>Upper-case letters and digits only.</summary>
    private static string CleanSeedText(string text)
    {
        var clean = new System.Text.StringBuilder(text.Length);
        foreach (char c in text.ToUpperInvariant())
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) clean.Append(c);
        return clean.ToString();
    }

    /// <summary>Keeps the seed field to upper-case letters and digits while typing.</summary>
    private void OnSeedTextChanged(string text)
    {
        string result = CleanSeedText(text);
        if (result != text)
        {
            int caret = _seedInput.CaretColumn;
            _seedInput.Text = result;
            _seedInput.CaretColumn = Mathf.Min(caret, result.Length);
        }
    }

    private void OnGeneratePressed()
    {
        if (_busy) return;
        if (!Seed.TryParse(_seedInput.Text, out var seed, out var error))
        {
            SetStatus(error, isError: true);
            return;
        }
        _ = GenerateAsync(seed);
    }

    private void OnRandomPressed()
    {
        if (_busy) return;
        var seed = Seed.NewRandom();
        _seedInput.Text = seed.Text;
        _ = GenerateAsync(seed);
    }

    private void SetStatus(string text, bool isError = false)
    {
        _status.Text = text;
        _status.Modulate = isError ? new Color(1f, 0.55f, 0.5f) : Colors.White;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _generateButton.Disabled = busy;
        _randomButton.Disabled = busy;
        _seedInput.Editable = !busy;
    }

    // ------------------------------------------------------------------ world

    private async Task GenerateAsync(Seed seed)
    {
        SetBusy(true);
        try
        {
            await GenerateInnerAsync(seed);
        }
        catch (System.Exception ex)
        {
            GD.PushError($"World generation failed for seed {seed.Text}: {ex}");
            SetStatus($"Something went wrong generating {seed.Text}: {ex.Message}", isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task GenerateInnerAsync(Seed seed)
    {
        _villages.Clear();
        SetStatus($"Generating world {seed.Text}...");

        var sw = Stopwatch.StartNew();
        // Heavy work off the main thread; the await resumes on the main thread.
        var map = await Task.Run(() => WorldGenerator.Generate(seed));
        long genMs = sw.ElapsedMilliseconds;

        SetStatus($"Building 3D world {seed.Text}...");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        sw.Restart();
        _map = null; // if building fails halfway, don't keep clicking on the old world
        _worldView.Build(map);
        long buildMs = sw.ElapsedMilliseconds;
        _map = map;
        _cameraRig.Map = map;
        _cameraRig.Focus(new Vector3(map.Extent / 2, 0, map.Extent / 2), 7000f);

        GD.Print($"World {seed.Text}: generated in {genMs} ms, built in {buildMs} ms, land {map.LandAreaKm2:0.0} km2, {map.Resources.Count} resource clusters");
        SetStatus($"World {seed.Text} — {map.LandAreaKm2:0.0} km² of land. Click a spot to found your village.");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb) return;

        // Clicking the world takes keyboard focus away from the seed field.
        if (mb.Pressed) GetViewport().GuiReleaseFocus();
        if (mb.ButtonIndex != MouseButton.Left) return;

        // Only treat it as a click if the press also started in the world and the mouse didn't travel.
        if (mb.Pressed)
        {
            _mouseDownPosition = mb.Position;
            _leftPressedInWorld = true;
            return;
        }
        if (!_leftPressedInWorld) return;
        _leftPressedInWorld = false;
        if (mb.Position.DistanceTo(_mouseDownPosition) > 6f) return;
        if (_busy || _map == null) return;

        var camera = _cameraRig.Camera;
        var origin = camera.ProjectRayOrigin(mb.Position);
        var direction = camera.ProjectRayNormal(mb.Position);
        if (!_map.Raycast(origin.X, origin.Y, origin.Z, direction.X, direction.Y, direction.Z, 40000f,
                out float hx, out _, out float hz))
            return;
        if (!_map.TryGetCell(hx, hz, out int cx, out int cz))
            return;

        TryPlacePlayerVillage(cx, cz);
    }

    private void TryPlacePlayerVillage(int cx, int cz)
    {
        if (_map == null) return;

        // Only the terrain rule is checked here: the bot always re-picks its site after the player
        // (re)places, so the bot never blocks the player.
        if (!VillagePlacement.CheckSite(_map, cx, cz, new List<VillageSite>(), out var reason))
        {
            SetStatus($"Can't build here: {reason}", isError: true);
            return;
        }

        _villages.Clear();
        _villages.Add(new VillageSite(VillagePlacement.PlayerId, PlayerVillageName, cx, cz));

        var bot = VillagePlacement.ChooseBotSite(_map, _villages, BotId, BotVillageName);
        if (bot != null) _villages.Add(bot);

        _worldView.ShowVillages(_villages);

        if (bot == null)
        {
            SetStatus("Village founded. The rival found no suitable site left.");
        }
        else
        {
            double km = VillagePlacement.Distance(_map, cx, cz, bot.CellX, bot.CellZ) / 1000.0;
            SetStatus($"Village founded. The rival settled {km:0.0} km away. Click elsewhere to move your village.");
        }
    }

    // ------------------------------------------------------------------ environment

    private void ConfigureEnvironment()
    {
        var env = GetNode<WorldEnvironment>("WorldEnvironment").Environment;
        env.FogEnabled = true;
        env.FogLightColor = new Color(0.72f, 0.80f, 0.88f);
        env.FogDensity = 0.00007f;
        env.FogSkyAffect = 0.25f;
        env.SsaoEnabled = true;

        var sun = GetNode<DirectionalLight3D>("Sun");
        sun.DirectionalShadowMaxDistance = 2500f;
    }
}
