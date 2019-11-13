using debugger.Samples.Extensions;
using Microsoft.Extensions.Caching.Memory;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace debugerr.Samples.Extensions.Tests
{
    [TestFixture]
    internal static class MemoryCacheExtensionTests
    {
        [Test]
        public static async Task Test_MemoryCacheExtensions_Fetch_Throws()
        {
            var memoryCache = new MemoryCache(new MemoryCacheOptions());

            var tasks = new List<Task>();

            const int tasksCount = 200;
            const int maxTaskScheduleDelay = 10;
            const int maxCacheLoadDelay = 2_000;

            int cacheRefresh = 0;
            var rnd = new Random();

            string DoThrow()
            {
                throw new OperationCanceledException();
            }

            for (var i = 0; i < tasksCount; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    tasks.Add(memoryCache.GetOrCreateAtomicAsync($"cacheItem", async (ce) =>
                    {
                        Interlocked.Increment(ref cacheRefresh);

                        int delay;
                        lock (rnd)
                        {
                            delay = rnd.Next(1, maxCacheLoadDelay);
                        }

                        await Task.Delay(delay);
                        return DoThrow();
                    }));
                }));

                int wait;
                lock (rnd)
                {
                    wait = rnd.Next(1, maxTaskScheduleDelay);
                }

                await Task.Delay(wait);
            }

            using (var cts = new CancellationTokenSource(maxCacheLoadDelay + 1_000))
            {
                try
                {
                    Task.WaitAll(tasks.ToArray(), cts.Token);
                }
                catch (AggregateException) { }
                catch (TaskCanceledException) { }
                catch (OperationCanceledException) { }
            }

            Assert.True(tasks.All(x => x.IsCompleted), "Not all tasks completed");
        }

        [Test]
        public static void Test_MemoryCacheExtensions_ExclusiveGetOrAdd()
        {
            const int itemsCount = 50;
            const int numberOfTasks = 30;

            var memoryCache = new MemoryCache(new MemoryCacheOptions());

            int accessCount = 0;

            async Task<DateTime> CreateDtAsync()
            {
                await Task.Delay(50);
                return DateTime.UtcNow;
            }

            var tasks = new Task[numberOfTasks];

            for (var i = 0; i < numberOfTasks; i++)
            {
                tasks[i] = Task.Run(async () => {

                    for (var j = 0; j < itemsCount; j++)
                    {
                        await memoryCache.GetOrCreateAtomicAsync($"cacheItem::{j}", async (ce) =>
                        {
                            Interlocked.Increment(ref accessCount);
                            ce.SlidingExpiration = TimeSpan.FromMinutes(1);
                            return await CreateDtAsync();
                        });
                    }
                });
            }

            Task.WaitAll(tasks);
            Assert.AreEqual(itemsCount, accessCount);
        }

        [Test]
        public static async Task Test_MemoryCacheExtensions_Expiration_Works()
        {
            var memoryCache = new MemoryCache(new MemoryCacheOptions());

            int accessCount = 0;

            async Task<DateTime> CreateDtAsync()
            {
                await Task.Delay(5);
                return DateTime.UtcNow;
            }

            const int numberOfThreads = 10;
            const int numberOfIterations = 15;
            const int maxExpiration = 100;

            var slimLock = new SemaphoreSlim(1, 1);
            var rnd = new Random();
            var tasks = new List<Task>(numberOfThreads);

            async Task<DateTime> FetchItemFromCache()
            {
                return await memoryCache.GetOrCreateAtomicAsync("cacheItem::expire", async (ce) =>
                {
                    Interlocked.Increment(ref accessCount);
                    await slimLock.WaitAsync();
                    var expiration = rnd.Next(50, maxExpiration);
                    slimLock.Release();
                    ce.AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(expiration);
                    return await CreateDtAsync();
                });
            }

            int doneThreads = 0;
            for (var i = 0; i < numberOfThreads; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    for (var j = 0; j < numberOfIterations; j++)
                    {
                        await FetchItemFromCache();
                        await slimLock.WaitAsync();
                        var delay = rnd.Next(5, maxExpiration);
                        slimLock.Release();
                        await Task.Delay(delay);
                    }

                    Interlocked.Increment(ref doneThreads);
                }));
            }

            await Task.WhenAll(tasks);
            await Task.Delay(maxExpiration);

            accessCount = 0;

            Assert.AreEqual(numberOfThreads, doneThreads);
            var res = await FetchItemFromCache();

            Assert.Less((DateTime.UtcNow - res).TotalMilliseconds, 10);
            Assert.AreEqual(1, accessCount);
        }
    }
}