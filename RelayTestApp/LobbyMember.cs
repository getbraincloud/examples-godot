using Godot;
using System;

public partial class LobbyMember : HBoxContainer
{
    private Label _nameLabel;
    private TextureRect _hostIcon;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>("NameLabel");
        _hostIcon = GetNode<TextureRect>("HostIcon");
        _hostIcon.Hide();
    }

    public void SetName(string name)
    {
        _nameLabel.Text = name;
    }

    public void SetColour(GameManager.GameColors colour)
    {
        _nameLabel.AddThemeColorOverride("font_color", GameManager.ReturnUserColor(colour));
    }

    public void SetHostIcon(bool memberIsHost)
    {
        if (memberIsHost)
        {
            _hostIcon.Show();
        }
        else
        {
            _hostIcon.Hide();
        }
    }
}
