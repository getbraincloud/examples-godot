using Godot;
using System;

public partial class XP : Control
{
    private LineEdit _levelField;
    private LineEdit _accruedField;
    private LineEdit _amountField;
    private Button _incrementButton;
    
    private BCManager _brainCloud;

    public override void _Ready()
    {
        _levelField = GetNode<LineEdit>("VBoxContainer/GridContainer/LevelLine");
        _accruedField = GetNode<LineEdit>("VBoxContainer/GridContainer/AccruedLine");
        _amountField = GetNode<LineEdit>("VBoxContainer/GridContainer/IncrementLine");
        _incrementButton = GetNode<Button>("VBoxContainer/GridContainer/IncrementButton");

        _brainCloud = GetNode<BCManager>("/root/BCManager");

        _incrementButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnIncrementXPButtonPressed));

        RefreshXPFields();
    }

    private void RefreshXPFields()
    {
        //
    }
    
    private void OnIncrementXPButtonPressed()
    {
           
        if(int.TryParse(_amountField.Text, out int incrementAmount) && incrementAmount > 0)
        {
            _brainCloud.IncrementExperiencePoints(incrementAmount);
        }
        else
        {
            GD.Print("Increment XP Error - invalid increment value");
        }
    }
}
