using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ObjectPool
{
	/// <summary>
	/// Потокобезопасный пул для переиспользуемых объектов.
	/// </summary>
	public abstract class ObjectPool<T> where T : class
	{
		private readonly ConcurrentStack<PoolInstance<T>> _storage; // потокобезопасное хранилище объектов "в пуле"
		private readonly LockFreeSemaphore _allocSemaphore; // легкий семафор для распределения операций

		private int _currentCount;

		/// <summary>
		/// Инициализирует новый экземпляр пула с указанным верхним пределом.
		/// </summary>
		protected ObjectPool(int maxCapacity)
		{
			if (maxCapacity < 1)
				throw new ArgumentOutOfRangeException("maxCapacity", "Максимальная емкость должна быть больше 0");

			_storage = new ConcurrentStack<PoolInstance<T>>();
			_allocSemaphore = new LockFreeSemaphore(maxCapacity, maxCapacity);
		}

		/// <summary>
		/// Инициализирует новый экземпляр пула с первоначальной инициализцией и указанным верхним пределом.
		/// </summary>
		/// <param name="initialCount">Заранее создает заданное колчиество экземпляров класса T</param>
		protected ObjectPool(int initialCount, int maxCapacity) : this(maxCapacity)
		{
			if (initialCount < 0)
				throw new ArgumentOutOfRangeException("initialCount", "Начальная емкость не должна быть отрицательной");
			if (initialCount > maxCapacity)
				throw new ArgumentOutOfRangeException("initialCount", "Начальная емкость не может быть больше максимальной");

			TryAllocatePush(initialCount);
		}

		#region Public members

		/// <summary>
		/// Тайм-аут очистки объектов из пула в секундах(по умолчанию 30)
		/// </summary>
		public int FlushTimeOut = 30;

		/// <summary>
		/// Получает текущее количество доступных объектов в пуле.
		/// </summary>
		public int CurrentCount
		{
			get { return _currentCount; }
		}

		/// <summary>
		/// Получает число объектов, созданное в пуле.
		/// </summary>
		public int TotalCount
		{
			get { return _allocSemaphore.MaxCount - _allocSemaphore.CurrentCount; }
		}

		/// <summary>
		/// Получает доступный экземпляр объекта из пула или создает новый.
		/// </summary>
		public PoolInstance<T> TakeInstance()
		{
			PoolInstance<T> instance;
			if (TryPop(out instance))
				return instance;
			if (TryAllocatePop(out instance))
				return instance;
			return WaitPop();
		}

		/// <summary>
		/// Помещает объект обратно в пул.
		/// </summary>
		public void Release(PoolInstance<T> instance)
		{
			if (instance == null)
				throw new ArgumentNullException("instance");
			if (instance.GetStatus(this))
				throw new InvalidOperationException("Указанный объект уже находится в пуле");

			CleanUp(instance.Object);
			Push(instance);
		}

		/// <summary>
		/// Попытка уменьшить количество выделенных объектов.
		/// </summary>
		/// <param name="targetTotalCount">Целевое минимальное количество объектов</param>
		/// <returns>true, если целевое минимальное количество объектов было достигнуто, иначе false</returns>
		public bool TryReduceTotal(int targetTotalCount)
		{
			if (targetTotalCount < 0)
				throw new ArgumentOutOfRangeException("targetTotalCount");
			if (targetTotalCount > TotalCount)
				throw new ArgumentOutOfRangeException("targetTotalCount");

			var removingCount = TotalCount - targetTotalCount;
			for (var i = 0; i < removingCount; i++)
				if (!TryPopRemove())
					return TotalCount == targetTotalCount;
			return TotalCount == targetTotalCount;
		}

		/// <summary>
		///  Очищает пул от всех созданных экземпляров, применяя к каждому алгоритм уничтожения
		/// </summary>
		/// <exception cref="TimeoutException">Возникает если объект не вернулся в пул, либо если слишком долгое уничтожение</exception>
		/// <remarks>Объекты которые не вернулись в пул, будет ожидать Release до истечения TimeOut</remarks>
		public void TryFlush(Action<T> destroyer)
		{
			Task flushingTask = Task.Factory.StartNew(() => Flushing(destroyer));
			var timeoutMilliseconds = FlushTimeOut * 1000;

			var isCompleted = flushingTask.Wait(timeoutMilliseconds);

			if (!isCompleted)
				throw new TimeoutException($"Время очищения пула объектов {typeof(T).Name} истекло(default {FlushTimeOut} seconds)");
		}

		/// <summary>
		///  Очищает пул от всех созданных экземпляров, применяя к каждому экземпляру Dispose(если поддерживает)
		/// </summary>
		/// <exception cref="TimeoutException">TimeoutException</exception>
		/// <remarks>Объекты которые не вернулись в пул, будет ожидать Release до истечения TimeOut</remarks>
		public void TryFlush()
		{
			TryFlush(Destroyer);
		}

		/// <summary>
		///  Очищает пул от всех созданных экземпляров, применяя к каждому алгоритм уничтожения
		/// </summary>
		private void Flushing(Action<T> destroyer)
		{
			while (TotalCount > 0)
			{
				PoolInstance<T> instance;
				if (!TryPop(out instance))
					instance = WaitPop();

				destroyer(instance.Object);

				_allocSemaphore.Release();
			}
		}

		/// <summary>
		///  Применяет Dispose для объектов поддерживающих интерфейс IDisposable
		/// </summary>
		private void Destroyer(T obj)
		{
			var disposableObj = obj as IDisposable;
			if (disposableObj != null)
				disposableObj.Dispose();
		}

		/// <summary>
		/// Ожидает, что пул высвободит все объекты.
		/// Обеспечивает, чтобы все объекты были выпущены до их возвращения.
		/// </summary>
		public void WaitAll()
		{
			while (_currentCount != TotalCount)
				Wait();
		}

		/// <summary>
		/// {Название пула}: {Объектов в работе}/{всего создано объектов}/{Максимально возможное количество объектов}
		/// </summary>
		public override string ToString()
		{
			return string.Format("{0}: {1}/{2}/{3}", GetType().Name, _currentCount,
								 TotalCount, _allocSemaphore.MaxCount);
		}

		#endregion

		#region Pool operations

		/// <summary>
		/// Попытка создать и добавить указанное количество экземпляров в пул.
		/// </summary>
		/// <param name="count">Количество объектов для добавления</param>
		protected bool TryAllocatePush(int count)
		{
			for (var i = 0; i < count; i++)
				if (!TryAllocatePush())
					return false;
			return true;
		}

		/// <summary>
		/// Попытка создать и добавить новый экземпляр в пул.
		/// </summary>
		protected bool TryAllocatePush()
		{
			if (_allocSemaphore.TryTake())
			{
				Push(Allocate());
				return true;
			}
			return false;
		}

		/// <summary>
		/// Попытка создать, зарегистрировать статус «Вне пула» и вернуть новый экземпляр
		/// </summary>
		/// <returns>true, если операция была успешно выполнена, в противном случае - false</returns>
		protected bool TryAllocatePop(out PoolInstance<T> instance)
		{
			if (_allocSemaphore.TryTake())
			{
				instance = Allocate();
				return true;
			}

			instance = null;
			return false;
		}

		protected bool TryPopRemove()
		{
			PoolInstance<T> instance;
			if (TryPop(out instance))
			{
				_allocSemaphore.Release();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Ожиданияе свободного экземпляра
		/// </summary>
		protected PoolInstance<T> WaitPop()
		{
			PoolInstance<T> instance;
			while (!TryPop(out instance))
				Wait();
			return instance;
		}

		/// <summary>
		/// Предоставляет задержку для других операций пула
		/// </summary>
		protected void Wait()
		{
			if (!Thread.Yield())
				Thread.Sleep(100);
		}

		private PoolInstance<T> Allocate()
		{
			var obj = ObjectConstructor();
			var instance = new PoolInstance<T>(obj, this);
			return instance;
		}

		#endregion

		#region For overriding

		/// <summary>
		/// Инициализирует новый объект, готовый к размещению в пуле
		/// </summary>
		protected abstract T ObjectConstructor();

		/// <summary>
		/// Обеспечивает очистку объекта перед возвратом в пул
		/// </summary>
		protected virtual void CleanUp(T @object)
		{
		}

		#endregion

		#region Storage wrappers

		private void Push(PoolInstance<T> instance)
		{
			instance.SetStatus(true);
			_storage.Push(instance);
			Interlocked.Increment(ref _currentCount);
		}

		private bool TryPop(out PoolInstance<T> instance)
		{
			if (_storage.TryPop(out instance))
			{
				Interlocked.Decrement(ref _currentCount);
				instance.SetStatus(false);
				return true;
			}
			instance = null;
			return false;
		}

		#endregion
	}
}
