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

    private BrainCloudWrapper _brainCloudWrapper;

    // TODO:  replace these values with the IDs from your app
    private string _url = "https://api.braincloudservers.com/dispatcherv2";
    private string _secretKey = "AppSecret";
    private string _appId = "AppID";
    private string _version = "1.0.0";

    public override void _Ready()
    {
        _brainCloudWrapper = new BrainCloudWrapper();

        _brainCloudWrapper.Init(_url, _secretKey, _appId, _version);

        _brainCloudWrapper.Client.EnableLogging(true);

        GD.Print("brainCloud client version: " + _brainCloudWrapper.Client.BrainCloudClientVersion);
    }

    public override void _Process(double delta)
    {
        _brainCloudWrapper.RunCallbacks();
    }

    // Authentication Service

    public void RequestAnonymousAuthentication()
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("[AuthenticateAnonymous Success] {0}", response));
            EmitSignal(SignalName.AuthenticationSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("[AuthenticateAnonymous Failed] {0}  {1}  {2}", status, code, error));
            EmitSignal(SignalName.AuthenticationFailure);
        };

        _brainCloudWrapper.AuthenticateAnonymous(successCallback, failureCallback);
    }

    public void RequestUniversalAuthentication(string userId, string password)
    {
        bool forceCreate = true;
        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("[AuthenticateUniversal Success] {0}", response));
            EmitSignal(SignalName.AuthenticationSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("[AuthenticateUniversal Failed] {0}  {1}  {2}", status, code, error));
            EmitSignal(SignalName.AuthenticationFailure);
        };

        _brainCloudWrapper.AuthenticateUniversal(userId, password, forceCreate, successCallback, failureCallback);
    }

    public void RequestEmailPasswordAuthentication(string email, string password)
    {
        bool forceCreate = true;

        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("[AuthenticateEmailPassword Success] {0}", response));
            EmitSignal(SignalName.AuthenticationSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("[AuthenticateEmailPassword Failed] {0}  {1}  {2}", status, code, error));
            EmitSignal(SignalName.AuthenticationFailure);
        };
        _brainCloudWrapper.AuthenticateEmailPassword(email, password, forceCreate, successCallback, failureCallback);
    }

    // Player State Service

    public void RequestLogOut()
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("Logout Success | {0}", response));
            EmitSignal(SignalName.LogOutSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("Logout Failed | {0}  {1}  {2}", status, code, error));
            EmitSignal(SignalName.LogOutFailure);
        };

        _brainCloudWrapper.PlayerStateService.Logout(successCallback, failureCallback);
    }

    // Identity Service

    public void RequestEmailIdentityAttach(string email, string password)
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("Attach Email Identity Success | {0}", response));
            EmitSignal(SignalName.IdentityAttachSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("Attach Email Identity Failed | {0}  {1}  {2}", status, code, error));
            EmitSignal(SignalName.IdentityAttachFailure);
        };

        _brainCloudWrapper.IdentityService.AttachEmailIdentity(email, password, successCallback, failureCallback);
    }

    public void RequestEmailIdentityMerge(string email, string password)
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("Merge Email Identity Success | {0}", response));
            EmitSignal(SignalName.IdentityMergeSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("Merge Email Identity Failed | {0}  {1}  {2}", status, code, error));
            EmitSignal(SignalName.IdentityMergeFailure);
        };

        _brainCloudWrapper.IdentityService.MergeEmailIdentity(email, password, successCallback, failureCallback);
    }

    public void RequestUniversalIdentityAttach(string universalID, string password)
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("Attach Universal Identity Success | {0}", response));
            EmitSignal(SignalName.IdentityAttachSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("Attach Universal Identity Failed | {0}  {1}  {2}", status, code, error));
            EmitSignal(SignalName.IdentityAttachFailure);
        };

        _brainCloudWrapper.IdentityService.AttachUniversalIdentity(universalID, password, successCallback, failureCallback);
    }

    public void RequestUniversalIdentityMerge(string universalID, string password)
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("Merge Universal Identity Success | {0}", response));
            EmitSignal(SignalName.IdentityMergeSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("Merge Universal Identity Failed | {0}  {1}  {2}", status, code, error));
            EmitSignal(SignalName.IdentityMergeFailure);
        };

        _brainCloudWrapper.IdentityService.MergeUniversalIdentity(universalID, password, successCallback, failureCallback);
    }

    // Entity Service

    public void RequestEntityGetPage()
    {
        string context = "{\"pagination\":{\"rowsPerPage\":50,\"pageNumber\":1},\"searchCriteria\":{\"entityType\":\"user\"},\"sortCriteria\":{\"createdAt\":1,\"updatedAt\":-1}}";
        
        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("Entity.GetPage Success | {0}", response));

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
            GD.Print(string.Format("Entity.GetPage Failed | {0}  {1}  {2}", status, code, error));
        };

        _brainCloudWrapper.EntityService.GetPage(context, successCallback, failureCallback);
    }

    public void RequestCreateEntity(string entityType, string jsonEntityData, string jsonEntityAcl)
    {        
        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("Create Entity Success | {0}", response));
            EmitSignal(SignalName.CreateEntitySuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("Create Entity Failed | {0}  {1}  {2}", status, code, error));
        };

        _brainCloudWrapper.EntityService.CreateEntity(entityType, jsonEntityData, jsonEntityAcl, successCallback, failureCallback);
    }

    public void RequestUpdateEntity(string entityId, string entityType, string jsonEntityData, string jsonEntityAcl)
    {
        // The version of the entity to delete. Use - 1 to indicate the newest version
        int version = -1;

        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("Update Entity Success | {0}", response));
            EmitSignal(SignalName.UpdateEntitySuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("Update Entity Failed | {0}  {1}  {2}", status, code, error));
        };

        _brainCloudWrapper.EntityService.UpdateEntity(entityId, entityType, jsonEntityData, jsonEntityAcl, version, successCallback, failureCallback);
    }

    public void RequestDeleteEntity(string entityId)
    {
        // The version of the entity to delete. Use - 1 to indicate the newest version
        int version = -1;

        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("Delete Entity Success | {0}", response));
            EmitSignal(SignalName.DeleteEntitySuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("Delete Entity Failed | {0}  {1}  {2}", status, code, error));
        };

        _brainCloudWrapper.EntityService.DeleteEntity(entityId, version, successCallback, failureCallback);
    }
}
