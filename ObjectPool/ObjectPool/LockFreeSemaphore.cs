using System;
using System.Threading;

namespace ObjectPool
{
	/// <summary>
	/// Быстрая альтернатива semaphore, используя атомарные операции Interlocked
	/// </summary>
	public sealed class LockFreeSemaphore
	{
		private readonly int _maxCount;
		private int _currentCount;

		/// <summary>
		/// Инициализирует новый экземпляр класса LockFreeSemaphore
		/// </summary>
		/// <param name="initialCount">Исходное количество запросов, которые могут быть предоставлены одновременно.</param>
		/// <param name="maxCount">Максимальное количество запросов, которые могут быть предоставлены одновременно.</param>
		public LockFreeSemaphore(int initialCount, int maxCount)
		{
			if (initialCount < 0 || maxCount < initialCount)
				throw new ArgumentOutOfRangeException("initialCount");
			if (maxCount <= 0)
				throw new ArgumentOutOfRangeException("maxCount");

			_currentCount = initialCount;
			_maxCount = maxCount;
		}

		/// <summary>
		/// Получает максимальное количество потоков, которые могут быть предоставлены одновременно
		/// </summary>
		public int MaxCount
		{
			get { return _maxCount; }
		}

		/// <summary>
		/// Возвращает текущее количество потоков, которые могут быть предоставлены одновременно.
		/// </summary>
		public int CurrentCount
		{
			get { return _currentCount; }
		}

		/// <summary>
		/// Попытка войти в семафор
		/// </summary>
		/// <returns>true, если поток вошел успешно, иначе false</returns>
		public bool TryTake()
		{
			int oldValue, newValue;
			do
			{
				oldValue = _currentCount;
				newValue = oldValue - 1;
				if (newValue < 0) return false;
			} while (Interlocked.CompareExchange(ref _currentCount, newValue, oldValue) != oldValue);
			return true;
		}

		/// <summary>
		/// Выход из семафора, освобождая одно место.
		/// </summary>
		public void Release()
		{
			int oldValue, newValue;
			do
			{
				oldValue = _currentCount;
				newValue = oldValue + 1;
				if (newValue > _maxCount)
					throw new SemaphoreFullException();
			} while (Interlocked.CompareExchange(ref _currentCount, newValue, oldValue) != oldValue);
		}

		/// <summary>
		/// Выходит из семафора определенное количество раз.
		/// </summary>
		/// <param name="releaseCount">Количество раз, чтобы выйти из семафора.</param>
		public void Release(int releaseCount)
		{
			if (releaseCount < 1)
				throw new ArgumentOutOfRangeException("releaseCount", "Release сount is less than 1");

			int oldValue, newValue;
			do
			{
				oldValue = _currentCount;
				newValue = oldValue + releaseCount;
				if (newValue > _maxCount)
					throw new SemaphoreFullException();
			} while (Interlocked.CompareExchange(ref _currentCount, newValue, oldValue) != oldValue);
		}

		public override string ToString()
		{
			return string.Format("{0}: {1}/{2}", typeof(LockFreeSemaphore).Name, _currentCount, _maxCount);
		}
	}
}
