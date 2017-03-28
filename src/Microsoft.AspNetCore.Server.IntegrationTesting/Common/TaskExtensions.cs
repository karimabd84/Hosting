namespace System.Threading.Tasks
{
    internal static class TaskExtensions
    {
        public static async Task OrTimeout(this Task task, TimeSpan timeout)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeout));
            if(completed == task)
            {
                // Manifest any exception
                task.GetAwaiter().GetResult();
            }
            else
            {
                throw new TimeoutException();
            }
        }

        public static async Task<T> OrTimeout<T>(this Task<T> task, TimeSpan timeout)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeout));
            if(completed == task)
            {
                return task.GetAwaiter().GetResult();
            }
            else
            {
                throw new TimeoutException();
            }
        }
    }
}
