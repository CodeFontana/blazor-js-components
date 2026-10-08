using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;
using System.Diagnostics.CodeAnalysis;

namespace BlazorJSComponents;

internal sealed class InteractiveJSHandler(string src, string? key, IJSRuntime jsRuntime) : IJSHandler
{
    private readonly string _rootId = $"bjc-{Guid.CreateVersion7():N}";
    private int _instanceId;
    private bool _disposed;
    private TaskCompletionSource? _initTcs = new();
    private Task? _onAfterRenderTask;
    private object?[]? _args;

    public void SetArgs(object?[]? args)
        => _args = args;

    public void Render(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "id", _rootId);
        builder.AddAttribute(2, "hidden", true);
        builder.AddAttribute(3, "style", "display:none");
        builder.CloseElement();
    }

    public async Task OnAfterRenderAsync()
    {
        _onAfterRenderTask = OnAfterRenderAsyncCore();
        try
        {
            await _onAfterRenderTask;
        }
        finally
        {
            _onAfterRenderTask = null;
        }

        async Task OnAfterRenderAsyncCore()
        {
            try
            {
                if (_initTcs is not null)
                {
                    _instanceId = await jsRuntime.InvokeAsync<int>("__blazorScript.getOrCreateJSComponent", 0, src, key);
                    _initTcs.SetResult();
                    _initTcs = null;
                }

                if (_disposed)
                {
                    await DisposeJSInstanceAsync();
                    return;
                }

                if (_instanceId != 0)
                {
                    await jsRuntime.InvokeVoidAsync("__blazorScript.setJSComponentParameters", _instanceId, _args, _rootId);
                }
            }
            catch (Exception ex) when (IsStaleInterop(ex))
            {
            }
        }
    }

    public async ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, object?[]? args)
    {
        await WaitForPendingRenderAsync();

        if (!TryGetInstanceId(out int instanceId))
        {
            return default!;
        }

        return await jsRuntime.InvokeAsync<TValue>("__blazorScript.invokeJSComponentMethod", instanceId, identifier, args);
    }

    public async ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        await WaitForPendingRenderAsync();

        if (!TryGetInstanceId(out int instanceId))
        {
            return default!;
        }

        return await jsRuntime.InvokeAsync<TValue>("__blazorScript.invokeJSComponentMethod", cancellationToken, instanceId, identifier, args);
    }

    private bool TryGetInstanceId(out int instanceId)
    {
        instanceId = _instanceId;
        if (instanceId != 0)
        {
            return true;
        }

        if (_disposed)
        {
            return false;
        }

        throw new InvalidOperationException($"There is no JS component associated with the '{nameof(JS)}' instance");
    }

    private async Task WaitForPendingRenderAsync()
    {
        if (_initTcs is not null)
        {
            await _initTcs.Task;
        }

        if (_onAfterRenderTask is not null)
        {
            await _onAfterRenderTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        Task? pendingRender = _onAfterRenderTask;
        if (pendingRender is not null)
        {
            try
            {
                await pendingRender;
            }
            catch (Exception ex) when (ex is JSDisconnectedException or OperationCanceledException or JSException)
            {
                // OnAfterRenderAsync reports a real JSException. Disposal must not report it again.
            }
        }

        await DisposeJSInstanceAsync();
    }

    private async Task DisposeJSInstanceAsync()
    {
        int instanceId = _instanceId;
        if (instanceId == 0)
        {
            return;
        }

        _instanceId = 0;

        try
        {
            await jsRuntime.InvokeVoidAsync("__blazorScript.disposeJSComponent", instanceId);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or OperationCanceledException)
        {
        }
    }

    private bool IsStaleInterop(Exception exception)
        => exception is JSDisconnectedException or OperationCanceledException
            || (exception is JSException && _disposed);
}
