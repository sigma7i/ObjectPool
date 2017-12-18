using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MSTestExtensions;

namespace ObjectPool.Tests
{
	/// <summary>
	///  Тесты для пула объектов
	/// </summary>
	[TestClass]
	public class PoolTests
	{
		[TestMethod]
		public void SemaphoreSingleThreadTest()
		{
			var semaphore = new LockFreeSemaphore(0, 3);
			Assert.AreEqual(0, semaphore.CurrentCount);

			semaphore.Release();
			Assert.AreEqual(1, semaphore.CurrentCount);

			semaphore.Release(2);
			Assert.AreEqual(3, semaphore.CurrentCount);

			ThrowsAssert.Throws<SemaphoreFullException>(semaphore.Release);

			Assert.IsTrue(semaphore.TryTake());
			Assert.AreEqual(2, semaphore.CurrentCount);

			Assert.IsTrue(semaphore.TryTake());
			Assert.IsTrue(semaphore.TryTake());
			Assert.AreEqual(0, semaphore.CurrentCount);

			Assert.IsFalse(semaphore.TryTake());
			Assert.AreEqual(0, semaphore.CurrentCount);
		}

		[TestMethod]
		public void SemaphoreMultiThreadTest()
		{
			const int threadCount = 25;
			const int iterations = 25;
			const int resourceCount = 4;

			var semaphore = new LockFreeSemaphore(resourceCount, int.MaxValue);
			Assert.AreEqual(resourceCount, semaphore.CurrentCount);

			var factory = new TaskFactory(TaskScheduler.Default);
			ThreadPool.QueueUserWorkItem(state => { });
			var tasks = new Task[threadCount];
			var currentTreadCount = 0;
			var fails = 0;

			for (var t = 0; t < threadCount; t++)
			{
				tasks[t] = factory.StartNew(
					() =>
					{
						for (var i = 0; i < iterations; i++)
						{
							if (semaphore.TryTake())
							{
								Interlocked.Increment(ref currentTreadCount);
								Assert.IsTrue(currentTreadCount <= resourceCount);
								Thread.Sleep(10);
								Interlocked.Decrement(ref currentTreadCount);
								semaphore.Release();
							}
							else
							{
								Interlocked.Increment(ref fails);
							}
						}
					});
			}
			Task.WaitAll(tasks);
			Debug.WriteLine("Fails: " + fails);
			Assert.AreEqual(resourceCount, semaphore.CurrentCount);
		}

		[TestMethod]
		public void PoolCreation()
		{
			var pool = new ThirdPartyPool(0, 3);
			pool.TakeInstance();
			pool.TakeInstance();
			pool.TakeInstance();
			Assert.AreEqual(3, pool.TotalCount);

			pool = new ThirdPartyPool(100, 100);
			Assert.AreEqual(100, pool.TotalCount);
			Assert.AreEqual(100, pool.CurrentCount);
		}

		[TestMethod]
		public void PoolFlushing()
		{
			var pool = new ThirdPartyPool(0, 3);
			var insanceArray = new List<PoolInstance<ThirdParty>>();

			insanceArray.Add(pool.TakeInstance());
			insanceArray.Add(pool.TakeInstance());
			insanceArray.Add(pool.TakeInstance());
			Assert.AreEqual(3, pool.TotalCount);

			insanceArray.ForEach(insance => pool.Release(insance));

			pool.TryFlush();
			insanceArray.Clear();
			Assert.IsTrue(insanceArray.All(inst => inst.Object.Disposed));
			Assert.AreEqual(0, pool.TotalCount);

			// recycle pool and manual flush
			insanceArray.Add(pool.TakeInstance());
			insanceArray.Add(pool.TakeInstance());

			Assert.AreEqual(2, pool.TotalCount);
			insanceArray.ForEach(insance => pool.Release(insance));

			Action<ThirdParty> destroyer = obj =>
			{
				obj.ManualDisposed = true;
			};

			pool.TryFlush(destroyer);

			Assert.IsTrue(insanceArray.All(inst => inst.Object.ManualDisposed));
			Assert.AreEqual(0, pool.TotalCount);
		}

