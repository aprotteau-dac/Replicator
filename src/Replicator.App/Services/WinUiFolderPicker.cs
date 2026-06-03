using Microsoft.UI.Xaml;
using Replicator.Presentation.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Replicator.App.Services;

public sealed class WinUiFolderPicker(Window window) : IFolderPicker
{
    public async Task<string?> PickFolderAsync(string initialPath, CancellationToken cancellationToken = default)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };

        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
