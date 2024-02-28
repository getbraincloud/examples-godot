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

    //Team codes for Free for all = all and team specific is alpha and beta
    public enum TeamCodes { all, alpha, beta }

    private GameMode _gameMode = GameMode.FreeForAll;

    public Lobby CurrentLobby;
    public Server CurrentServer;

    public GameMode Mode
    {
        get => _gameMode;
        set => _gameMode = value;
    }
    //Singleton Pattern
    private static GameManager _instance;
    public static GameManager Instance => _instance;

    //Local User Info
    private UserInfo _currentUserInfo;
    public UserInfo CurrentUserInfo
    {
        get => _currentUserInfo;
        set => _currentUserInfo = value;
    }

    public bool IsReady;

    private BCManager m_BCManager;

    internal RelayConnectionType Protocol { get; set; }

    public override void _Ready()
    {
        if (_instance == null)
        {
            _instance = this;
        }

        m_BCManager = GetNode<BCManager>("/root/BCManager");

        _currentUserInfo = new UserInfo();

        GD.Print("New user created. color: " + (int)_currentUserInfo.UserGameColor);
    }

    public void UpdatePresentSinceStart()
    {
        // TODO:  can this all just be in BCManager?
        _currentUserInfo.PresentSinceStart = true;
        //Send update to BC
        Dictionary<string, object> extra = new Dictionary<string, object>();
        extra["colorIndex"] = (int)_currentUserInfo.UserGameColor;
        extra["presentSinceStart"] = _currentUserInfo.PresentSinceStart;

        m_BCManager.UpdateReady(extra);
    }

    public static Color ReturnUserColor(GameColors newColor = GameColors.White)
    {
        switch (newColor)
        {
            case GameColors.Black:
                return new Color(1, 1, 1);
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