		[TestMethod]
		public void PoolOneThreadScenario()
		{
			const int iterations = 100;
			const int initialCount = 5;

			var pool = new ThirdPartyPool(initialCount, 50);
			var item = pool.TakeInstance();
			pool.Release(item);
			Assert.AreEqual(initialCount, pool.TotalCount);
			Assert.AreEqual(initialCount, pool.CurrentCount);

			ThrowsAssert.Throws<ArgumentException>(() => pool.Release(new ThirdPartyPool(1, 1).TakeInstance()));
			ThrowsAssert.Throws<InvalidOperationException>(() => pool.Release(item));

			for (var i = 0; i < iterations; i++)
			{
				using (var slot = pool.TakeInstance())
				{
					Assert.IsFalse(slot.Object.Flag);
					slot.Object.Flag = true;
					Assert.AreEqual(initialCount, pool.TotalCount);
					Assert.AreEqual(initialCount - 1, pool.CurrentCount);
				}
			}
			Assert.AreEqual(initialCount, pool.TotalCount);
			Assert.AreEqual(initialCount, pool.CurrentCount);
		}

		[TestMethod]
		public void PoolMultiThreadsScenario()
		{
			const int iterations = 50;
			const int threadCount = 50;

			var pool = new ThirdPartyPool(10, 50);
			MultiThreadsScenario(threadCount, iterations, pool);
			Thread.Sleep(100);
			pool.WaitAll();
			Assert.IsTrue(threadCount >= pool.TotalCount);

			Debug.WriteLine(pool.TotalCount);
		}

		[TestMethod]
		public void PoolMaxCapacity()
		{
			const int capacity0 = 1;
			const int capacity1 = 25;
			const int iterations = 100;
			const int taskCount = 25;

			var pool0 = new ThirdPartyPool(capacity0, capacity0);
			var sw = Stopwatch.StartNew();
			MultiThreadsScenario(taskCount, iterations, pool0);
			pool0.WaitAll();
			Debug.WriteLine(sw.Elapsed);
			Assert.AreEqual(capacity0, pool0.TotalCount);

			var pool1 = new ThirdPartyPool(capacity1, capacity1);
			sw.Restart();
			MultiThreadsScenario(taskCount, iterations, pool1);
			pool1.WaitAll();
			Debug.WriteLine(sw.Elapsed);
			Assert.AreEqual(capacity1, pool1.TotalCount);
		}

		[TestMethod]
		public void PoolReduceTotalCount()
		{
			var pool = new ThirdPartyPool(100, 100);
			Assert.IsTrue(pool.TryReduceTotal(10));
			Assert.AreEqual(10, pool.TotalCount);

			var item = pool.TakeInstance();
			Assert.IsFalse(pool.TryReduceTotal(0));
			Assert.AreEqual(1, pool.TotalCount);
		}

		private void MultiThreadsScenario(int threadCount, int iterations, ObjectPool<ThirdParty> pool)
		{
			var factory = new TaskFactory(TaskScheduler.Default);
			ThreadPool.QueueUserWorkItem(state => { });
			var tasks = new Task[threadCount];

			for (var t = 0; t < threadCount; t++)
			{
				tasks[t] = factory.StartNew(
					() =>
					{
						for (var i = 0; i < iterations; i++)
						{
							using (var slot = pool.TakeInstance())
							{
								Assert.IsFalse(slot.Object.Flag);
								slot.Object.Flag = true;
							}
						}
					}
					);
			}

			Task.WaitAll(tasks);
		}

		/// <summary>
		///  "Запечатанный" класс
		/// </summary>
		internal sealed class ThirdParty : IDisposable
		{
			public bool Flag { get; set; }

			public bool Disposed { get; set; }

			public bool ManualDisposed { get; set; }

			public void Dispose()
			{
				Disposed = true;
			}
		}

		/// <summary>
		///  Класс пула объектов для класса ThirdParty
		/// </summary>
		/// <remarks>Можнно заметить что класс ThirdPartyPool не затрагивает реализацию ThirdParty</remarks>
		internal sealed class ThirdPartyPool : ObjectPool<ThirdParty>
		{
			public ThirdPartyPool(int initialCount, int maxCapacity)
				: base(initialCount, maxCapacity)
			{
			}

			protected override ThirdParty ObjectConstructor()
			{
				return new ThirdParty();
			}

			protected override void CleanUp(ThirdParty @object)
			{
				@object.Flag = false;
			}
		}
	}
}
