namespace ProfitDjinn.Core;

/// <summary>
/// A refusal or error written for the person using the app: what went wrong and what to do.
/// The UI shows <see cref="Exception.Message"/> as-is. Anything that is not a
/// UserFacingException is a bug and is shown with its technical detail.
/// </summary>
public class UserFacingException : Exception
{
    public UserFacingException(string message) : base(message) { }
    public UserFacingException(string message, Exception inner) : base(message, inner) { }
}
