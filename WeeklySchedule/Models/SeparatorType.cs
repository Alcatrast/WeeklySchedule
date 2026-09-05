namespace WeeklySchedule.Models;

public enum SeparatorType
{
    None,           // Невидимый (высота 0)
    ThickWhite,     // Толстый белый (маркер текущего времени)
    ThinRedTop,     // Тонкий красный сверху (граница текущей пары)
    ThinRedBottom   // Тонкий красный снизу (граница текущей пары)
}
