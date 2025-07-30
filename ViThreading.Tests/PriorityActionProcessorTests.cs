namespace ViThreading.Tests;

public class PriorityActionProcessorTests
{
    [Fact]
    public void ExecuteAction_ItemProcessed()
    {
        var processor = new PriorityActionProcessor(1);
        bool executed = false;

        processor.AddItem(() => executed = true, priority: 1);
        Thread.Sleep(100); // Даем время на выполнение

        Assert.True(executed);
    }

    [Fact]
    public void PriorityOrder_HigherPriorityFirst()
    {
        var processor = new PriorityActionProcessor(1);
        var results = new List<int>();
        var sync = new object();

        processor.AddItem(() => { lock (sync) results.Add(3); }, 3);
        processor.AddItem(() => { lock (sync) results.Add(1); }, 1);
        processor.AddItem(() => { lock (sync) results.Add(2); }, 2);

        Thread.Sleep(200);
        Assert.Equal(new[] { 1, 2, 3 }, results);
    }

    [Fact]
    public void WorkerCount_IncreaseDecreaseWorkers()
    {
        var processor = new PriorityActionProcessor(2);
        Assert.Equal(2, processor.WorkerCount);

        processor.SetWorkerCount(4);
        Assert.Equal(4, processor.WorkerCount);

        processor.SetWorkerCount(1);
        Assert.Equal(1, processor.WorkerCount);
    }

    [Fact]
    public void ErrorHandling_ReportsException()
    {
        Exception? caughtEx = null;
        var processor = new PriorityActionProcessor(1, ex => caughtEx = ex);

        processor.AddItem(() => throw new InvalidOperationException("test"), 1);
        Thread.Sleep(100);

        Assert.NotNull(caughtEx);
        Assert.IsType<InvalidOperationException>(caughtEx);
        Assert.Equal("test", caughtEx.Message);
    }

    [Fact]
    public void Dispose_StopsAllWorkers()
    {
        var processor = new PriorityActionProcessor(3);
        processor.Dispose();

        Assert.Equal(0, processor.WorkerCount);
    }

    [Fact]
    public void Dispose_RejectsNewItems()
    {
        var processor = new PriorityActionProcessor(1);
        processor.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            processor.AddItem(() => { }, 1));
    }

    [Fact]
    public async Task ConcurrentAdd_ProcessesAllItems()
    {
        var processor = new PriorityActionProcessor(4);
        var counter = 0;
        var tasks = new List<Task>();

        // 100 задач из 4 потоков
        for (int i = 0; i < 4; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 25; j++)
                {
                    processor.AddItem(() => Interlocked.Increment(ref counter), 1);
                }
            }));
        }

        await Task.WhenAll(tasks.ToArray());
        Thread.Sleep(500); // Даем время на обработку

        Assert.Equal(100, counter);
    }

    [Fact]
    public void ReduceWorkers_CancelsWorkers()
    {
        var processor = new PriorityActionProcessor(3);
        var startSignal = new ManualResetEventSlim();
        var longTaskRunning = false;

        // Блокирующая задача
        processor.AddItem(() =>
        {
            longTaskRunning = true;
            startSignal.Set();
            Thread.Sleep(1000);
        }, 1);

        startSignal.Wait();
        processor.SetWorkerCount(1);

        Assert.True(longTaskRunning);
        Assert.Equal(1, processor.WorkerCount);
    }

    [Fact]
    public void AddItem_WakesSleepingWorker()
    {
        var processor = new PriorityActionProcessor(1);
        var executed = false;

        // Дождаться перехода в режим ожидания
        Thread.Sleep(100);

        processor.AddItem(() => executed = true, 1);
        Thread.Sleep(50);

        Assert.True(executed);
    }
}

