using BrainCloud;
using Godot;
using System;
using System.Collections.Generic;

public partial class GameManager : Node
{
    public enum GameColors { 
        Black,
        Purple,
        Grey,
        Orange,
        Blue,
        Green,
        Yellow,
        Cyan,
        White
    }
    public enum GameMode { FreeForAll, Team }

    // Team codes for Free for all = all and team specific is alpha and beta
    public enum TeamCodes { all, alpha, beta }

    public Lobby CurrentLobby;
    public Server CurrentServer;

    private GameMode _gameMode = GameMode.FreeForAll;

    public GameMode Mode
    {
        get => _gameMode;
        set => _gameMode = value;
    }

    // Local User Info
    private UserInfo _currentUserInfo;
    public UserInfo CurrentUserInfo
    {
        get => _currentUserInfo;
        set => _currentUserInfo = value;
    }

    public bool IsReady;

    private static GameManager _instance;
    public static GameManager Instance => _instance;

    private BCManager _bcManager;

    internal RelayConnectionType Protocol { get; set; }

    public override void _Ready()
    {
        if (_instance == null)
        {
            _instance = this;
        }

        _bcManager = GetNode<BCManager>("/root/BCManager");

        _currentUserInfo = new UserInfo();
    }

    public bool IsLocalUserHost()
    {
        return CurrentLobby.OwnerID == CurrentUserInfo.ID;
    }

    public void UpdatePresentSinceStart()
    {
        _currentUserInfo.PresentSinceStart = true;
        // Send update to brainCloud
        Dictionary<string, object> extra = new Dictionary<string, object>();
        extra["colorIndex"] = (int)_currentUserInfo.UserGameColor;
        extra["presentSinceStart"] = _currentUserInfo.PresentSinceStart;

        _bcManager.UpdateReady(extra);
    }

    public static Color ReturnUserColor(GameColors newColor = GameColors.White)
    {
        switch (newColor)
        {
            case GameColors.Black:
                return new Color(0, 0, 0);
            case GameColors.Purple:
                return new Color(0.33f, 0.25f, 0.37f);
            case GameColors.Grey:
                return new Color(0.4f, 0.4f, 0.4f);
            case GameColors.Orange:
                return new Color(0.85f, 0.4f, 0.04f);
            case GameColors.Blue:
                return new Color(0.31f, 0.54f, 0.84f);
            case GameColors.Green:
                return new Color(0.39f, 0.72f, 0.39f);
            case GameColors.Yellow:
                return new Color(0.9f, 0.78f, 0.43f);
            case GameColors.Cyan:
                return new Color(0.86f, 0.96f, 1);
            default:
                return new Color(0, 0, 0);
        }
    }
}
