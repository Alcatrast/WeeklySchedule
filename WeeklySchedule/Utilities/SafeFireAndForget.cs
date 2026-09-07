using Microsoft.Extensions.Logging;
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
    /// Логгер приложения; ставится один раз при сборке контейнера. Пока его нет
    /// (тесты, ранний старт), остается Debug.WriteLine — а он в Release вырезается
    /// компилятором, и упавшая операция не оставляла вообще никакого следа.
    /// </summary>
    public static ILogger? Logger { get; set; }

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
            if (Logger != null) Logger.LogError(ex, "[{Caller}] необработанное исключение", caller);
            else Debug.WriteLine($"[{caller}] необработанное исключение: {ex}");
        }
    }
}
