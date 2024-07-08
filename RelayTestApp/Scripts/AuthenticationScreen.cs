using Godot;
using System;

public partial class AuthenticationScreen : Control
{
	[Signal]
	public delegate void AuthenticationRequestedEventHandler(string id, string token);

	// Username (universalID, email, etc.)
	private LineEdit _idLine;

	// Password
	private LineEdit _tokenLine;

	// Error message
	private Label _errorMessageLabel;

	private Button _authButton;

	public override void _Ready()
	{
		_idLine = GetNode<LineEdit>("AuthFields/IDLine");
		_tokenLine = GetNode<LineEdit>("AuthFields/TokenLine");
		_errorMessageLabel = GetNode<Label>("AuthFields/ErrorMessage");
		_authButton = GetNode<Button>("AuthFields/AuthButton");

		// Connect Button listener(s)
		_authButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnAuthButtonPressed));

		// Hide error message when not necessary
		_errorMessageLabel.Hide();
	}

	/// <summary>
	/// Display an error message.
	/// </summary>
	/// <param name="errorMsg"></param>
	public void SetErrorMessage(string errorMsg)
	{
		_errorMessageLabel.Text = errorMsg;
		_errorMessageLabel.Show();
	}

	/// <summary>
	/// Attempt authentication
	/// </summary>
	private void OnAuthButtonPressed()
	{
		// Hide error message on retry
		_errorMessageLabel.Hide();

		// Get values from authentication fields
		string id = _idLine.Text;
		string token = _tokenLine.Text;

		// Verify that ID and Token fields were filled in
		if(string.IsNullOrEmpty(id) || string.IsNullOrEmpty(token))
		{
			// Prompt user to fill in empty fields and try again
			SetErrorMessage("One or more authentication fields were empty");

			return;

		}

		// Attempt to authenticate
		EmitSignal(SignalName.AuthenticationRequested, id, token);
	}
}
