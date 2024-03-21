using Godot;
using System;
using System.Collections.Generic;

public partial class UserInfo : Node
{
    public string Username;
    public string ID;
    public GameManager.GameColors UserGameColor;
    public string cxId;
    
    //Used to determine if user is in lobby or in match.
    public bool IsReady;

    public bool PresentSinceStart;

    //Is this user still connected
    public bool IsAlive;

    public bool IsHost;

    public bool AllowSendTo = true;

    public GameManager.TeamCodes Team;

    public Vector2 MousePosition;
    public List<Vector2> ShockwavePositions = new List<Vector2>();
    public List<GameManager.TeamCodes> ShockwaveTeamCodes = new List<GameManager.TeamCodes>();
    public List<GameManager.TeamCodes> InstigatorTeamCodes = new List<GameManager.TeamCodes>();

    public Cursor UserCursor;

    public UserInfo() { }

    public UserInfo(Dictionary<string, object> userJson)
    {
        cxId = userJson["cxId"] as string;
        ID = userJson["profileId"] as string;
        Username = userJson["name"] as string;
        IsReady = (bool)userJson["isReady"];
        string teamValue = userJson["team"] as string;
        Enum.TryParse(teamValue, out Team);
        
        if (GameManager.Instance.Mode == GameManager.GameMode.FreeForAll)
        {
            Dictionary<string, object> extra = userJson["extra"] as Dictionary<string, object>;
              
            int colorIndex = 0;
            if (extra != null && extra.ContainsKey("colorIndex"))
            {
                colorIndex = (int)extra["colorIndex"];
            }
            UserGameColor = (GameManager.GameColors)colorIndex;
        }
        else if (GameManager.Instance.Mode == GameManager.GameMode.Team)
        {
            if (Team == GameManager.TeamCodes.alpha)
            {
                UserGameColor = GameManager.GameColors.Blue;
            }
            else
            {
                UserGameColor = GameManager.GameColors.Orange;
            }
        }
        if (userJson.ContainsKey("presentSinceStart"))
        {
            PresentSinceStart = (bool)userJson["presentSinceStart"];
        }
    }
}
