namespace ViThreading
{
    /// <summary>
    /// Обрабатывает действия с приоритетами, используя пул рабочих потоков.
    /// Позволяет динамически управлять количеством рабочих потоков.
    /// </summary>
    public class PriorityActionProcessor
    {
        private class Worker : IDisposable
        {
            public readonly Task WorkerTask;
            private readonly CancellationTokenSource WorkerCts;
            private readonly PriorityQueue<Action, int> _queue;
            private readonly Action<Exception>? _errorHandler;

            public Worker(PriorityQueue<Action, int> queue, Action<Exception>? errorHandler = null)
            {
                _queue = queue;
                _errorHandler = errorHandler;

                WorkerCts = new CancellationTokenSource();
                WorkerTask = Task.Run(() => WorkerLoop(WorkerCts.Token));
            }

            private async Task WorkerLoop(CancellationToken ct)
            {
                try
                {
                    while (true)
                    {
                        try
                        {
                            bool hasItems = false;
                            Action? item = default;
                            lock (_queue)
                            {
                                hasItems = _queue.TryDequeue(out item, out var priority);
                            }
                            if (hasItems && item != null)
                                item();
                            else
                                await Task.Delay(50);
                        }
                        catch (Exception ex)
                        {
                            try { _errorHandler?.Invoke(ex); }
                            catch { /* Ignore handler errors */ }
                        }

                        // Проверяем отмену после обработки каждого элемента
                        if (ct.IsCancellationRequested) break;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Ожидаемое при отмене
                }

            }

            public void Cancel()
            {
                WorkerCts.Cancel();
            }

            public void Dispose()
            {
                WorkerCts.Dispose();
            }
        }


        private readonly PriorityQueue<Action, int> _queue = new();
        private readonly List<Worker> _workers = new();
        private readonly object _lock = new();
        private readonly Action<Exception>? _errorHandler;
        private bool _disposed = false;

        /// <summary>
        /// Текущее количество активных рабочих потоков.
        /// </summary>
        public int WorkerCount => _workers.Count;

        /// <summary>
        /// Инициализирует процессор с указанным количеством потоков.
        /// </summary>
        /// <param name="initialWorkers">Начальное количество рабочих потоков.</param>
        /// <param name="errorHandler">Обработчик ошибок для действий (опционально).</param>
        /// <exception cref="ArgumentOutOfRangeException">Если initialWorkers отрицательное.</exception>
        public PriorityActionProcessor(int initialWorkers, Action<Exception>? errorHandler = null)
        {
            _errorHandler = errorHandler;
            SetWorkerCount(initialWorkers);
        }

        /// <summary>
        /// Добавляет действие в очередь на выполнение с указанным приоритетом.
        /// </summary>
        /// <param name="item">Действие для выполнения.</param>
        /// <param name="priority">Приоритет выполнения (меньше значение = выше приоритет).</param>
        /// <exception cref="ObjectDisposedException">Если процессор уже уничтожен.</exception>

        public void AddItem(Action item, int priority)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_queue)
            {
                _queue.Enqueue(item, priority);
            }
        }

        /// <summary>
        /// Текущее количество элементов в очереди на обработку.
        /// </summary>
        public int ItemsCount => _queue.Count;

        /// <summary>
        /// Динамически изменяет количество рабочих потоков.
        /// </summary>
        /// <param name="newCount">Новое количество потоков.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если newCount отрицательное.</exception>
        /// <remarks>
        /// При уменьшении количества потоков останавливает последние добавленные потоки.
        /// При увеличении - добавляет новые потоки.
        /// </remarks>
        public void SetWorkerCount(int newCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(newCount);
            if (newCount == _workers.Count) return; // Нет изменений, ничего не делаем

            lock (_lock)
            {
                // Увеличиваем количество воркеров
                if (newCount > _workers.Count)
                {
                    for (int i = _workers.Count; i < newCount; i++)
                    {
                        _workers.Add(new Worker(_queue, _errorHandler));
                    }
                }
                // Уменьшаем количество воркеров
                else if (newCount < _workers.Count)
                {
                    int removeCount = _workers.Count - newCount;
                    var tasksToRemove = _workers.GetRange(_workers.Count - removeCount, removeCount);
                    var tasksToWait = tasksToRemove.Select(t => t.WorkerTask);

                    // Инициируем отмену для последних workers
                    tasksToRemove.ForEach(t => t.Cancel());
                    Task.WaitAll(tasksToWait.ToArray(), Timeout.Infinite); // Синхронное ожидание
                    tasksToRemove.ForEach(t => t.Dispose());

                    // Удаляем из отслеживаемых списков              
                    _workers.RemoveRange(_workers.Count - removeCount, removeCount);
                }
            }
        }

        /// <summary>
        /// Останавливает все рабочие потоки и освобождает ресурсы.
        /// </summary>
        public void Dispose()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _disposed = true;

            foreach (var worker in _workers) worker.Cancel();

            Task.WaitAll([.. _workers.Select(w => w.WorkerTask)]);
            foreach (var worker in _workers) worker.Dispose();

            _workers.Clear();

        }
    }
}
