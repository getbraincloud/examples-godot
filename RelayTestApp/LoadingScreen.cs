using Godot;
using System;

public partial class LoadingScreen : Label
{
    public void SetLoadingMessage(string message)
    {
        Text = message;
    }
}
