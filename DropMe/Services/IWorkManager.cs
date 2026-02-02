using System;
using System.Threading.Tasks;
namespace DropMe.Services;

public interface IWorkManager {
    // Schedule a sync work thread
    void ScheduleWork(Action callback);
    
    // Schedule an async work thread
    void ScheduleWork(Func<Task> callback);
}