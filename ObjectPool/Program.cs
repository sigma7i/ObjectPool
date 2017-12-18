using System;
using System.Threading;

namespace ObjectPool
{
	class Program
	{

		private static TestClassPool testClassPool;

		static void Main(string[] args)
		{
			testClassPool = new TestClassPool(Environment.ProcessorCount);

			Console.WriteLine("Создаются объекты...");

			for (int i = 0; i < 5; i++)
			{
				using (var poolInstance = testClassPool.TakeInstance())
				{

					var classIstance = poolInstance.Object;
					DoSomeWork(classIstance, i);
				}
			}

			Console.WriteLine(testClassPool.ToString());
		}

		static void DoSomeWork(TestClass testClass, int counter)
		{
			if (testClass.Result > 0)
				return;

			if (testClass.Token > 0)
			{
				Thread.Sleep(10); // чтобы заметить разницу Random
				testClass.Result = new Random().Next(1, 10);
			}

			Console.WriteLine($"{counter} Result: {testClass.Result}");
		}



		/// <summary>
		///  Пул конкретных объктов
		/// </summary>
		private class TestClassPool : ObjectPool<TestClass>
		{
			public TestClassPool(int maxCapacity) : base(maxCapacity)
			{
			}

			protected override TestClass ObjectConstructor()
			{
				var instance = new TestClass();
				instance.Token = TestClass.GetToken(); // длительная операция

				return instance;
			}

			protected override void CleanUp(TestClass @object)
			{
				@object.Result = 0;
			}
		}
	}
}
