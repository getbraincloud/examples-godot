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
}
