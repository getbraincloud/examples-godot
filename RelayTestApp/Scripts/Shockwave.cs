using Godot;

/// <summary>
/// Expanding filled-circle impact effect drawn at a shockwave/paint location. Mirrors the
/// GDScript and C++ RelayTestApp shockwave: the circle grows from 0 to 64px (ease-out) over
/// one second while fading out, then frees itself. Created entirely in code (no scene file).
/// </summary>
public partial class Shockwave : Node2D
{
	private const float Lifetime = 1.0f;
	private const float MaxRadius = 64.0f;

	private Color _colour = Colors.White;
	private float _elapsed = 0.0f;

	public void Setup(Color colour)
	{
		_colour = colour;
	}

	public override void _Process(double delta)
	{
		_elapsed += (float)delta;
		if (_elapsed >= Lifetime)
		{
			QueueFree();
			return;
		}
		QueueRedraw();
	}

	public override void _Draw()
	{
		float t = _elapsed / Lifetime;
		float easeT = 1.0f - (1.0f - t) * (1.0f - t); // ease-out
		float radius = MaxRadius * easeT;
		float alpha = 1.0f - t;
		DrawCircle(Vector2.Zero, radius, new Color(_colour.R, _colour.G, _colour.B, alpha));
	}
}
