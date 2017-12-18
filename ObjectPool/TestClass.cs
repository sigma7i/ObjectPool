using System;
using System.Threading;

namespace ObjectPool
{
	public sealed class TestClass
	{
		public int Token { get; set; }

		public int Result { get; set; }

		public static int GetToken()
		{
			Thread.Sleep(3000); // эмулируем длительную операцию

			return new Random().Next(1, 100);
		}
	}
}
