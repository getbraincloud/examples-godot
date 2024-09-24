using BrainCloud;
using Godot;
using Godot.Collections;
using GodotPlugins.Game;
using System;
using System.Diagnostics;
using System.Runtime.Intrinsics.X86;

public partial class BCManager : Node
{
    [Signal]
    public delegate void BrainCloudLogReceivedEventHandler(string brainCloudLog);
    [Signal]
    public delegate void AuthenticationSuccessEventHandler();
    [Signal]
    public delegate void AuthenticationFailureEventHandler();
    [Signal]
    public delegate void LogOutSuccessEventHandler();
    [Signal]
    public delegate void LogOutFailureEventHandler();
    [Signal]
    public delegate void IdentityAttachSuccessEventHandler();
    [Signal]
    public delegate void IdentityAttachFailureEventHandler();
    [Signal]
    public delegate void IdentityMergeSuccessEventHandler();
    [Signal]
    public delegate void IdentityMergeFailureEventHandler();
    [Signal]
    public delegate void EntityReceivedEventHandler(Dictionary entity);
    [Signal]
    public delegate void CreateEntitySuccessEventHandler();
    [Signal]
    public delegate void UpdateEntitySuccessEventHandler();
    [Signal]
    public delegate void DeleteEntitySuccessEventHandler();
    [Signal]
    public delegate void ReceivedStatisticsEventHandler(Dictionary statistics);
    [Signal]
    public delegate void ReconnectSuccessEventHandler();
    [Signal]
    public delegate void ReconnectFailEventHandler();

    private BrainCloudWrapper _brainCloudWrapper;

    // TODO:  replace these values with the IDs from your app
    //private string _url = "";
    //private string _secretKey = "";
    //private string _appId = "";
    //private string _version = "";

    public override void _Ready()
    {
        _brainCloudWrapper = new BrainCloudWrapper();

        // TODO:  replace these values with the IDs from your app
        //_brainCloudWrapper.Init(_url, _secretKey, _appId, _version);

        _brainCloudWrapper.Client.EnableLogging(true);

        GD.Print("brainCloud client version: " + _brainCloudWrapper.Client.BrainCloudClientVersion);
    }

    public override void _Process(double delta)
    {
        _brainCloudWrapper.RunCallbacks();
    }

    // Authentication Service

    public bool IsAuthenticated()
    {
        return _brainCloudWrapper.Client.Authenticated;
    }

