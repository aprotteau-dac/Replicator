using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Replicator.Presentation.Services;

namespace Replicator.App.Services;

public sealed class WinUiUserConfirmation(FrameworkElement dialogHost) : IUserConfirmation
{
    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = cancelText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = dialogHost.XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
