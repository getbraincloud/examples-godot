using Godot;
using System;

public partial class VirtualCurrency : Control
{
	private LineEdit _consumedField;
	private LineEdit _purchasedField;
	private LineEdit _balanceField;
	private LineEdit _awardedField;
	private LineEdit _awardAmountField;
	private LineEdit _consumedAmountField;
	private Button _awardButton;
	private Button _consumeButton;

	private BCManager _brainCloud;

	public override void _Ready()
	{
		_consumedField = GetNode<LineEdit>("VBoxContainer/Labels/ConsumedLine");
		_purchasedField = GetNode<LineEdit>("VBoxContainer/Labels/PurchasedLine");
		_balanceField = GetNode<LineEdit>("VBoxContainer/Labels/BalanceLine");
		_awardedField = GetNode<LineEdit>("VBoxContainer/Labels/AwardedLine");
		_awardAmountField = GetNode<LineEdit>("VBoxContainer/Actions/AwardLine");
		_consumedAmountField = GetNode<LineEdit>("VBoxContainer/Actions/ConsumeLine");
		_awardButton = GetNode<Button>("VBoxContainer/Actions/AwardButton");
		_consumeButton = GetNode<Button>("VBoxContainer/Actions/ConsumeButton");

		_brainCloud = GetNode<BCManager>("/root/BCManager");
	}

	private void RefreshCurrencyFields()
	{
		//
	}

	private void OnAwardButtonPressed()
	{
		//
	}

	private void OnConsumeButtonPressed()
	{
		//
	}
}
