using Godot;
using System;

public partial class Cursor : Area2D
{
	private Label _nameLabel;
	private Sprite2D _sprite;
	private bool _userCursor;

	public override void _Ready()
	{
		_nameLabel = GetNode<Label>("Name");
		_sprite = GetNode<Sprite2D>("CursorSprite");
	}

	public void SetName(string name)
	{
		_nameLabel.Text = name;
	}

	public void SetColour(int colourIndex)
	{
		_sprite.Texture = (Texture2D)GD.Load("res://Cursors/arrow" + colourIndex + ".png");
	}

	public void SetPosition(Vector2 pos)
	{
		Position = pos;
	}

	public void SetUserCursor(bool userCursor)
	{
		this._userCursor = userCursor;
	}
}
