using System.Collections.Concurrent;

namespace RizzziGit.Collections;

public class WaitQueue<T>
{
  private class LockableObject
  {
    public LockableObject()
    {
      Backlog = new();
      DequeueWaiters = new();
      EnqueueWaiters = new();
    }

    public ConcurrentQueue<T> Backlog;
    public ConcurrentQueue<TaskCompletionSource<T>> DequeueWaiters;
    public ConcurrentQueue<TaskCompletionSource<TaskCompletionSource<T>>> EnqueueWaiters;
  }

  public WaitQueue() : this(null) { }
  public WaitQueue(int? capacity)
  {
    Capacity = capacity;
    LockObject = new();
  }

  private int? Capacity;
  private LockableObject? LockObject;

  public int Count => LockObject?.Backlog.Count ?? throw new ObjectDisposedException(typeof(LockableObject).Name);

  public void Dispose(Exception? exception = null)
  {
    if (LockObject == null)
    {
      throw new ObjectDisposedException(typeof(LockableObject).Name);
    }

    lock (LockObject)
    {
      LockableObject lockObject = LockObject;
      LockObject = null;

      while (lockObject.DequeueWaiters.TryDequeue(out TaskCompletionSource<T>? dequeueWaiter))
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

      while (lockObject.EnqueueWaiters.TryDequeue(out TaskCompletionSource<TaskCompletionSource<T>>? enqueueWaiter))
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
    }
  }

  public Task<T> DequeueAsync()
  {
    TaskCompletionSource<T> source = new();
    if (LockObject == null)
    {
      throw new ObjectDisposedException(typeof(LockableObject).Name);
    }

    lock (LockObject)
    {
      if (LockObject.Backlog.TryDequeue(out T? result) && (result != null))
      {
        source.SetResult(result);
      }
      else if (LockObject.EnqueueWaiters.TryDequeue(out TaskCompletionSource<TaskCompletionSource<T>>? enqueueWaiter) && (enqueueWaiter != null))
      {
        enqueueWaiter.SetResult(source);
      }
      else
      {
        LockObject.DequeueWaiters.Enqueue(source);
      }
    }

    return source.Task;
  }

  public async Task EnqueueAsync(T item)
  {
    TaskCompletionSource<TaskCompletionSource<T>>? enqueueSource = null;

    if (LockObject == null)
    {
      throw new ObjectDisposedException(typeof(LockableObject).Name);
    }

    lock (LockObject)
    {
      if (LockObject.DequeueWaiters.TryDequeue(out TaskCompletionSource<T>? result) && (result != null))
      {
        result.SetResult(item);
      }
      else if ((Capacity != null) && (LockObject.Backlog.Count >= Capacity))
      {
        LockObject.EnqueueWaiters.Enqueue(enqueueSource = new());
      }
      else
      {
        LockObject.Backlog.Enqueue(item);
      }
    }

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
