using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViThreading.Tests;

public class DynamicSemaphoreTestsAdvanced
{
	[Fact]
	public void TryWaitOne_ZeroTimeout_WhenAvailable_ShouldReturnTrue()
	{
		var semaphore = new DynamicSemaphore(1);
		Assert.True(semaphore.TryWaitOne(0));
	}

	[Fact]
	public void TryWaitOne_ZeroTimeout_WhenFull_ShouldReturnFalse()
	{
		var semaphore = new DynamicSemaphore(1);
		semaphore.WaitOne();
		Assert.False(semaphore.TryWaitOne(0));
	}

	[Fact]
	public void TryWaitOne_Timeout_ShouldReturnFalseWhenExpired()
	{
		var semaphore = new DynamicSemaphore(1);
		semaphore.WaitOne(); // Заняли слот

		bool result = semaphore.TryWaitOne(50);
		Assert.False(result);
	}

	[Fact]
	public async Task MultipleThreads_ShouldRespectMaxCount()
	{
		var semaphore = new DynamicSemaphore(2);
		int concurrent = 0;
		var tasks = new List<Task>();
		var sync = new object();

		for (int i = 0; i < 4; i++)
		{
			tasks.Add(Task.Run(() => {
				semaphore.WaitOne();
				lock (sync) concurrent++;

				// Имитация работы
				Thread.Sleep(100);

				lock (sync) concurrent--;
				semaphore.Release();
			}));
		}

		Thread.Sleep(50); // Даем время на запуск
		Assert.True(concurrent <= 2); // Не больше лимита
		await Task.WhenAll(tasks.ToArray());
	}


}

