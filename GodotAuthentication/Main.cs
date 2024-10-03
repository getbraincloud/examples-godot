using BrainCloud;
using Godot;
using System;

public partial class Main : Node
{
    private BCManager _brainCloudManager;
    private AuthenticationMenu _authenticationService;
    private ServiceMenu _serviceMenu;
    private Identity _identityService;
    private Entity _entityService;
    private Script _scriptService;
    private Statistics _statisticsService;
    private XP _xpService;
    private VirtualCurrency _virtualCurrencyService;

    private Button _menuButton;
    private MarginContainer _serviceContainer;
    private Node _currentService;
    private Button _logOutButton;
    private Label _serviceInfo;
    private Label _serviceTitle;

    public override void _Ready()
    {
        _brainCloudManager = GetNode<BCManager>("/root/BCManager");

        _menuButton = GetNode<Button>("MarginContainer/VBoxContainer/Content/CurrentService/HBoxContainer/MenuButton");
        _logOutButton = GetNode<Button>("MarginContainer/VBoxContainer/Content/CurrentService/HBoxContainer/LogOutButton");
        _serviceContainer = GetNode<MarginContainer>("MarginContainer/VBoxContainer/Content/CurrentService/ServiceContainer");
        _serviceInfo = GetNode<Label>("MarginContainer/VBoxContainer/Content/VBoxContainer/ScrollContainer/ServiceInfo");
        _serviceTitle = GetNode<Label>("MarginContainer/VBoxContainer/Content/VBoxContainer/ServiceTitle");

        _brainCloudManager.Connect(BCManager.SignalName.LogOutSuccess, new Callable(this, MethodName.OnLoggedOut));
        _brainCloudManager.Connect(BCManager.SignalName.ReconnectSuccess, new Callable(this, MethodName.OnAuthenticated));
        _brainCloudManager.Connect(BCManager.SignalName.ReconnectFail, new Callable(this, MethodName.OnReconnectFail));

        _menuButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.LoadServiceMenu));
        _logOutButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnLogOutButtonPressed));

        _menuButton.Hide();
        _logOutButton.Hide();

        _brainCloudManager.RequestReconnectAuthentication();
    }

    public override void _Notification(int what)
    {
        if(what == NotificationWMCloseRequest)
		{
            _brainCloudManager.LogOut(false);
			GetTree().Quit(); // default behaviour
		}
    }

    /// <summary>
    /// Remove the currently active service from the scene and display the new one.
    /// </summary>
    /// <param name="newService">New scene to display</param>
    private void DisplayService(Node newService)
    {
        if(_currentService != null)
        {
            _currentService.QueueFree();
        }

        _currentService = newService;

        _serviceContainer.AddChild(newService);

        if (_brainCloudManager.IsAuthenticated())
        {
            _logOutButton.Show();
        }
    }

    private void LoadServiceMenu()
    {
        var serviceMenu = GD.Load<PackedScene>("res://service_menu.tscn");

        _serviceMenu = (ServiceMenu)serviceMenu.Instantiate();

        DisplayService(_serviceMenu);

        _serviceTitle.Text = "Explore brainCloud";
        _serviceInfo.Text = "Select a service to view more information about it.";
        _menuButton.Hide();

        _serviceMenu.Connect(ServiceMenu.SignalName.ServiceButtonPressed, new Callable(this, MethodName.OnServiceButtonPressed));
    }

    private void LoadAuthenticationService()
    {
        var authenticationMenu = GD.Load<PackedScene>("res://authentication_menu.tscn");

        _authenticationService = (AuthenticationMenu)authenticationMenu.Instantiate();

        DisplayService(_authenticationService);

        _serviceTitle.Text = "Authentication";
        _serviceInfo.Text =
            "There are many different ways to implement authenticaiton in your app. This example application demonstrates how to authenticate anonymously, or with a Universal ID / Email Address.\n" +
            "All authentication methods contain a 'forceCreate' flag. " +
            "If the flag is set to 'true' a new profile will be created if one does not already exist. " +
            "If 'false', the authentication will fail if it does not find an existing account.";
        _menuButton.Hide();
        _logOutButton.Hide();

        _authenticationService.Connect(AuthenticationMenu.SignalName.Authenticated, new Callable(this, MethodName.OnAuthenticated));
    }

    private void LoadIdentityService()
    {
        var identityService = GD.Load<PackedScene>("res://identity.tscn");

        _identityService = (Identity)identityService.Instantiate();

        DisplayService(_identityService);

        _serviceTitle.Text = "Identity";
        _serviceInfo.Text = 
            "The Identity APIs are used to attach additional identities to a profile. " +
            "These APIs are most commonly used for apps that allow users to begin anonymously, " +
            "and later encourage them to attach their Facebook ID for additional social features. " +
            "Please see the API Docs for examples and use cases.";
        _menuButton.Show();
    }

    private void LoadEntityService()
    {
        var entityService = GD.Load<PackedScene>("res://entity.tscn");

        _entityService = (Entity)entityService.Instantiate();

        DisplayService(_entityService);

        _serviceTitle.Text = "Entity";
        _serviceInfo.Text = 
            "User Entities (also called Player Entities) are full json objects (similar to Global Entities) " +
            "except they are private to a brainCloud user. " +
            "User entities can be as simple or complex as you would like." +
            "All User Entities: have a unique entityId, generated by brainCloud; " +
            "a developer-defined entityType (string); " +
            "a version to help control updates; " +
            "a json data section for developer-defined content; " +
            "and are by default private to a user, though can be set as shareable via ACL" +
            "User Entities are normally retrieved in bulk after a user logs in, and then updated in real-time as the user interacts with them. " +
            "Note that User Entities are by default private (only accessible by the owner), " +
            "but you can make them accessible to other users via the GetSharedEntityForProfileId and GetSharedEntitiesForProfileId APIs. " +
            "To do so, you must make them shareable to others via the ACL settings. " +
            "Please see the API Docs for examples and use cases.";
        _menuButton.Show();
    }

    private void LoadScriptService()
    {
        var scriptService = GD.Load<PackedScene>("res://script.tscn");

        _scriptService = (Script)scriptService.Instantiate();

        DisplayService(_scriptService);

        _serviceTitle.Text = "Script";
        _serviceInfo.Text = 
            "The Script service contains the RunScript method which is used to invoke Cloud Code scripts from the client. " +
            "Cloud Code scripts can be created, edited, and tested from the Edit Scripts page of the brainCloud portal. " +
            "Note - Scripts must have the Client 'Callable' flag set to 'true' to be callable from client apps. " +
            "Please see the API Docs for examples and use cases.";
        _menuButton.Show();
    }

    private void LoadGlobalStatisticsService()
    {
        var globalStatisticsService = GD.Load<PackedScene>("res://statistics.tscn");

        _statisticsService = (Statistics)globalStatisticsService.Instantiate();

        DisplayService(_statisticsService);

        _serviceTitle.Text = "Global Statistics";
        _serviceInfo.Text =
            "The Global Statistics service allows you to retrieve and update your app's predefined statistics defined on the " +
            "Global Statistics page of the brainCloud portal. " +
            "Global Statistics must be pre-defined from the Statistics Rules - Global Statistics page of the brainCloud portal. " +
            "If instead you need to create them dynamically at runtime, " +
            "you can enable the Generate App Statistic Rule Enabled setting on the Advanced Settings page of your app in the brainCloud portal. " +
            "Global (or App) Statistics are similar to User Stats, except that they are stored centrally across all of an app's users.";
        _menuButton.Show();
        _statisticsService.SetStatisticType(true);
    }

    private void LoadPlayerStatisticsService()
    {
        var playerStatisticsService = GD.Load<PackedScene>("res://statistics.tscn");

        _statisticsService = (Statistics)playerStatisticsService.Instantiate();

        DisplayService(_statisticsService);

        _serviceTitle.Text = "Player Statistics";
        _serviceInfo.Text =
            "The Global Statistics service allows you to retrieve and update your app's predefined statistics defined on the " +
            "Global Statistics page of the brainCloud portal. " +
            "Global Statistics must be pre-defined from the Statistics Rules - Global Statistics page of the brainCloud portal. " +
            "If instead you need to create them dynamically at runtime, " +
            "you can enable the Generate App Statistic Rule Enabled setting on the Advanced Settings page of your app in the brainCloud portal. " +
            "Global (or App) Statistics are similar to User Stats, except that they are stored centrally across all of an app's users.";
        _menuButton.Show();
        _statisticsService.SetStatisticType(false);
    }

    private void LoadXPService()
    {
        var xpService = GD.Load<PackedScene>("res://xp.tscn");

        _xpService = (XP)xpService.Instantiate();

        DisplayService(_xpService);

        _serviceTitle.Text = "XP Service";
        _serviceInfo.Text =
            "";
        _menuButton.Show();
    }

    private void LoadVirtualCurrencyService()
    {
        var virtualCurrencyService = GD.Load<PackedScene>("res://virtual_currency.tscn");

        _virtualCurrencyService = (VirtualCurrency)virtualCurrencyService.Instantiate();

        DisplayService(_virtualCurrencyService);

        _serviceTitle.Text = "Virtual Currency Service";
        _serviceInfo.Text =
            "";
        _menuButton.Show();
    }

    private void OnAuthenticated()
    {
        LoadServiceMenu();
    }

    private void OnReconnectFail(){
        LoadAuthenticationService();
    }

    private void OnLogOutButtonPressed()
    {
        _brainCloudManager.LogOut(true);
    }

    private void OnLoggedOut()
    {
        LoadAuthenticationService();
    }

    private void OnServiceButtonPressed(int buttonIndex)
    {
        switch(buttonIndex)
        {
            case 0:
                LoadIdentityService();
                break;
            case 1:
                LoadEntityService();
                break;
            case 2:
                LoadScriptService();
                break;
            case 3:
                LoadGlobalStatisticsService();
                break;
            case 4:
                LoadPlayerStatisticsService();
                break;
            case 5:
                LoadXPService();
                break;
            case 6:
                LoadVirtualCurrencyService();
                break;
            default:
                GD.Print("Invalid button pressed");
                break;
        }
    }
}
