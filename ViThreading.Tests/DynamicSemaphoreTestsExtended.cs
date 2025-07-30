namespace ViThreading.Tests
{
    public class DynamicSemaphoreTestsExtended
    {
        [Fact]
        public void NegativeMaxCount_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new DynamicSemaphore(-1));

            var semaphore = new DynamicSemaphore(1);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                semaphore.MaximumCount = -5);
        }

        [Fact]
        public void NegativeTimeout_ShouldThrow()
        {
            var semaphore = new DynamicSemaphore(1);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                semaphore.TryWaitOne(-10));
        }

        [Fact]
        public void MaxCountZero_ShouldBlockAllWaiters()
        {
            var semaphore = new DynamicSemaphore(0);
            Assert.False(semaphore.TryWaitOne(0));

            bool acquired = false;
            var thread = new Thread(() => acquired = semaphore.TryWaitOne(50));
            thread.Start();
            thread.Join();

            Assert.False(acquired);
        }

        [Fact]
        public void Release_ShouldNormalizeAfterMaxCountReduction()
        {
            var semaphore = new DynamicSemaphore(3);
            semaphore.WaitOne();
            semaphore.WaitOne();
            semaphore.MaximumCount = 1; // Уменьшаем лимит

            semaphore.Release(); // UsedCount=1
            Assert.Equal(1, semaphore.UsedCount);
            Assert.Equal(0, semaphore.AvailableCount); // Лимит=1, занято=1

            semaphore.Release(); // Теперь лимит=1, занято=0
            Assert.Equal(0, semaphore.UsedCount);
            Assert.Equal(1, semaphore.AvailableCount);
        }
    }
}
