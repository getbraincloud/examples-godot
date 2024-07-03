using Godot;
using System;

public partial class Member : Sprite2D
{
	private Label _nameLabel;

	private string _cxID;
	private string _name;
	private int _colourIndex;

	public override void _Ready()
	{
		_nameLabel = GetNode<Label>("Name");
	}

	public void SetCxID(string cxID)
	{
		_cxID = cxID;
	}

	public string GetCxID()
	{
		return _cxID;
	}

	public string GetName()
	{
		return _name;
	}

	/// <summary>
	/// Set the arrow/cursor colour and name text colour of the user.
	/// </summary>
	/// <param name="colourIndex">int representing the colour index. Used to select the arrow to be select which arrow should be used for the Texture.</param>
	public void SetColour(int colourIndex)
	{
		// Set texture (arrow/cursor colour)
		Texture = GD.Load<Texture2D>("Art/Cursors/arrow" + colourIndex + ".png");

		// Set name text colour
		_nameLabel.AddThemeColorOverride("font_color", Main.Colours[colourIndex]);
	}

	/// <summary>
	/// Set the name of the member controlling this cursor.
	/// </summary>
	/// <param name="name">string containing member's name.</param>
	public void SetName(string name)
	{
		_name = name;
		_nameLabel.Text = name;
	}
}
