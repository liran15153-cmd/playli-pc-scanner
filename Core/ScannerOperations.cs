namespace Playli.PcScanner;

public enum ScannerOperation { None, Scan, Export, Share }

public sealed class ScannerOperations
{
    private readonly object sync = new();
    private ScannerOperation current;
    private TaskCompletionSource idle = Completed();
    public event Action? StateChanged;
    public ScannerOperation Current { get { lock(sync) return current; } }
    public bool CanClose => Current != ScannerOperation.Export;
    public Task WhenIdle { get { lock(sync) return idle.Task; } }
    private static TaskCompletionSource Completed() { var source=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);source.SetResult();return source; }

    public async Task<T> RunAsync<T>(ScannerOperation operation, Func<Task<T>> start, CancellationToken waitCancellation = default)
    {
        if(operation==ScannerOperation.None)throw new ArgumentOutOfRangeException(nameof(operation));
        lock(sync){if(current!=ScannerOperation.None)throw new InvalidOperationException("operation_active");current=operation;idle=new(TaskCreationOptions.RunContinuationsAsynchronously);}
        StateChanged?.Invoke();
        Task<T> worker;
        try { worker=start(); } catch { Finish();throw; }
        var completion=ObserveCompletion(worker);
        // A cancelled UI wait does not cancel ownership of the native worker.
        // Observe a later fault even after that UI wait has already ended.
        _=completion.ContinueWith(task=>_ = task.Exception,CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        return await completion.WaitAsync(waitCancellation);
    }
    private async Task<T> ObserveCompletion<T>(Task<T> worker) { try { return await worker.ConfigureAwait(false); } finally { Finish(); } }
    private void Finish()
    {
        lock(sync){current=ScannerOperation.None;idle.TrySetResult();}
        StateChanged?.Invoke();
    }
}
