using Godot;
using System;

public partial class LobbyMember : HBoxContainer
{
    [Signal]
    public delegate void ColourSwatchClickedEventHandler();

    // User's name
    private Label _nameLabel;

    // "HOST" badge, shown only on the lobby owner's row.
    private Label _hostBadge;

    // "YOU" badge, shown only on the local player's own row.
    private Label _youBadge;

    // "Ready" / "Not ready" status line.
    private Label _readyLabel;

    // Colour swatch — clickable ONLY on your own row (cpp: ColorButton opens a popup picker
    // on your row only; react: canPickColor = isMe). Other members' swatches are display-only.
    private Button _colourButton;
    private ColorRect _colourRect;

    // This member's cxId, so the lobby chat's "isMe" colouring and other lookups can find
    // the row for a given player (set by LobbyScreen/MatchScreen alongside SetName/SetColour).
    private string _cxId;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>("Name");
        _hostBadge = GetNode<Label>("HostBadge");
        _youBadge = GetNode<Label>("YouBadge");
        _readyLabel = GetNode<Label>("ReadyLabel");
        _colourButton = GetNode<Button>("ColorButton");
        _colourRect = GetNode<ColorRect>("ColorButton/ColorRect");

        _colourButton.Connect(Button.SignalName.Pressed, Callable.From(() => EmitSignal(SignalName.ColourSwatchClicked)));

        // Hide the host badge when not necessary (i.e. user is NOT the host / lobby owner)
        _hostBadge.Hide();
        _youBadge.Hide();
    }

    public void SetCxId(string cxId)
    {
        _cxId = cxId;
    }

    public string GetCxId()
    {
        return _cxId;
    }

    /// <summary>
    /// Show the "YOU" badge on this row — the local player's own member row only.
    /// </summary>
    public void SetIsMe(bool isMe)
    {
        _youBadge.Visible = isMe;
        _colourButton.Disabled = !isMe;
        _colourButton.MouseDefaultCursorShape = isMe ? Control.CursorShape.PointingHand : Control.CursorShape.Arrow;
    }

    /// <summary>
    /// Update the "Ready" / "Not ready" status line.
    /// </summary>
    public void SetReady(bool isReady)
    {
        _readyLabel.Text = isReady ? "Ready" : "Not ready";
        _readyLabel.AddThemeColorOverride("font_color", isReady ? new Color(0.4f, 1f, 0.4f) : new Color(0.6f, 0.6f, 0.6f));
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
        // The swatch carries the colour-coding — the name stays a fixed, always-legible
        // colour (matching cpp/react/GDScript) instead of inheriting the player's palette
        // colour, some of which are too dark to read as text on the card background.
        _colourRect.Color = colour;
    }

    /// <summary>
    /// Display the "HOST" badge if the user is the host / lobby owner.
    /// </summary>
    /// <param name="userIsHost">bool determining whether the user is the host / lobby owner.</param>
    public void SetIsHost(bool userIsHost)
    {
        _hostBadge.Visible = userIsHost;
    }
}
