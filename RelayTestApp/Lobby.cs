using Godot;
using System;
using System.Collections.Generic;

public partial class Lobby : Node
{
    public string LobbyID;
    public string OwnerID;
    public List<UserInfo> Members = new List<UserInfo>();
    public Lobby(Dictionary<string, object> lobbyJson, string in_lobbyId)
    {
        LobbyID = in_lobbyId;
        OwnerID = FormatOwnerID(lobbyJson["ownerCxId"] as string);

        var jsonMembers = lobbyJson["members"] as Dictionary<string, object>[];
        if (jsonMembers == null)
        {
            return;
        }
        GD.Print("members length = " + jsonMembers.Length);
        for (int i = 0; i < jsonMembers.Length; ++i)
        {
            GD.Print("Creating new user");
            Dictionary<string, object> jsonMember = jsonMembers[i];
            var user = new UserInfo(jsonMember);
            if (user.ID == GameManager.Instance.CurrentUserInfo.ID)
            {
                GameManager.Instance.CurrentUserInfo = user;
            }
            user.IsAlive = true;
            if (user.ID.Equals(OwnerID))
            {
                Dictionary<string, object> extra = jsonMember["extra"] as Dictionary<string, object>;
                

                if (extra.ContainsKey("presentSinceStart"))
                {
                    user.PresentSinceStart = (bool)extra["presentSinceStart"];
                }

                user.IsHost = true;
            }
            Members.Add(user);
        }
    }

    private string FormatOwnerID(string id)
    {
        string[] splits = id.Split(':');
        return splits[1];
    }

    public string FormatCxIdToProfileId(string id)
    {
        return FormatOwnerID(id);
    }

    public string ReassignOwnerID(string id)
    {
        OwnerID = FormatOwnerID(id);

        return OwnerID;
    }
}
