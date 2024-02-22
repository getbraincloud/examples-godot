using BrainCloud.Common;
using Godot;
using Godot.Collections;
using System;

public partial class Entity : Control
{
    private LineEdit _entityIDField;
    private LineEdit _entityTypeField;
    private LineEdit _nameField;
    private LineEdit _ageField;
    
    private Button _createButton;
    private Button _updateButton;
    private Button _deleteButton;

    private BCManager _brainCloud;

    // Default entity values to display when no entity has been retrieved
    private string _entityID;
    private string _entityType;
    private string _entityName;
    private string _entityAge;

    // Objects used for creating / updating entities
    private Dictionary _newEntity;
    private string _acl = Json.Stringify(new Dictionary { { "other", 0 } });
    private string _newEntityData;

    public override void _Ready()
    {
        _entityIDField = GetNode<LineEdit>("VBoxContainer/IDValue");
        _entityTypeField = GetNode<LineEdit>("VBoxContainer/TypeValue");
        _nameField = GetNode<LineEdit>("VBoxContainer/ProfileFieldsHBoxContainer/NameLine");
        _ageField = GetNode<LineEdit>("VBoxContainer/ProfileFieldsHBoxContainer/AgeLine");

        _createButton = GetNode<Button>("VBoxContainer/ButtonsVBoxContainer/CreateButton");
        _updateButton = GetNode<Button>("VBoxContainer/ButtonsVBoxContainer/UpdateButton");
        _deleteButton = GetNode<Button>("VBoxContainer/ButtonsVBoxContainer/DeleteButton");

        _brainCloud = GetNode<BCManager>("/root/BCManager");

        _createButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnCreateButtonPressed));
        _updateButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnUpdateButtonPressed));
        _deleteButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnDeleteButtonPressed));

        _brainCloud.Connect(BCManager.SignalName.EntityReceived, new Callable(this, MethodName.OnEntityReceived));
        _brainCloud.Connect(BCManager.SignalName.CreateEntitySuccess, new Callable(this, MethodName.OnCreateEntitySuccess));
        _brainCloud.Connect(BCManager.SignalName.UpdateEntitySuccess, new Callable(this, MethodName.OnUpdateEntitySuccess));
        _brainCloud.Connect(BCManager.SignalName.DeleteEntitySuccess, new Callable(this, MethodName.OnDeleteEntitySuccess));

        // These fields are read-only
        _entityIDField.Editable = false;
        _entityTypeField.Editable = false;

        _updateButton.Hide();
        _deleteButton.Hide();

        SetDefaultEntityValues();
        DisplayEntity();

        GetEntities();
    }

    /// <summary>
    /// Display default / placeholder entity values
    /// </summary>
    private void SetDefaultEntityValues()
    {
        _entityID = "00000000-0000-0000-0000-000000000000";
        _entityType = "user"; // default type of "user" for this demo
        _entityName = "Name";
        _entityAge = "Age";
    }

    /// <summary>
    /// Display the current entity's values
    /// </summary>
    private void DisplayEntity()
    {
        _entityIDField.Text = _entityID;
        _entityTypeField.Text = _entityType;
        _nameField.PlaceholderText = _entityName;
        _ageField.PlaceholderText = _entityAge;
    }

    /// <summary>
    /// Retrieve existing entity
    /// </summary>
    private void GetEntities()
    {
        GD.Print("Searching for entities . . .");
        _brainCloud.GetPage();
    }

    /// <summary>
    /// Create entity data object with user submitted name and age values
    /// </summary>
    /// <returns></returns>
    private bool CreateNewEntityData()
    {
        string name = _nameField.Text;
        string age = _ageField.Text;
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(age))
        {
            GD.Print("Please fill in empty fields");

            return false;
        }

        _newEntity = new Dictionary();
        _newEntity.Add("name", name);
        _newEntity.Add("age", age);

        _newEntityData = Json.Stringify(_newEntity);

        return true;
    }

    private void OnEntityReceived(Dictionary entity)
    {
        GD.Print("Entity Received! " + entity);

        _entityID = (string)entity["entityId"];
        _entityType = (string)entity["entityType"];

        Dictionary entityData = (Dictionary)entity["data"];
        _entityAge = (string)entityData["age"];
        _entityName = (string)entityData["name"];

        DisplayEntity();

        _createButton.Hide();
        _updateButton.Show();
        _deleteButton.Show();
    }

    private void OnCreateButtonPressed()
    {
        if (CreateNewEntityData())
        {
            _brainCloud.CreateEntity(_entityType, _newEntityData, _acl);
        }
    }

    private void OnUpdateButtonPressed()
    {
        if (CreateNewEntityData())
        {
            _brainCloud.UpdateEntity(_entityID, _entityType, _newEntityData, _acl);
        }
    }

    private void OnDeleteButtonPressed()
    {
        _brainCloud.DeleteEntity(_entityID);
    }

    private void OnCreateEntitySuccess()
    {
        _nameField.Clear();
        _ageField.Clear();

        GetEntities();

        _createButton.Hide();
        _updateButton.Show();
        _deleteButton.Show();
    }

    private void OnUpdateEntitySuccess()
    {
        _nameField.Clear();
        _ageField.Clear();

        GetEntities();
    }

    private void OnDeleteEntitySuccess()
    {
        SetDefaultEntityValues();
        DisplayEntity();

        _createButton.Show();
        _updateButton.Hide();
        _deleteButton.Hide();
    }
}
