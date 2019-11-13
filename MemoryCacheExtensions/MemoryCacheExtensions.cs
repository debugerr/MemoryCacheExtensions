using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace debugger.Samples.Extensions
{
    public static class MemoryCacheExtensions
    {
        private static readonly SemaphoreSlim Synchronization = new SemaphoreSlim(1, 1);
        private static readonly ConcurrentDictionary<string, object> RunningTasks = new ConcurrentDictionary<string, object>();

        /// <summary>
        /// Does either retrieve an item from the cache if it is available and valid, otherwise calls
        /// the factory method to get the item and put it in there. This particular extension does
        /// make sure that there is only ever one active fetching task. Other requests to the cache
        /// while the item is retrieved from the source are grouped together and only every one will
        /// perform the actual async request.
        /// </summary>
        /// <typeparam name="TItem">The type of the cached item</typeparam>
        /// <param name="cache">extension type</param>
        /// <param name="key">The key to lookup</param>
        /// <param name="factory">The factory method</param>
        /// <returns></returns>
        public static Task<TItem> GetOrCreateAtomicAsync<TItem>(this IMemoryCache cache, object key, Func<ICacheEntry, Task<TItem>> factory)
        {
            TaskCompletionSource<TItem> tcs;
            var taskKey = $"$_task$::{key}";

            try
            {
                Synchronization.Wait();

                if (RunningTasks.TryGetValue(taskKey, out var existingTask))
                {
                    return (Task<TItem>)existingTask;
                }

                if (cache.TryGetValue<TItem>(key, out var cachedItem))
                {
                    return Task.FromResult(cachedItem);
                }

                tcs = new TaskCompletionSource<TItem>();
                if (!RunningTasks.TryAdd(taskKey, tcs.Task))
                    throw new InvalidOperationException("Task should never be available here.");
            }
            finally
            {
                Synchronization.Release();
            }

            var ce = cache.CreateEntry(key);
            factory(ce).ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    tcs.SetException(t.Exception);
                }
                else if (t.IsCanceled)
                {
                    tcs.SetCanceled();
                }
                else
                {
                    ce.SetValue(t.Result);
                    tcs.SetResult(t.Result);
                }

                try
                {
                    // this lock is not absolutely required, it makes sure
                    // the initialization phase can run, without having the
                    // _runningTasks to be changed, while checking it and minimizes
                    // the risk on the above to happen
                    Synchronization.Wait();
                    if (!RunningTasks.TryRemove(taskKey, out _))
                        throw new InvalidOperationException("Failed to remove task");
                }
                finally
                {
                    Synchronization.Release();
                }
            });

            return tcs.Task;
        }
    }
}
