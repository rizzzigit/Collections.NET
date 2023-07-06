using System.Collections.Concurrent;

namespace RizzziGit.Collections;

public class WaitQueue<T> : IDisposable
{
  public WaitQueue() : this(null) { }
  public WaitQueue(int? capacity)
  {
    Collection = capacity != null ? new((int)capacity) : new();
  }

  private Exception? Exception;
  private BlockingCollection<T> Collection;

  public int Capacity => Collection.BoundedCapacity;
  public int Count => Collection.Count;

  public void Dispose() => Dispose(null);
  public void Dispose(Exception? exception = null)
  {
    Exception = exception;
    Collection.CompleteAdding();
  }

  public T Dequeue() => Dequeue(null);
  public T Dequeue(CancellationToken? cancellationToken)
  {
    if (Collection.IsCompleted)
    {
      if (Exception != null)
      {
        throw Exception;
      }

      throw new ObjectDisposedException(typeof(WaitQueue<T>).Name);
    }

    return Collection.Take(cancellationToken ?? new(false));
  }

  public void Enqueue(T item) => Enqueue(item, null);
  public void Enqueue(T item, CancellationToken? cancellationToken)
  {
    if (Collection.IsAddingCompleted)
    {
      throw new ObjectDisposedException(typeof(WaitQueue<T>).Name);
    }

    Collection.Add(item, new(false));
  }
}
