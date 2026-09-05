using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace WeeklySchedule.Utilities;

/// <summary>
/// Обработчики событий и переопределения вроде OnAppearing объявлены как void,
/// поэтому await внутри них превращает метод в async void. Исключение из такого
/// метода поймать негде: оно летит мимо вызывающего кода прямо в планировщик
/// задач и роняет процесс. Здесь единственное место, где его можно перехватить.
/// </summary>
public static class SafeFireAndForget
{
    /// <summary>
    /// Выполняет асинхронную операцию, не пропуская исключение наружу.
    /// Имя вызывающего метода подставляется само и попадает в лог.
    /// </summary>
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
