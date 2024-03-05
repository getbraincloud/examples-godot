using Godot;
using System;
using System.Collections;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

public partial class Shockwave : Sprite2D
{
    private float _timeMultiplier = 4;

    public override void _Ready()
    {
        StartAnimating();
    }

    private void StartAnimating()
    {
        Modulate = new Color
        (
            Modulate.R,
            Modulate.G,
            Modulate.B,
            255 // resetting alpha
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
        
        QueueFree();
    }
}
