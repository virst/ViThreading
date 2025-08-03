using ViThreading;
using System.Diagnostics;

var processor = new PriorityActionProcessor(
    initialWorkers: 3,
    errorHandler: ex => Console.WriteLine($"ОШИБКА: {ex.Message}")
);

var stopwatch = Stopwatch.StartNew();

// Генератор событий
var eventGenerator = new Thread(() =>
{
    int eventId = 1;
    var random = new Random();

    while (true)
    {
        // Случайный тип события (1=критическое, 2=предупреждение, 3=информационное)
        int eventType = random.Next(1, 4);
        int priority = 0;
        string message = "";

        switch (eventType)
        {
            case 1:
                priority = 1; // Высший приоритет
                message = $"🚨 ПОЖАР в секторе {random.Next(1, 10)}";
                break;
            case 2:
                priority = 50;
                message = $"⚠️ Сбой датчика #{random.Next(100, 200)}";
                break;
            case 3:
                priority = 100;
                message = $"ℹ️ Данные обновлены ({DateTime.Now:HH:mm:ss})";
                break;
        }

        processor.AddItem(() => ProcessEvent(eventId++, message), priority);
        Thread.Sleep(random.Next(200, 800)); // Имитация задержки между событиями
    }
});

eventGenerator.Start();

// Обработчик события
void ProcessEvent(int id, string message)
{
    Console.ForegroundColor = GetColorByPriority(message);
    Console.Write($"[{stopwatch.ElapsedMilliseconds}ms] ");
    Console.WriteLine($"СОБЫТИЕ #{id}: {message}");
    Console.ResetColor();

    // Имитация обработки (критические события обрабатываются дольше)
    int delay = message.Contains("🚨") ? 1000 : 200;
    Thread.Sleep(delay);
}

ConsoleColor GetColorByPriority(string message)
{
    if (message.Contains("🚨")) return ConsoleColor.Red;
    if (message.Contains("⚠️")) return ConsoleColor.Yellow;
    return ConsoleColor.Green;
}

// Автоматическое масштабирование обработчиков
var scaler = new Thread(() =>
{
    while (true)
    {
        // Увеличиваем потоки при высокой нагрузке
        if (stopwatch.ElapsedMilliseconds > 5000 &&
            stopwatch.ElapsedMilliseconds < 10000)
        {
            processor.SetWorkerCount(8);
            Console.WriteLine("\n⚡ АКТИВИРОВАН ТУРБО-РЕЖИМ (8 потоков)\n");
        }
        Thread.Sleep(2000);
    }
});

scaler.Start();

Console.ReadLine();
processor.Dispose();