using Godot;
using System;
using static GameManager;
using System.Collections.Generic;
using static Godot.HttpRequest;

public partial class CursorParty : Area2D
{
    [Signal]
    public delegate void MouseMovedEventHandler(Vector2 mousePos);
    [Signal]
    public delegate void MouseClickedEventHandler(Vector2 mousePos, MouseButton mouseButton);

    public float SizeX;
    public float SizeY;

    private Panel _gameAreaPanel;

    private Vector2 _mousePos;

    private Resource _userCursor;

    public override void _Ready()
    {
        _gameAreaPanel = GetNode<Panel>("GameAreaPanel");
        SizeX = _gameAreaPanel.Size.X;
        SizeY = _gameAreaPanel.Size.Y;
    }

    public override void _Input(InputEvent @event)
    {
        if (MouseInGameArea())
        {
            if (@event is InputEventMouseButton eventMouseButton && @event.IsPressed())
            {
                EmitSignal(SignalName.MouseClicked, _mousePos, (int)eventMouseButton.ButtonIndex);
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

            _mousePos = new Vector2(normalizedX, normalizedY);

            EmitSignal(SignalName.MouseMoved, _mousePos);
        }
    }

    public Rect2 GetGameAreaRect()
    {
        return _gameAreaPanel.GetRect();
    }

    public Rect2 GetGlobalRect()
    {
        return _gameAreaPanel.GetGlobalRect();
    }

    public void SetCustomCursor(string cursorPath)
    {
        _userCursor = ResourceLoader.Load(cursorPath);
        if(MouseInGameArea() )
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
        query.CollideWithAreas = true;  // This is necessary to detect Area2D
        query.Position = GetGlobalMousePosition();
        var result = spaceState.IntersectPoint(query);
        if (result.Count > 0)
        {
            return true;
        }

        return false;
    }

    private void OnMouseEntered()
    {
        Input.SetCustomMouseCursor(_userCursor);
    }

    private void OnMouseExited()
    {
        Input.SetCustomMouseCursor(null);
    }
}
