using Godot;
using System;

public partial class Cursor : Area2D
{
	private Label _nameLabel;
	private Sprite2D _sprite;

	public override void _Ready()
	{
		_nameLabel = GetNode<Label>("Name");
		_sprite = GetNode<Sprite2D>("CursorSprite");
	}

	public override void _Input(InputEvent @event)
	{
		// Mouse in viewport coordinates.
		if (@event is InputEventMouseButton eventMouseButton)
			GD.Print("Mouse Click/Unclick at: ", eventMouseButton.Position);
		else if (@event is InputEventMouseMotion eventMouseMotion)
		{
			GD.Print("Mouse Motion at: ", eventMouseMotion.Position);
			Position = eventMouseMotion.Position;
		}


		//// Print the size of the viewport.
		//GD.Print("Viewport Resolution is: ", GetViewport().GetVisibleRect().Size);

		//Position = GetViewport().GetMousePosition();
	}

	public void SetPlayerName(string name)
	{
		_nameLabel.Text = name;
	}

	public void SetCursorColour(int colourIndex)
	{
		_sprite.Texture = (Texture2D)GD.Load("res://Cursors/arrow" + colourIndex + ".png");
	}
}
