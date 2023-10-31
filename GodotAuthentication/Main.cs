using Godot;
using System;

public partial class Main : Node
{
    private Node currentScene;
    private AuthenticationMenu _authenticationMenu;
    private MainMenu _mainMenu;

    public override void _Ready()
    {        
        GoToAuthenticationMenu();
    }

    /// <summary>
    /// Switch between the main app screen upon authenticating, and the login screen before authenticating / after logging out.
    /// </summary>
    /// <param name="newScene">Node holding the instantiated scene to add to the scenetree</param>
    private void ChangeScene(Node newScene)
    {
        if (currentScene != null)
        {
            currentScene.QueueFree();
        }

        currentScene = newScene;
        AddChild(currentScene);
    }

    private void GoToAuthenticationMenu()
    {
        var authenticationMenuScene = GD.Load<PackedScene>("res://authentication_menu.tscn");

        _authenticationMenu = (AuthenticationMenu)authenticationMenuScene.Instantiate();

        ChangeScene(_authenticationMenu);

        _authenticationMenu.Connect(AuthenticationMenu.SignalName.Authenticated, new Callable(this, MethodName.OnAuthenticated));
    }

    private void GoToMainMenu()
    {
        var mainMenuScene = GD.Load<PackedScene>("res://main_menu.tscn");

        _mainMenu = (MainMenu)mainMenuScene.Instantiate();

        ChangeScene(_mainMenu);

        _mainMenu.Connect(MainMenu.SignalName.LoggedOut, new Callable(this, MethodName.OnLoggedOut));
    }

    private void OnAuthenticated()
    {
        GoToMainMenu();
    }

    private void OnLoggedOut()
    {
        GoToAuthenticationMenu();
    }
}
