using Godot;
using System;
using System.Threading.Tasks;

public partial class Splatter : Sprite2D
{
	private Vector2 _position;
	private Color _colour;

	private float lifespan = 10.0f;
	private float splatterDuration = 0.3f;
	private float overSplat = 0.4f;
	private float targetScale = 0.5f;
	private float fadeDuration = 10f;

	private Random random;

	public override void _Ready()
	{
		targetScale = Scale.X;
		random = new Random();
		Rotate(RandomRange(0, Mathf.Tau));
		AppearAnimation();
	}

	public void SetColour(Color newColour)
	{
		Modulate = AlterColour(newColour);
	}

	public void SetLifespan(float newLifespan)
    {
        lifespan = newLifespan;
    }

    public void SetAnimationDurations(float appearDuration, float disappearDuration)
    {
        splatterDuration = appearDuration;
        fadeDuration = disappearDuration;
    }

	private async void AppearAnimation()
	{
		float age = 0.0f;
		while(age <= splatterDuration)
		{
			age += (float)GetPhysicsProcessDeltaTime();
			Scale = Vector2.One * SplatSizeOverTime(age, splatterDuration, overSplat) * targetScale;
			Modulate = SetAlpha(Modulate, Mathf.Max(age / splatterDuration, 0.25f));
			await Task.Delay(10);
		}
		Scale = Vector2.One * targetScale;
		Modulate = SetAlpha(Modulate, 1);

		if(lifespan >= 0)
		{
			await Task.Delay((int)(lifespan * 1000));
			DisappearAnimation();
		}
	}

	private async void DisappearAnimation()
	{
		float age = 0.0f;
		while(age <= fadeDuration)
		{
			age += (float)GetPhysicsProcessDeltaTime();
			Modulate = SetAlpha(Modulate, 1.0f - Mathf.Pow(age / fadeDuration, 2));
			await Task.Delay(10);
		}
		QueueFree();
	}

	private Color SetAlpha(Color oldColor, float newAlpha)
	{
		return new Color(oldColor.R, oldColor.G, oldColor.B, newAlpha);
	}

	private Color AlterColour(Color oldColor)
	{
		float alt = 0.07f;
		return new Color(
			oldColor.R * (1 + RandomRange(-alt, alt)), 
			oldColor.G * (1 + RandomRange(-alt, alt)), 
			oldColor.B * (1 + RandomRange(-alt, alt)),
			oldColor.A
			);
	}

	private float SplatSizeOverTime(float t, float a, float b)
	{
		float grow = (1 + b) * t / a;
		float shrink = -(((1 + b) * t) - ((2 + b) * a)) / a;
		return Mathf.Min(grow, shrink);
	}

	private float RandomRange(float min, float max)
	{
		return (random.NextSingle() * (max - min)) + min;
	}
}
