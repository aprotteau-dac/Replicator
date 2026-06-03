namespace Replicator.Presentation.Services;

public interface IUserConfirmation
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText);
}
