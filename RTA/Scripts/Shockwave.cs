using Godot;
using System;
using System.Threading.Tasks;

public partial class Shockwave : Sprite2D
{
    private Vector2 _position;
    private Color _colour;

    private float _timeMultiplier = 4;

    public override void _Ready()
    {
        StartAnimating();
    }

    public void SetPosition(Vector2 position)
    {
        _position = position;
    }

    public Vector2 GetPosition()
    {
        return _position;
    }

    public void SetColour(Color colour)
    {
        _colour = colour;
    }

    public Color GetColour()
    {
        return _colour;
    }

    private void StartAnimating()
    {
        Modulate = new Color
        (
            Modulate.R,
            Modulate.G,
            Modulate.B,
            255
        );

        Ripple();
    }

    private async void Ripple()
    {
        double scale = 0;

        for (float i = 2; i >= 0; i -= (float)GetPhysicsProcessDeltaTime() * _timeMultiplier)
        {
            //Start fading..
            if (i <= 1)
            {
                Modulate = new Color
                (
                    Modulate.R,
                    Modulate.G,
                    Modulate.B,
                    i
                );
            }

            scale += 0.025f;
            Scale = new Vector2((float)scale, (float)scale);
            await Task.Delay(10);
        }

        await Task.Yield();

        GD.Print("Remove shockwave");
        QueueFree();
    }
}
