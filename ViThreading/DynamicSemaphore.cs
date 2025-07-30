using System.Diagnostics;

namespace ViThreading
{
    /// <summary>
    /// Реализует семафор с динамически изменяемым максимальным количеством слотов.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Особенности поведения:
    /// - Поддерживает динамическое изменение <see cref="MaximumCount"/> во время работы
    /// - При уменьшении лимита ниже текущего <see cref="UsedCount"/> новые операции блокируются,
    ///   но существующие продолжают работу до завершения
    /// - Автоматическая нормализация при вызовах <see cref="Release"/>
    /// </para>
    /// <para>
    /// Потокобезопасность: все операции класса защищены внутренней блокировкой.
    /// </para>
    /// </remarks>
    public class DynamicSemaphore
    {
        private readonly object _lock = new();
        private int _usedCount;
        private int _maximumCount;

        /// <summary>
        /// Инициализирует новый экземпляр семафора с указанным максимальным количеством слотов.
        /// </summary>
        /// <param name="maximumCount">Начальное максимальное количество слотов. Должно быть неотрицательным.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если <paramref name="maximumCount"/> отрицательное.</exception>
        public DynamicSemaphore(int maximumCount)
        {
            MaximumCount = maximumCount;
        }

        /// <summary>
        /// Устанавливает максимальное количество доступных слотов.
        /// </summary>
        /// <remarks>
        /// Допускает значение 0 для приостановки выдачи новых слотов.
        /// При уменьшении значения ниже текущего <see cref="UsedCount"/>:
        /// - Новые вызовы <see cref="WaitOne"/> блокируются
        /// - Существующие операции продолжают работу
        /// - <see cref="AvailableCount"/> возвращает 0
        /// - Семафор автоматически нормализуется при вызовах <see cref="Release"/>
        /// </remarks>
        public int MaximumCount
        {
            get { lock (_lock) return _maximumCount; }
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                lock (_lock)
                {
                    if (_maximumCount == value) return;
                    var isUp = value > _maximumCount;
                    _maximumCount = value;
                    if (isUp)
                        Monitor.PulseAll(_lock);
                }
            }
        }

        /// <summary>
        /// Текущее количество занятых слотов.
        /// </summary>
        /// <remarks>
        /// Может временно превышать <see cref="MaximumCount"/> 
        /// при уменьшении лимита во время активных операций.
        /// </remarks>
        public int UsedCount
        {
            get { lock (_lock) return _usedCount; }
        }

        /// <summary>
        /// Текущее количество доступных слотов.
        /// </summary>
        /// <remarks>
        /// Возвращает 0, если количество доступных слотов отрицательно
        /// (когда <see cref="MaximumCount"/> меньше <see cref="UsedCount"/>).
        /// </remarks>
        public int AvailableCount
        {
            get { lock (_lock) return Math.Max(_maximumCount - _usedCount, 0); }
        }

        /// <summary>
        /// Блокирует текущий поток до получения слота (с опциональным таймаутом).
        /// </summary>
        /// <param name="timeoutMs">
        /// Время ожидания в миллисекундах. 
        /// 0 - немедленный возврат. 
        /// Timeout.Infinite (-1) - бесконечное ожидание.
        /// </param>
        /// <returns>
        /// true - если слот успешно получен; 
        /// false - если истекло время ожидания
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">Если <paramref name="timeoutMs"/> отрицательное (кроме -1).</exception>
        public bool TryWaitOne(int timeoutMs = 0)
        {
            if (timeoutMs < -1)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), "timeoutMs must be non-negative or -1.");

            if (timeoutMs == Timeout.Infinite)
            {
                WaitOne();
                return true;
            }

            lock (_lock)
            {
                if (timeoutMs == 0)
                    return TryAcquireImmediately();

                var sw = Stopwatch.StartNew();
                while (_usedCount >= _maximumCount)
                {
                    int elapsedMs = (int)sw.ElapsedMilliseconds;
                    if (elapsedMs >= timeoutMs)
                        return false;

                    Monitor.Wait(_lock, timeoutMs - elapsedMs);
                }
                _usedCount++;
                return true;
            }
        }

        private bool TryAcquireImmediately()
        {
            if (_usedCount >= _maximumCount)
                return false;
            _usedCount++;
            return true;
        }

        /// <summary>
        /// Блокирует текущий поток до получения семафора.
        /// </summary>
        /// <remarks>
        /// Если слот недоступен, поток блокируется до его появления.
        /// </remarks>
        public void WaitOne()
        {
            lock (_lock)
            {
                while (_usedCount >= _maximumCount)
                {
                    Monitor.Wait(_lock);
                }
                _usedCount++;
            }
        }

        /// <summary>
        /// Освобождает занятый слот.
        /// </summary>
        /// <exception cref="SemaphoreFullException">
        /// Выбрасывается, если количество использованных слотов становится отрицательным.
        /// </exception>
        public void Release()
        {
            lock (_lock)
            {
                _usedCount--;
                if (_usedCount < 0) throw new SemaphoreFullException();

                Monitor.Pulse(_lock);

            }
        }
    }
}