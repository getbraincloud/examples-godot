using Godot;
using System;

public partial class LobbyMember : HBoxContainer
{
    // User's name
    private Label _nameLabel;

    // Indicates that this user is the host / lobby owner when visible
    private TextureRect _hostIcon;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>("Name");
        _hostIcon = GetNode<TextureRect>("HostIcon");

        // Hide the host icon when not necessary (i.e. user is NOT the host / lobby owner)
        _hostIcon.Hide();
    }

    /// <summary>
    /// Set the user's name.
    /// </summary>
    /// <param name="name">string value of the user's name</param>
    public void SetName(string name)
    {
        _nameLabel.Text = name;
    }

    public void SetColour(Color colour)
    {
        // TODO:  fix the way colour index is used
        _nameLabel.AddThemeColorOverride("font_color", colour);
    }

    /// <summary>
    /// Display the host icon if the user is the host / lobby owner.
    /// </summary>
    /// <param name="userIsHost">bool determining whether the user is the host / lobby owner.</param>
    public void SetHostIcon(bool userIsHost)
    {
        // TODO:  verify that this works and that we don't need to use Show() / Hide()
        _hostIcon.Visible = userIsHost;
    }
}
