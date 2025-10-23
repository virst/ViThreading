namespace ViThreading
{
    /// <summary>
    /// Обрабатывает действия с приоритетами, используя пул рабочих потоков.
    /// Позволяет динамически управлять количеством рабочих потоков.
    /// </summary>
    public class PriorityActionProcessor(int initialWorkers, Action<Exception>? errorHandler = null)
        : PriorityActionProcessor<int>(initialWorkers, null, errorHandler);


    /// <summary>
    /// Обрабатывает действия с приоритетами, используя пул рабочих потоков.
    /// Позволяет динамически управлять количеством рабочих потоков.
    /// </summary>
    /// <typeparam name="T">Тип приоритета задач</typeparam>
    public class PriorityActionProcessor<T>
    {
        /// <summary>
        /// Внутренний класс рабочего потока, обрабатывающего задачи из очереди
        /// </summary>
        private class Worker : IDisposable
        {
            public readonly Thread WorkerTask;
            private readonly CancellationTokenSource _workerCts;
            private readonly PriorityQueue<PriorityTask, T> _queue;
            private readonly Action<Exception>? _errorHandler;

            /// <summary>
            /// Флаг, указывающий активен ли воркер в данный момент (выполняет задачу)
            /// </summary>
            public bool IsActive { get; private set; }

            /// <summary>
            /// Создает и запускает рабочий поток
            /// </summary>
            /// <param name="queue">Общая очередь задач</param>
            /// <param name="errorHandler">Обработчик ошибок (опционально)</param>
            public Worker(PriorityQueue<PriorityTask, T> queue, Action<Exception>? errorHandler = null)
            {
                _queue = queue;
                _errorHandler = errorHandler;
                IsActive = false;

                _workerCts = new CancellationTokenSource();
                WorkerTask = new Thread(() => WorkerLoop(_workerCts.Token))
                {
                    IsBackground = true
                };
                WorkerTask.Start();
            }

            /// <summary>
            /// Основной цикл обработки задач рабочим потоком
            /// </summary>
            /// <param name="ct">Токен отмены для graceful shutdown</param>
            private void WorkerLoop(CancellationToken ct)
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            bool hasItems;
                            PriorityTask? task;
                            T? priority;

                            // Если очередь пуста - ждем перед следующей проверкой
                            if (_queue.Count == 0)
                            {
                                Thread.Sleep(50);
                                continue;
                            }

                            // Безопасное извлечение задачи из очереди
                            lock (_queue)
                            {
                                hasItems = _queue.TryDequeue(out task, out priority);
                            }

                            // Если задача извлечена - выполняем ее
                            if (hasItems && task != null)
                            {
                                IsActive = true;
                                task.Action();
                                task.ResetEvent.Set();
                            }
                            else
                                Thread.Sleep(50); // Краткая пауза если задача не извлечена
                        }
                        catch (Exception ex)
                        {
                            // Обработка ошибок с защитой от исключений в обработчике ошибок
                            try
                            {
                                _errorHandler?.Invoke(ex.InnerException ?? ex);
                            }
                            catch
                            {
                                /* Ignore handler errors */
                            }
                        }
                        finally
                        {
                            IsActive = false; // Сбрасываем фак активности после завершения задачи
                        }

                        // Проверяем отмену после обработки каждого элемента
                        if (ct.IsCancellationRequested) break;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Ожидаемое исключение при отмене операции - игнорируем
                }
            }

            /// <summary>
            /// Инициирует остановку рабочего потока
            /// </summary>
            public void Cancel()
            {
                _workerCts.Cancel();
            }

            /// <summary>
            /// Освобождает ресурсы рабочего потока
            /// </summary>
            public void Dispose()
            {
                _workerCts.Dispose();
            }
        }


        private readonly PriorityQueue<PriorityTask, T> _queue;
        private readonly List<Worker> _workers = [];
        private readonly object _lock = new();
        private readonly Action<Exception>? _errorHandler;
        private bool _disposed = false;

        /// <summary>
        /// Общее количество рабочих потоков
        /// </summary>
        public int WorkerCount => _workers.Count;

        /// <summary>
        /// Количество активных рабочих потоков (выполняющих задачи в данный момент)
        /// </summary>
        public int ActiveWorkerCount => _workers.Count(w => w.IsActive);

        /// <summary>
        /// Инициализирует процессор с указанным количеством потоков.
        /// </summary>
        /// <param name="initialWorkers">Начальное количество рабочих потоков.</param>
        /// <param name="comparer">Компаратор для определения порядка приоритетов</param>
        /// <param name="errorHandler">Обработчик ошибок для действий (опционально).</param>
        /// <exception cref="ArgumentOutOfRangeException">Если initialWorkers отрицательное.</exception>
        public PriorityActionProcessor(int initialWorkers, IComparer<T>? comparer,
            Action<Exception>? errorHandler = null)
        {
            if (comparer != null)
                _queue = new(comparer);
            else
                _queue = new();
            _errorHandler = errorHandler;
            SetWorkerCount(initialWorkers);
        }

        /// <summary>
        /// Добавляет действие в очередь на выполнение с указанным приоритетом.
        /// </summary>
        /// <param name="item">Действие для выполнения.</param>
        /// <param name="priority">Приоритет выполнения (меньше значение = выше приоритет).</param>
        /// <exception cref="ObjectDisposedException">Если процессор уже уничтожен.</exception>
        public PriorityTask AddItem(Action item, T priority)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_queue)
            {
                var pt = new PriorityTask(item);
                _queue.Enqueue(pt, priority);
                return pt;
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
                    foreach (var t in tasksToWait) t.Join(); // Ожидаем завершения потоков
                    tasksToRemove.ForEach(t => t.Dispose()); // Освобождаем ресурсы

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

            // Ожидаем завершения всех рабочих потоков
            foreach (var worker in _workers) worker.WorkerTask.Join();

            // Освобождаем ресурсы
            foreach (var worker in _workers) worker.Dispose();

            _workers.Clear();
        }
    }
}