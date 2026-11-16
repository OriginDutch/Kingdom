using System;
using Godot;
using Kingdom.Core.World;

namespace Kingdom.Game;

/// <summary>
/// RTS camera: orbits a pivot point on the ground.
///  - WASD / arrow keys: move (Shift = 3x faster)
///  - Mouse wheel: zoom
///  - Right mouse drag: rotate and tilt
///  - Q / E: rotate
/// Keyboard is ignored while a text field has focus.
/// </summary>
public partial class RtsCamera : Node3D
{
    public const float MinDistance = 40f;
    public const float MaxDistance = 9000f;

    public Camera3D Camera { get; private set; } = null!;
    public WorldMap? Map { get; set; }

    private Vector3 _pivot;
    private float _yaw = 0.6f;        // radians around Y
    private float _pitch = 0.95f;     // radians above the horizon
    private float _distance = 6000f;
    private float _targetDistance = 6000f;

    public override void _Ready()
    {
        Camera = new Camera3D { Near = 1.0f, Far = 45000f, Fov = 55f };
        AddChild(Camera);
        Camera.MakeCurrent();
        UpdateCamera();
    }

    /// <summary>Jumps to look at a world position from a given distance.</summary>
    public void Focus(Vector3 worldPosition, float distance)
    {
        _pivot = worldPosition;
        _distance = _targetDistance = Mathf.Clamp(distance, MinDistance, MaxDistance);
        UpdateCamera();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed:
                _targetDistance = Mathf.Max(MinDistance, _targetDistance * 0.85f);
                break;
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed:
                _targetDistance = Mathf.Min(MaxDistance, _targetDistance * 1.18f);
                break;
            // Read the button state from the motion event itself, so a release swallowed by the UI can't leave it stuck.
            case InputEventMouseMotion motion when (motion.ButtonMask & MouseButtonMask.Right) != 0:
                _yaw -= motion.Relative.X * 0.005f;
                _pitch = Mathf.Clamp(_pitch + motion.Relative.Y * 0.004f, 0.25f, 1.45f);
                break;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bool typing = GetViewport().GuiGetFocusOwner() is LineEdit;

        if (!typing)
        {
            var input = Vector2.Zero;
            if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) input.Y -= 1;
            if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) input.Y += 1;
            if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) input.X -= 1;
            if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) input.X += 1;
            if (input != Vector2.Zero)
            {
                input = input.Normalized();
                float speed = _distance * 0.9f * (Input.IsKeyPressed(Key.Shift) ? 3f : 1f);
                var forward = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
                var right = new Vector3(Mathf.Cos(_yaw), 0, -Mathf.Sin(_yaw));
                _pivot += (right * input.X - forward * input.Y) * speed * dt;
            }
            if (Input.IsKeyPressed(Key.Q)) _yaw += 1.6f * dt;
            if (Input.IsKeyPressed(Key.E)) _yaw -= 1.6f * dt;
        }

        // Smooth zoom.
        _distance = Mathf.Lerp(_distance, _targetDistance, 1f - Mathf.Exp(-10f * dt));
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        if (Camera == null) return;
        if (Map != null)
        {
            _pivot.X = Mathf.Clamp(_pivot.X, 0, Map.Extent);
            _pivot.Z = Mathf.Clamp(_pivot.Z, 0, Map.Extent);
            _pivot.Y = Math.Max(0f, Map.SurfaceHeightAt(_pivot.X, _pivot.Z));
        }

        var offset = new Vector3(
            Mathf.Sin(_yaw) * Mathf.Cos(_pitch),
            Mathf.Sin(_pitch),
            Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _distance;
        var position = _pivot + offset;

        // Never dip below the ground.
        if (Map != null)
        {
            float ground = Math.Max(0f, Map.SurfaceHeightAt(position.X, position.Z));
            if (position.Y < ground + 8f) position.Y = ground + 8f;
        }

        Camera.GlobalPosition = position;
        Camera.LookAt(_pivot, Vector3.Up);
    }
}
