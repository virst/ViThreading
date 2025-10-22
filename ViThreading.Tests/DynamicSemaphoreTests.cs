namespace ViThreading.Tests;

public class DynamicSemaphoreTests
{
    [Fact]
    public void WaitOne_ShouldIncrementUsedCount()
    {
        var semaphore = new DynamicSemaphore(1);
        semaphore.WaitOne();
        Assert.Equal(1, semaphore.UsedCount);
    }

    [Fact]
    public void Release_ShouldDecrementUsedCount()
    {
        var semaphore = new DynamicSemaphore(1);
        semaphore.WaitOne();
        semaphore.Release();
        Assert.Equal(0, semaphore.UsedCount);
    }

    [Fact]
    public void Release_WithoutAcquire_ShouldThrow()
    {
        var semaphore = new DynamicSemaphore(1);
        Assert.Throws<SemaphoreFullException>(() => semaphore.Release());
    }

    [Theory]
    [InlineData(5, 3, 2)]
    [InlineData(3, 3, 0)] // AvailableCount не может быть отрицательным
    public void AvailableCount_ShouldBeCorrect(int maxCount, int acquires, int expected)
    {
        var semaphore = new DynamicSemaphore(maxCount);
        for (int i = 0; i < acquires; i++) semaphore.WaitOne();
        Assert.Equal(expected, semaphore.AvailableCount);
    }

    [Fact]
    public void MaximumCount_Setter_ShouldUpdateValue()
    {
        var semaphore = new DynamicSemaphore(1);
        semaphore.MaximumCount = 5;
        Assert.Equal(5, semaphore.MaximumCount);
    }

    [Fact]
    public void ReduceMaxCount_BelowUsedCount_ShouldBlockNewWaiters()
    {
        var semaphore = new DynamicSemaphore(3);
        semaphore.WaitOne();
        semaphore.WaitOne(); // UsedCount=2

        semaphore.MaximumCount = 1; // Устанавливаем ниже занятых слотов

        bool acquired = semaphore.TryWaitOne(0);
        Assert.False(acquired); // Новые запросы блокируются
        Assert.Equal(0, semaphore.AvailableCount);
    }

    [Fact]
    public void IncreaseMaxCount_ShouldUnlockWaiters()
    {
        var semaphore = new DynamicSemaphore(1);
        semaphore.WaitOne(); // Заняли единственный слот

        bool isReleased = false;
        var thread = new Thread(() => {
            semaphore.WaitOne();
            isReleased = true;
        });
        thread.Start();

        Thread.Sleep(50); // Даем время на блокировку
        semaphore.MaximumCount = 2; // Увеличиваем лимит
        Thread.Sleep(50); // Даем время на разблокировку

        Assert.True(isReleased);
        thread.Join();
    }
}
