using Godot;
using System;

public partial class ColourSelectButton : Button
{
	[Export]
	int colourIndex = 0;
	
	public override void _Ready()
	{
        ChangeColour(Main.Colours[colourIndex]);
		Text = colourIndex.ToString();
		Pressed += () => GetNode<LobbyScreen>("../../..").OnColourButtonPressed(colourIndex);
	}

	private void ChangeColour(Color newColour)
	{
		StyleBoxFlat normalStyleBox = new StyleBoxFlat
        {
            BgColor = newColour,
			BorderColor = newColour,
			BorderWidthBottom = 3,
			BorderWidthLeft = 3,
			BorderWidthRight = 3,
			BorderWidthTop = 3,
        };
		StyleBoxFlat hoverStyleBox = (StyleBoxFlat)normalStyleBox.Duplicate();
		hoverStyleBox.BgColor = new Color("#1F2124");

        AddThemeStyleboxOverride("normal", normalStyleBox);
		AddThemeStyleboxOverride("hover", hoverStyleBox);
	}
}
