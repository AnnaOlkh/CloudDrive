using CloudDrive.Web.Models;
using Microsoft.JSInterop;

namespace CloudDrive.Web.Services;

public sealed class BrowserInterop(IJSRuntime js)
{
    public ValueTask<string?> StorageGetAsync(string key) => js.InvokeAsync<string?>("cloudDrive.storage.get", key);
    public ValueTask StorageSetAsync(string key, string value) => js.InvokeVoidAsync("cloudDrive.storage.set", key, value);
    public ValueTask StorageRemoveAsync(string key) => js.InvokeVoidAsync("cloudDrive.storage.remove", key);

    public async Task SaveFileAsync(string fileName, Stream content)
    {
        using var reference = new DotNetStreamReference(content, leaveOpen: true);
        await js.InvokeVoidAsync("cloudDrive.saveFile", fileName, reference);
    }

    public ValueTask ClickAsync(string elementId) => js.InvokeVoidAsync("cloudDrive.click", elementId);
    public ValueTask ResetInputAsync(string elementId) => js.InvokeVoidAsync("cloudDrive.resetInput", elementId);

    public ValueTask RegisterDropZoneAsync<T>(DotNetObjectReference<T> target) where T : class =>
        js.InvokeVoidAsync("cloudDrive.registerDrop", target);

    public ValueTask UnregisterDropZoneAsync() => js.InvokeVoidAsync("cloudDrive.unregisterDrop");

    public ValueTask<bool> IsFolderPickerSupportedAsync() => js.InvokeAsync<bool>("cloudDrive.fs.supported");
    public ValueTask<PickedFolder?> PickFolderAsync() => js.InvokeAsync<PickedFolder?>("cloudDrive.fs.pick");
    public ValueTask<List<LocalFileInfo>> ScanFolderAsync(int folderId) => js.InvokeAsync<List<LocalFileInfo>>("cloudDrive.fs.scan", folderId);
    public ValueTask<byte[]> ReadFileAsync(int folderId, string path) => js.InvokeAsync<byte[]>("cloudDrive.fs.read", folderId, path);
    public ValueTask WriteFileAsync(int folderId, string path, byte[] data) => js.InvokeVoidAsync("cloudDrive.fs.write", folderId, path, data);
    public ValueTask<string?> FileHashAsync(int folderId, string path) => js.InvokeAsync<string?>("cloudDrive.fs.hash", folderId, path);
    public ValueTask<bool> FileExistsAsync(int folderId, string path) => js.InvokeAsync<bool>("cloudDrive.fs.exists", folderId, path);
    public ValueTask DeleteFileAsync(int folderId, string path) => js.InvokeVoidAsync("cloudDrive.fs.remove", folderId, path);
    public ValueTask ReleaseFolderAsync(int folderId) => js.InvokeVoidAsync("cloudDrive.fs.release", folderId);
}
