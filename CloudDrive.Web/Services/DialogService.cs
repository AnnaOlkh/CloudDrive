using CloudDrive.Client;

namespace CloudDrive.Web.Services;

public abstract class DialogRequest { }

public sealed class ConfirmRequest(string title, string message, string confirmText, string? cancelText, bool danger) : DialogRequest
{
    public string Title { get; } = title;
    public string Message { get; } = message;
    public string ConfirmText { get; } = confirmText;
    public string? CancelText { get; } = cancelText;
    public bool Danger { get; } = danger;
    public TaskCompletionSource<bool> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed record ChoiceOption(string Label, string Value, bool Primary = false);

public sealed class ChoiceRequest(string title, string message, IReadOnlyList<ChoiceOption> options) : DialogRequest
{
    public string Title { get; } = title;
    public string Message { get; } = message;
    public IReadOnlyList<ChoiceOption> Options { get; } = options;
    public TaskCompletionSource<string> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class PromptRequest(string title, string? subtitle, string placeholder) : DialogRequest
{
    public string Title { get; } = title;
    public string? Subtitle { get; } = subtitle;
    public string Placeholder { get; } = placeholder;
    public TaskCompletionSource<string?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class DeletionChoice(string path, string action)
{
    public string Path { get; } = path;
    public string Action { get; } = action;
    public bool Selected { get; set; } = true;
}

public sealed class DeletionsRequest(IReadOnlyList<DeletionChoice> items) : DialogRequest
{
    public IReadOnlyList<DeletionChoice> Items { get; } = items;
    public TaskCompletionSource<IReadOnlyCollection<string>> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class SyncResultRequest(SyncExecutionResult result, string localName, string remotePath) : DialogRequest
{
    public SyncExecutionResult Result { get; } = result;
    public string LocalName { get; } = localName;
    public string RemotePath { get; } = remotePath;
    public TaskCompletionSource<bool> Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class DialogService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DialogRequest? Current { get; private set; }
    public event Action? Changed;

    private async Task<T> ShowAsync<T>(DialogRequest request, Task<T> result)
    {
        await _gate.WaitAsync();
        try
        {
            Current = request;
            Changed?.Invoke();
            return await result;
        }
        finally
        {
            Current = null;
            Changed?.Invoke();
            _gate.Release();
        }
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool danger = false)
    {
        var request = new ConfirmRequest(title, message, confirmText, "Cancel", danger);
        return ShowAsync(request, request.Result.Task);
    }

    public async Task AlertAsync(string title, string message)
    {
        var request = new ConfirmRequest(title, message, "OK", null, false);
        await ShowAsync(request, request.Result.Task);
    }

    /// <summary>Повертає Value вибраного варіанта; при закритті вікна - "cancel".</summary>
    public Task<string> ChoiceAsync(string title, string message, params ChoiceOption[] options)
    {
        var request = new ChoiceRequest(title, message, options);
        return ShowAsync(request, request.Result.Task);
    }

    public Task<string?> PromptFolderNameAsync(string location)
    {
        var request = new PromptRequest("New folder", "Create in: " + location, "Folder name");
        return ShowAsync(request, request.Result.Task);
    }

    public Task<IReadOnlyCollection<string>> ChooseDeletionsAsync(IReadOnlyList<DeletionChoice> items)
    {
        var request = new DeletionsRequest(items);
        return ShowAsync(request, request.Result.Task);
    }

    public Task<bool> ShowSyncResultAsync(SyncExecutionResult result, string localName, string remotePath)
    {
        var request = new SyncResultRequest(result, localName, remotePath);
        return ShowAsync(request, request.Closed.Task);
    }
}
