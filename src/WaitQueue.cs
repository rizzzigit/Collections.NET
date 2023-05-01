using System.Collections.Concurrent;

namespace RizzziGit.Collections;

public class WaitQueue<T>
{
  public WaitQueue() : this(null) { }
  public WaitQueue(int? capacity)
  {
    Capacity = capacity ?? -1;
    Mutex = new();

    Backlog = new();
    DequeueWaiters = new();
    EnqueueWaiters = new();
  }

  private Mutex? Mutex;

  private ConcurrentQueue<T> Backlog;
  private ConcurrentQueue<TaskCompletionSource<T>> DequeueWaiters;
  private ConcurrentQueue<TaskCompletionSource<TaskCompletionSource<T>>> EnqueueWaiters;

  public int Capacity { get; private set; }
  public int Count => Mutex != null ? Backlog.Count : throw new ObjectDisposedException(typeof(WaitQueue<T>).Name);

  public void Dispose(Exception? exception = null)
  {
    if (Mutex == null)
    {
      throw new ObjectDisposedException(typeof(WaitQueue<T>).Name);
    }

    Mutex mutex = Mutex;
    Mutex = null;

    mutex.WaitOne();
    while (DequeueWaiters.TryDequeue(out TaskCompletionSource<T>? dequeueWaiter))
    {
      if (exception != null)
      {
        dequeueWaiter.SetException(exception);
      }
      else
      {
        dequeueWaiter.SetCanceled();
      }
    }

    while (EnqueueWaiters.TryDequeue(out TaskCompletionSource<TaskCompletionSource<T>>? enqueueWaiter))
    {
      if (exception != null)
      {
        enqueueWaiter.SetException(exception);
      }
      else
      {
        enqueueWaiter.SetCanceled();
      }
    }
    mutex.Close();
    Backlog.Clear();
  }

  public Task<T> DequeueAsync()
  {
    TaskCompletionSource<T> source = new();
    if (Mutex == null)
    {
      throw new ObjectDisposedException(typeof(WaitQueue<T>).Name);
    }

    Mutex.WaitOne();
    if (Backlog.TryDequeue(out T? result) && (result != null))
    {
      source.SetResult(result);
    }
    else if (EnqueueWaiters.TryDequeue(out TaskCompletionSource<TaskCompletionSource<T>>? enqueueWaiter) && (enqueueWaiter != null))
    {
      enqueueWaiter.SetResult(source);
    }
    else
    {
      DequeueWaiters.Enqueue(source);
    }
    Mutex.ReleaseMutex();

    return source.Task;
  }

  public async Task EnqueueAsync(T item)
  {
    TaskCompletionSource<TaskCompletionSource<T>>? enqueueSource = null;

    if (Mutex == null)
    {
      throw new ObjectDisposedException(typeof(WaitQueue<T>).Name);
    }

    Mutex.WaitOne();
    if (DequeueWaiters.TryDequeue(out TaskCompletionSource<T>? result) && (result != null))
    {
      result.SetResult(item);
    }
    else if ((Capacity >= 0) && (Backlog.Count >= Capacity))
    {
      EnqueueWaiters.Enqueue(enqueueSource = new());
    }
    else
    {
      Backlog.Enqueue(item);
    }
    Mutex.ReleaseMutex();

    if (enqueueSource != null)
    {
      (await enqueueSource.Task).SetResult(item);
    }
  }

  public T Dequeue()
  {
    Task<T> task = DequeueAsync();

    try { task.Wait(); } catch { }
    return task.IsCompletedSuccessfully
      ? task.Result
      : throw task.Exception?.GetBaseException()
        ?? throw new TaskCanceledException();
  }

  public void Enqueue(T item)
  {
    Task task = EnqueueAsync(item);

    try { task.Wait(); } catch { }
    if (!task.IsCompletedSuccessfully)
    {
      throw task.Exception?.GetBaseException()
        ?? throw new TaskCanceledException();
    }
  }
}