    public void RequestAnonymousAuthentication()
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {           
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("[AuthenticateAnonymous Success]\n{0}", Json.Stringify(response)));
            EmitSignal(SignalName.AuthenticationSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("[AuthenticateAnonymous Failed] {0}  {1}  {2}", status, code, error));
            EmitSignal(SignalName.AuthenticationFailure);
        };

        _brainCloudWrapper.AuthenticateAnonymous(successCallback, failureCallback);
    }

    public void RequestUniversalAuthentication(string userId, string password)
    {
        bool forceCreate = true;
        SuccessCallback successCallback = (response, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("[AuthenticateUniversal Success]\n{0}", Json.Stringify(response)));
            EmitSignal(SignalName.AuthenticationSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("[AuthenticateUniversal Failed] {0}  {1}  {2}", status, code, error));
            EmitSignal(SignalName.AuthenticationFailure);
        };

        _brainCloudWrapper.AuthenticateUniversal(userId, password, forceCreate, successCallback, failureCallback);
    }

    public void RequestEmailPasswordAuthentication(string email, string password)
    {
        bool forceCreate = true;

        SuccessCallback successCallback = (response, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("[AuthenticateEmailPassword Success]\n{0}", Json.Stringify(response)));
            EmitSignal(SignalName.AuthenticationSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("[AuthenticateEmailPassword Failed] {0}  {1}  {2}", status, code, error));
            EmitSignal(SignalName.AuthenticationFailure);
        };
        _brainCloudWrapper.AuthenticateEmailPassword(email, password, forceCreate, successCallback, failureCallback);
    }

    public void RequestReconnectAuthentication()
    {
        if (_brainCloudWrapper.CanReconnect())
        {
            GD.Print("Reconnecting...");
            SuccessCallback successCallback = (response, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("[Reconnect Success]\n{0}", Json.Stringify(response)));
            EmitSignal(SignalName.ReconnectSuccess);
        };
            FailureCallback failureCallback = (status, code, error, cbObject) =>
            {
                EmitSignal(SignalName.BrainCloudLogReceived, string.Format("[Reconnect Failed] {0}  {1}  {2}", status, code, error));
                EmitSignal(SignalName.ReconnectFail);
            };
            _brainCloudWrapper.Reconnect(successCallback, failureCallback);
        }

        else{
            EmitSignal(SignalName.ReconnectFail);
        }
    }

    // Player State Service

    public void LogOut(bool forgetUser)
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Logout Success\n{0}", Json.Stringify(response)));
            EmitSignal(SignalName.LogOutSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Logout Failed | {0}  {1}  {2}", status, code, error));
            EmitSignal(SignalName.LogOutFailure);
        };

        _brainCloudWrapper.Logout(forgetUser, successCallback, failureCallback);
    }

    // Identity Service

    public void AttachEmailIdentity(string email, string password)
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Attach Email Identity Success\n{0}", Json.Stringify(response)));
            EmitSignal(SignalName.IdentityAttachSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Attach Email Identity Failed | {0}  {1}  {2}", status, code, error));
            EmitSignal(SignalName.IdentityAttachFailure);
        };

        _brainCloudWrapper.IdentityService.AttachEmailIdentity(email, password, successCallback, failureCallback);
    }

    public void MergeEmailIdentity(string email, string password)
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Merge Email Identity Success\n{0}", Json.Stringify(response)));
            EmitSignal(SignalName.IdentityMergeSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Merge Email Identity Failed | {0}  {1}  {2}", status, code, error));
            EmitSignal(SignalName.IdentityMergeFailure);
        };

        _brainCloudWrapper.IdentityService.MergeEmailIdentity(email, password, successCallback, failureCallback);
    }

    public void AttachUniversalIdentity(string universalID, string password)
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Attach Universal Identity Success\n{0}", Json.Stringify(response)));
            EmitSignal(SignalName.IdentityAttachSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Attach Universal Identity Failed | {0}  {1}  {2}", status, code, error));
            EmitSignal(SignalName.IdentityAttachFailure);
        };

        _brainCloudWrapper.IdentityService.AttachUniversalIdentity(universalID, password, successCallback, failureCallback);
    }

    public void MergeUniversalIdentity(string universalID, string password)
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Merge Universal Identity Success\n{0}", Json.Stringify(response)));
            EmitSignal(SignalName.IdentityMergeSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Merge Universal Identity Failed | {0}  {1}  {2}", status, code, error));
            EmitSignal(SignalName.IdentityMergeFailure);
        };

        _brainCloudWrapper.IdentityService.MergeUniversalIdentity(universalID, password, successCallback, failureCallback);
    }

    // Entity Service

    public void GetPage()
    {
        string context = "{\"pagination\":{\"rowsPerPage\":50,\"pageNumber\":1},\"searchCriteria\":{\"entityType\":\"user\"},\"sortCriteria\":{\"createdAt\":1,\"updatedAt\":-1}}";
        
        SuccessCallback successCallback = (response, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Entity.GetPage Success\n{0}", Json.Stringify(response)));

            Dictionary responseJson = (Dictionary)Json.ParseString(response);
            Dictionary data = (Dictionary)responseJson["data"];
            Dictionary results = (Dictionary)data["results"];
            Godot.Collections.Array items = (Godot.Collections.Array)results["items"];

            if (items.Count > 0)
            {
                GD.Print("Found entity");
                Dictionary entity = (Dictionary)items[0];
                EmitSignal(SignalName.EntityReceived, entity);
            }
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Entity.GetPage Failed | {0}  {1}  {2}", status, code, error));
        };

        _brainCloudWrapper.EntityService.GetPage(context, successCallback, failureCallback);
    }

    public void CreateEntity(string entityType, string jsonEntityData, string jsonEntityAcl)
    {        
        SuccessCallback successCallback = (response, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Create Entity Success\n{0}", Json.Stringify(response)));
            EmitSignal(SignalName.CreateEntitySuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Create Entity Failed | {0}  {1}  {2}", status, code, error));
        };

        _brainCloudWrapper.EntityService.CreateEntity(entityType, jsonEntityData, jsonEntityAcl, successCallback, failureCallback);
    }

    public void UpdateEntity(string entityId, string entityType, string jsonEntityData, string jsonEntityAcl)
    {
        // The version of the entity to delete. Use - 1 to indicate the newest version
        int version = -1;

        SuccessCallback successCallback = (response, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Update Entity Success\n{0}", Json.Stringify(response)));
            EmitSignal(SignalName.UpdateEntitySuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Update Entity Failed | {0}  {1}  {2}", status, code, error));
        };

        _brainCloudWrapper.EntityService.UpdateEntity(entityId, entityType, jsonEntityData, jsonEntityAcl, version, successCallback, failureCallback);
    }

    public void DeleteEntity(string entityId)
    {
        // The version of the entity to delete. Use - 1 to indicate the newest version
        int version = -1;

        SuccessCallback successCallback = (response, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Delete Entity Success\n{0}", Json.Stringify(response)));
            EmitSignal(SignalName.DeleteEntitySuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            EmitSignal(SignalName.BrainCloudLogReceived, string.Format("Delete Entity Failed | {0}  {1}  {2}", status, code, error));
        };

        _brainCloudWrapper.EntityService.DeleteEntity(entityId, version, successCallback, failureCallback);
    }

    // Script Service

    public void RunScript(string scriptName, string scriptData)
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("RunScript Success | {0}", response));
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("RunScript Failed | {0}  {1}  {2}", status, code, error));
        };

        _brainCloudWrapper.ScriptService.RunScript(scriptName, scriptData, successCallback, failureCallback);
    }

    // Global Statistics Service

    public void ReadAllGlobalStats()
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("Read All Global Stats Success | {0}", response));

            Dictionary responseJson = (Dictionary)Json.ParseString(response);
            Dictionary data = (Dictionary)responseJson["data"];
            Dictionary statistics = (Dictionary)data["statistics"];

            EmitSignal(SignalName.ReceivedStatistics, statistics);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("Read All Global Stats Failed | {0}  {1}  {2}", status, code, error));
        };

        _brainCloudWrapper.GlobalStatisticsService.ReadAllGlobalStats(successCallback, failureCallback);
    }

    public void IncrementGlobalStatistics(string globalStatName, int incrementAmount)
    {
        Dictionary statisticsJson = new Dictionary();
        statisticsJson.Add(globalStatName, incrementAmount);

        string statistics = Json.Stringify(statisticsJson);

        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("Increment Global Statistics Success | {0}", response));

            ReadAllGlobalStats();
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("Increment Global Statistics Failed | {0}  {1}  {2}", status, code, error));
        };

        _brainCloudWrapper.GlobalStatisticsService.IncrementGlobalStats(statistics, successCallback, failureCallback);
    }

    // Player Statistics Service

    public void ReadAllPlayerStats()
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("Read All Player Stats Success | {0}", response));

            Dictionary responseJson = (Dictionary)Json.ParseString(response);
            Dictionary data = (Dictionary)responseJson["data"];
            Dictionary statistics = (Dictionary)data["statistics"];

            EmitSignal(SignalName.ReceivedStatistics, statistics);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("Read All Global Stats Failed | {0}  {1}  {2}", status, code, error));
        };

        _brainCloudWrapper.PlayerStatisticsService.ReadAllUserStats(successCallback, failureCallback);
    }

    public void IncrementPlayerStatistics(string playerStatName, int incrementAmount)
    {
        Dictionary statisticsJson = new Dictionary();
        statisticsJson.Add(playerStatName, incrementAmount);

        string statistics = Json.Stringify(statisticsJson);

        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("Increment Player Statistics Success | {0}", response));

            ReadAllPlayerStats();
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("Increment Player Statistics Failed | {0}  {1}  {2}", status, code, error));
        };

        _brainCloudWrapper.PlayerStatisticsService.IncrementUserStats(statistics, successCallback, failureCallback);
    }

    public void IncrementExperiencePoints(int xpValue)
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("Increment Experience Points Success | {0}", response));
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("Increment Experience Points Failed | {0}  {1}  {2}", status, code, error));
        };

        _brainCloudWrapper.PlayerStatisticsService.IncrementExperiencePoints(xpValue, successCallback, failureCallback);
    }

}
