using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace WeeklySchedule.Utilities;

public static class SafeFireAndForget
{
    public static async void Run(Func<Task> operation, [CallerMemberName] string caller = "")
    {
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{caller}] необработанное исключение: {ex}");
        }
    }
}
