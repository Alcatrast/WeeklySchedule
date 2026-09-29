using CommunityToolkit.Mvvm.Messaging.Messages;

namespace WeeklySchedule.Messaging;
public class DataChangedMessage : ValueChangedMessage<DayOfWeek?>
{
    public DataChangedMessage(DayOfWeek? value) : base(value) { }
}