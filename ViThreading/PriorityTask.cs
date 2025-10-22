using System.Collections;

namespace ViThreading;

public class PriorityTask
{
    internal readonly ManualResetEvent ResetEvent = new(false);
    internal readonly Action Action;

    internal PriorityTask(Action action)
    {
        this.Action = action;
    }

    public void Wait() => ResetEvent.WaitOne();

    public static void WaitAll(params PriorityTask[] tasks)
    {
        foreach (var task in tasks)
            task.Wait();
    }
}