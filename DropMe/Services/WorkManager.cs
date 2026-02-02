using System;
using System.Threading.Tasks;
namespace DropMe.Services;

public sealed class WorkManager : IWorkManager{
    // Sync
    public void ScheduleWork(Action callback)
    {
        if (callback is null) throw new ArgumentNullException(nameof(callback));
        // Exceptions thrown inside callback won't crash UI thread
        // Add task to thread pool
        _ = Task.Run(() =>
        {
            try
            {
                callback();
            }
            catch (Exception ex)
            {
                // Route to logging service (IMPLEMENT LATER)
                System.Diagnostics.Debug.WriteLine($"WorkManager sync error: {ex}");
            }
        });
    }
    // Async
    public void ScheduleWork(Func<Task> callback)
    {
        if (callback is null) throw new ArgumentNullException(nameof(callback));
        _ = Task.Run(async () =>
        {
            try
            {
                await callback().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Route to logging service (IMPLEMENT LATER)
                System.Diagnostics.Debug.WriteLine($"WorkManager async error: {ex}");
            }
        });
    }
}