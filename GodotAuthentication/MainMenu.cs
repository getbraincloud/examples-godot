using BrainCloud;
using Godot;
using System;

public partial class MainMenu : Control
{
    [Signal]
    public delegate void LoggedOutEventHandler();

    private ServiceMenu _serviceMenu;

    // Some services have not yet been setup but will be added soon.

    // private CustomEntity _customEntityService;
    private Entity _entityService;
    // private GlobalStatistics _globalStatisticsService;
    private Identity _identityService;
    // private PlayerStatistics _playerStatisticsService;
    // private Script _scriptService;
    // private VirtualCurrency _virtualCurrencyService;

    private Button _logOutButton;

    private Node _currentService;

    private BCManager _brainCloud;

    public override void _Ready()
    {
        _serviceMenu = GetNode<ServiceMenu>("ServiceMenu");
        _logOutButton = GetNode<Button>("ServiceMenu/VBoxContainer/LogOutButton");

        _brainCloud = GetNode<BCManager>("/root/BCManager");

        _serviceMenu.Connect(ServiceMenu.SignalName.ServiceButtonPressed, new Callable(this, MethodName.OnServiceButtonPressed));

        _logOutButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnLogOutButtonPressed));

        _brainCloud.Connect(BCManager.SignalName.LogOutSuccess, new Callable(this, MethodName.OnLogOutSuccess));
        _brainCloud.Connect(BCManager.SignalName.LogOutFailure, new Callable(this, MethodName.OnLogOutFail));
    }

    /// <summary>
    /// Loads the scene for whichever service was selected to explore.
    /// NOTE: some services have not yet been setup but will be added soon
    /// </summary>
    /// <param name="buttonIndex">Value representing which button was selected</param>
    private void OnServiceButtonPressed(int buttonIndex)
    {
        if(_currentService != null)
        {
            RemoveChild(GetChild(1));
            _currentService = null;
        }
        switch (buttonIndex)
        {
            case 0:
                var identityScene = GD.Load<PackedScene>("res://identity.tscn");
                _identityService = (Identity)identityScene.Instantiate();
                AddChild(_identityService);

                _currentService = _identityService;
                break;
            case 1:
                var entityScene = GD.Load<PackedScene>("res://entity.tscn");
                _entityService = (Entity)entityScene.Instantiate();
                AddChild(_entityService);

                _currentService = _entityService;
                break;
            case 2:
                break;
            case 3:
                break;
            case 4:
                break;
            case 5:
                break;
            case 6:
                break;
            default:
                GD.Print("Invalid service selected . . .");
                break;
        }
    }

    private void OnLogOutButtonPressed()
    {
        _brainCloud.LogOut();
    }

    private void OnLogOutSuccess()
    {
        EmitSignal(SignalName.LoggedOut);
    }

    private void OnLogOutFail()
    {
        GD.Print("Log Out Fail");
    }
}
