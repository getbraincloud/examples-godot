using BrainCloud;
using Godot;
using Godot.Collections;
using System;

public partial class Script : Control
{
    private OptionButton _scriptOptions;
    private TextEdit _scriptDataField;
    private Button _runButton;

    private BCManager _brainCloud;

    private Dictionary _scriptData;

    public override void _Ready()
    {
        _scriptOptions = GetNode<OptionButton>("VBoxContainer/ScriptOptions");
        _scriptDataField = GetNode<TextEdit>("VBoxContainer/ScrollContainer/ScriptData");
        _runButton = GetNode<Button>("VBoxContainer/RunButton");

        _brainCloud = GetNode<BCManager>("/root/BCManager");

        _scriptData = new Dictionary();

        // Populate ScriptOptions dropdown options
        _scriptOptions.AddItem("HelloWorld");
        _scriptOptions.AddItem("IncrementGlobalStat");
        _scriptOptions.AddItem("IncrementPlayerStat");

        // TODO:  HelloWorld script should be set by default
        _scriptData.Add("name", "John Smith");
        _scriptData.Add("age", 21);

        _scriptDataField.Text = Json.Stringify(_scriptData);

        _scriptOptions.Connect(OptionButton.SignalName.ItemSelected, new Callable(this, MethodName.OnScriptSelected));
        _runButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnRunButtonPressed));
    }

    private void OnScriptSelected(int index)
    {
        GD.Print("Selected");
        _scriptDataField.Clear();
        _scriptData.Clear();

        switch (index)
        {
            case 0:
                // TODO:  HelloWorld Script
                _scriptData.Add("name", "John Smith");
                _scriptData.Add("age", 21);
                break;
            case 1:
                // TODO:  IncrementGlobalStat Script
                _scriptData.Add("globalStat", "PLAYER_COUNT");
                _scriptData.Add("incrementAmount", 1);
                break;
            case 2:
                // TODO:  IncrementPlayerStat Script
                _scriptData.Add("playerStat", "experiencePoints");
                _scriptData.Add("incrementAmount", 1);
                break;
            default:
                GD.Print("Invalid script selected");
                break;
        }

        _scriptDataField.Text = _scriptData.ToString();
    }

    private void OnRunButtonPressed()
    {
        // TODO:  Make scripts
        var selectedScript = _scriptOptions.GetItemText(_scriptOptions.GetSelectedId());
        var scriptData = _scriptDataField.Text;

        _brainCloud.RunScript(selectedScript, scriptData);
    }
}
