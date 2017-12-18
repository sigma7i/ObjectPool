using System;

namespace ObjectPool
{
	/// <summary>
	/// Экземпляр объекта в пуле
	/// </summary>
	public sealed class PoolInstance<T> : IDisposable where T : class
	{
		private readonly T _object;
		private readonly ObjectPool<T> _pool;

		private bool _inPool;

		#region Только для использования ObjectPool <T>

		/// <summary>
		/// Инициализирует новый экземпляр для указанного объекта и пула.
		/// </summary>
		internal PoolInstance(T @object, ObjectPool<T> pool)
		{
			_object = @object;
			_pool = pool;
		}

		/// <summary>
		/// Возвращает значение флажка доступности и проверки пула.
		/// </summary>
		/// <returns>true, если элемент «в пуле», overwise, false</returns>
		internal bool GetStatus(ObjectPool<T> pool)
		{
			if (_pool != pool)
				throw new ArgumentException("Этот экземпляр не для указанного пула", "pool");
			return _inPool;
		}

		/// <summary>
		/// Устанавливает значение флажка доступности.
		/// </summary>
		/// <param name="inPool">true, если элемент «в пуле», overwise, false</param>
		internal void SetStatus(bool inPool)
		{
			_inPool = inPool;
		}

		#endregion

		/// <summary>
		/// Исходный сохраненный объект в пуле
		/// </summary>
		public T Object
		{
			get { return _object; }
		}

		/// <summary>
		/// Возвращает объект обратно в пул
		/// </summary>
		public void Dispose()
		{
			_pool.Release(this);
		}
	}
}
