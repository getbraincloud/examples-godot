using Godot;
using System.Collections.Generic;
using System;

public partial class CursorParty : Area2D
{
	[Signal]
	public delegate void MouseMovedEventHandler(Vector2 mousePosition);
	[Signal]
	public delegate void MouseClickedEventHandler(Vector2 mousePosition, MouseButton mouseButton);

	// Cursor/Arrow image used for this user's mouse while in the game area
	private Resource _userArrow;

	private Vector2 _mousePosition;

	private Panel _gameAreaPanel;

	public override void _Ready()
	{
		_gameAreaPanel = GetNode<Panel>("GameAreaPanel");
		
		Connect(SignalName.MouseEntered, new Callable(this, MethodName.OnMouseEntered));
		Connect(SignalName.MouseExited, new Callable(this, MethodName.OnMouseExited));
	}

	public override void _Input(InputEvent @event)
	{
		if (MouseInGameArea())
		{
			if (@event is InputEventMouseButton eventMouseButton && @event.IsPressed())
			{
				EmitSignal(SignalName.MouseClicked, _mousePosition, (int)eventMouseButton.ButtonIndex);
			}
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (MouseInGameArea())
		{
			var gameAreaRect = _gameAreaPanel.GetRect();
			var globalRect = _gameAreaPanel.GetGlobalRect();

			var normalizedX = (GetGlobalMousePosition().X - globalRect.Position.X) / gameAreaRect.Size.X;
			var normalizedY = (GetGlobalMousePosition().Y - globalRect.Position.Y) / gameAreaRect.Size.Y;

			_mousePosition = new Vector2(normalizedX, normalizedY);

			EmitSignal(SignalName.MouseMoved, _mousePosition);
		}
	}

	/// <summary>
	/// Load/Set the arrow image used for this user's custom mouse cursor.
	/// </summary>
	/// <param name="cursorPath">string path to the arrow image associated with this user's colour index.</param>
	public void SetCustomCursor(string cursorPath)
	{
		_userArrow = ResourceLoader.Load(cursorPath);
		if (MouseInGameArea())
		{
			OnMouseEntered();
		}
	}

	/// <summary>
	/// Detect whether the mouse is in the Game Area / Cursor Party.
	/// </summary>
	/// <returns>True if the mouse position is within the Area2D representing the Game Area / Cursor Party</returns>
	private bool MouseInGameArea()
	{
		var spaceState = GetWorld2D().DirectSpaceState;
		var query = new PhysicsPointQueryParameters2D();

		// This is necessary to detect Area2D (the Node type of the CursorParty Scene)
		query.CollideWithAreas = true;

		query.Position = GetGlobalMousePosition();
		var result = spaceState.IntersectPoint(query);
		
		// If the count is greater than 0, it means there was a successful collision
		if (result.Count > 0)
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// Set this user's custom mouse cursor when the mouse enters the game area / CursorParty.
	/// </summary>
	private void OnMouseEntered()
	{
		Input.SetCustomMouseCursor(_userArrow);
	}

	/// <summary>
	/// Remove this user's custom mouse cursor when the mouse leaves the game area / CursorParty.
	/// </summary>
	private void OnMouseExited()
	{
		Input.SetCustomMouseCursor(null);
	}
}
