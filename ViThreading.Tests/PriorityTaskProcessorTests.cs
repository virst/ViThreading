using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViThreading.Tests
{
    public class PriorityTaskProcessorTests
    {
        [Fact]
        public void ExecuteTask_ItemProcessed()
        {
            var processor = new PriorityTaskProcessor<int>(1);
            bool executed = false;

            var task = new Task(() => executed = true);
            processor.AddItem(task, 1);

            Thread.Sleep(100); // Даем время на выполнение
            Assert.True(executed);
            processor.Dispose();
        }

        [Fact]
        public void PriorityOrder_HigherPriorityFirst()
        {
            var processor = new PriorityTaskProcessor<int>(1);
            var results = new ConcurrentQueue<int>();
            var sync = new object();

            processor.AddItem(new Task(() => { lock (sync) results.Enqueue(3); }), 3);
            processor.AddItem(new Task(() => { lock (sync) results.Enqueue(1); }), 1);
            processor.AddItem(new Task(() => { lock (sync) results.Enqueue(2); }), 2);

            Thread.Sleep(200);
            var resultList = results.ToArray();
            Assert.Equal(new[] { 1, 2, 3 }, resultList);
            processor.Dispose();
        }

        [Fact]
        public void WorkerCount_IncreaseDecreaseWorkers()
        {
            var processor = new PriorityTaskProcessor<int>(2);
            Assert.Equal(2, processor.WorkerCount);

            processor.SetWorkerCount(4);
            Assert.Equal(4, processor.WorkerCount);

            processor.SetWorkerCount(1);
            Assert.Equal(1, processor.WorkerCount);

            processor.Dispose();
        }

        [Fact]
        public void ActiveWorkerCount_ReflectsRunningTasks()
        {
            var processor = new PriorityTaskProcessor<int>(2);
            var startSignal = new ManualResetEventSlim();
            var taskStarted = new ManualResetEventSlim();

            // Добавляем длительную задачу
            processor.AddItem(new Task(() =>
            {
                taskStarted.Set();
                startSignal.Wait(); // Блокируем выполнение
            }), 1);

            taskStarted.Wait(1000);
            Thread.Sleep(50); // Даем время для начала выполнения

            Assert.Equal(1, processor.ActiveWorkerCount);

            startSignal.Set(); // Разблокируем задачу
            processor.Dispose();
        }

        [Fact]
        public async Task ErrorHandling_ReportsException()
        {
            Exception? caughtEx = null;
            var processor = new PriorityTaskProcessor<int>(1, ex => caughtEx = ex);

            var task = new Task(() => throw new InvalidOperationException("test"));

            processor.AddItem(task, 1);
            await Task.Delay(100);

            Assert.NotNull(caughtEx);
            Assert.IsType<InvalidOperationException>(caughtEx);
            Assert.Equal("test", caughtEx.Message);

            processor.Dispose();
        }

        [Fact]
        public void Dispose_StopsAllWorkers()
        {
            var processor = new PriorityTaskProcessor<int>(3);
            processor.Dispose();

            Assert.Equal(0, processor.WorkerCount);
        }

        [Fact]
        public void Dispose_RejectsNewItems()
        {
            var processor = new PriorityTaskProcessor<int>(1);
            processor.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                processor.AddItem(new Task(() => { }), 1));
        }

        [Fact]
        public async Task ConcurrentAdd_ProcessesAllTasks()
        {
            var processor = new PriorityTaskProcessor<int>(4);
            var counter = 0;
            var tasks = new List<Task>();

            // 100 задач из 4 потоков
            for (int i = 0; i < 4; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    for (int j = 0; j < 25; j++)
                    {
                        processor.AddItem(new Task(() => Interlocked.Increment(ref counter)), 1);
                    }
                }));
            }

            await Task.WhenAll(tasks.ToArray());
            Thread.Sleep(500); // Даем время на обработку

            Assert.Equal(100, counter);
            processor.Dispose();
        }

        [Fact]
        public void CustomComparer_RespectsCustomPriorityLogic()
        {
            // Используем обратный порядок приоритетов (больше число = выше приоритет)
            var processor = new PriorityTaskProcessor<int>(
                initialWorkers: 1,
                comparer: Comparer<int>.Create((a, b) => b.CompareTo(a))
            );

            var results = new ConcurrentQueue<int>();

            processor.AddItem(new Task(() => results.Enqueue(1)), 1);
            processor.AddItem(new Task(() => results.Enqueue(3)), 3); // Должен выполниться первым
            processor.AddItem(new Task(() => results.Enqueue(2)), 2);

            Thread.Sleep(200);
            var resultList = results.ToArray();

            // С custom comparer порядок должен быть: 3, 2, 1
            Assert.Equal(new[] { 3, 2, 1 }, resultList);
            processor.Dispose();
        }

        [Fact]
        public void ComplexPriorityType_WorksWithCustomType()
        {
            // Тестируем с сложным типом приоритета
            var processor = new PriorityTaskProcessor<string>(
                initialWorkers: 1,
                comparer: Comparer<string>.Create((a, b) => a.Length.CompareTo(b.Length))
            );

            var results = new ConcurrentQueue<string>();

            processor.AddItem(new Task(() => results.Enqueue("longer")), "longer");
            processor.AddItem(new Task(() => results.Enqueue("short")), "short"); // Должен выполниться первым
            processor.AddItem(new Task(() => results.Enqueue("medium")), "medium");

            Thread.Sleep(200);
            var resultList = results.ToArray();

            // Порядок по длине строк: "short" (5), "medium" (6), "longer" (6, но позже)
            Assert.Equal("short", resultList[0]);
            processor.Dispose();
        }

        [Fact]
        public void ReduceWorkers_GracefulShutdown()
        {
            var processor = new PriorityTaskProcessor<int>(3);
            var longTaskRunning = new ManualResetEventSlim();
            var longTaskCompleted = new ManualResetEventSlim();

            // Блокирующая задача
            processor.AddItem(new Task(() =>
            {
                longTaskRunning.Set();
                Thread.Sleep(200);
                longTaskCompleted.Set();
            }), 1);

            longTaskRunning.Wait();
            processor.SetWorkerCount(1); // Уменьшаем количество воркеров

            // Проверяем что задача завершилась нормально
            Assert.True(longTaskCompleted.Wait(500));
            Assert.Equal(1, processor.WorkerCount);
            processor.Dispose();
        }

        enum Priority { Critical, High, Normal, Low }
        [Fact]
        public void EnumPriorityType_WorksCorrectly()
        {
            var processor = new PriorityTaskProcessor<Priority>(1);
            var results = new ConcurrentQueue<Priority>();

            processor.AddItem(new Task(() => results.Enqueue(Priority.Normal)), Priority.Normal);
            processor.AddItem(new Task(() => results.Enqueue(Priority.Critical)), Priority.Critical); // Должен выполниться первым
            processor.AddItem(new Task(() => results.Enqueue(Priority.Low)), Priority.Low);

            Thread.Sleep(200);
            var resultList = results.ToArray();

            // Порядок по enum значениям: Critical, Normal, Low
            Assert.Equal(Priority.Critical, resultList[0]);
            Assert.Equal(Priority.Normal, resultList[1]);
            Assert.Equal(Priority.Low, resultList[2]);
            processor.Dispose();
        }

        [Fact]
        public void TaskWithReturnValue_HandledCorrectly()
        {
            var processor = new PriorityTaskProcessor<int>(1);

            var task = new Task<int>(() => 42);
            processor.AddItem(task, 1);

            Thread.Sleep(100);

            // Проверяем что задача выполнилась и вернула результат
            Assert.Equal(42, task.Result);
            processor.Dispose();
        }

        [Fact]
        public void MultiplePriorityLevels_CorrectOrdering()
        {
            var processor = new PriorityTaskProcessor<int>(1);
            var executionOrder = new ConcurrentQueue<int>();

            // Добавляем задачи с разными приоритетами
            for (int i = 10; i >= 1; i--)
            {
                var priority = i;
                processor.AddItem(new Task(() => executionOrder.Enqueue(priority)), priority);
            }

            Thread.Sleep(500);
            var orderArray = executionOrder.ToArray();

            // Проверяем что задачи выполнились в порядке возрастания приоритета (меньшее число = выше приоритет)
            for (int i = 0; i < orderArray.Length - 1; i++)
            {
                Assert.True(orderArray[i] <= orderArray[i + 1]);
            }
            processor.Dispose();
        }
    }
}
