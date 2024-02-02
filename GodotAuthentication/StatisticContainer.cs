using Godot;
using System;
using System.Numerics;

public partial class StatisticContainer : HBoxContainer
{
    [Signal]
    public delegate void StatisticIncrementedEventHandler(string statisticName, int incrementAmount);

    private LineEdit _statisticNameField;
    private LineEdit _statisticValueField;
    private Button _incrementStatisticButton;

    private BCManager _brainCloud;

    public override void _Ready()
    {
        _statisticNameField = GetNode<LineEdit>("GlobalStatisticLine");
        _statisticValueField = GetNode<LineEdit>("HBoxContainer/StatValueLine");
        _incrementStatisticButton = GetNode<Button>("HBoxContainer/IncrementButton");

        _brainCloud = GetNode<BCManager>("/root/BCManager");

        _incrementStatisticButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnIncrementButtonPressed));
    }

    public void SetStatisticName(string statisticName)
    {
        _statisticNameField.Text = statisticName;
    }

    public void SetStatisticValue(int statisticValue)
    {
        _statisticValueField.Text = statisticValue.ToString();
    }

    private void OnIncrementButtonPressed()
    {
        string statisticName = _statisticNameField.Text;
        int incrementAmount = 1;    // default value for this demo

        EmitSignal(SignalName.StatisticIncremented, statisticName, incrementAmount);
    }
}
