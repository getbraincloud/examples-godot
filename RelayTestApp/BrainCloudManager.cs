using BrainCloud;
using Godot;
using System;

public partial class BrainCloudManager : Node
{
    BrainCloudWrapper _bc = null;

    string url = "https://api.internal.braincloudservers.com/dispatcherv2";
    string secretKey = "a754a2c0-72d9-46ce-9fdf-18e9c19a556c";
    string appId = "23649";
    string version = "1.0.0";

    public override void _Ready()
    {
        _bc = new BrainCloudWrapper();
        _bc.Init(url, secretKey, appId, version);
        _bc.Client.EnableLogging(true);

        GD.Print("brainCloud client version: " + _bc.Client.BrainCloudClientVersion);
    }

    public override void _Process(double delta)
    {
        _bc.Update();
    }
}
