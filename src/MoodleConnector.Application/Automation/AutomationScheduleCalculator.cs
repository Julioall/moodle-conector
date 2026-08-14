namespace MoodleConnector.Application.Automation;

public static class AutomationScheduleCalculator
{
    public static DateTimeOffset? CalculateNext(
        string scheduleType,
        int runHourUtc,
        int runMinuteUtc,
        int? runDayOfWeek,
        DateTimeOffset now)
    {
        Validate(scheduleType, runHourUtc, runMinuteUtc, runDayOfWeek);
        if (string.Equals(scheduleType, AutomationCatalog.ManualSchedule, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var candidate = new DateTimeOffset(
            now.UtcDateTime.Date.AddHours(runHourUtc).AddMinutes(runMinuteUtc),
            TimeSpan.Zero);
        if (candidate <= now)
        {
            candidate = candidate.AddDays(1);
        }

        if (string.Equals(scheduleType, AutomationCatalog.WeeklySchedule, StringComparison.OrdinalIgnoreCase))
        {
            var targetDay = (DayOfWeek)runDayOfWeek!.Value;
            var days = ((int)targetDay - (int)candidate.DayOfWeek + 7) % 7;
            candidate = candidate.AddDays(days);
            if (candidate <= now)
            {
                candidate = candidate.AddDays(7);
            }
        }

        return candidate;
    }

    public static void Validate(
        string scheduleType,
        int runHourUtc,
        int runMinuteUtc,
        int? runDayOfWeek)
    {
        if (!AutomationCatalog.Schedules.Contains(scheduleType))
            throw new ArgumentException("Tipo de agendamento inválido.", nameof(scheduleType));
        if (runHourUtc is < 0 or > 23)
            throw new ArgumentException("A hora UTC deve estar entre 0 e 23.", nameof(runHourUtc));
        if (runMinuteUtc is < 0 or > 59)
            throw new ArgumentException("O minuto UTC deve estar entre 0 e 59.", nameof(runMinuteUtc));
        if (string.Equals(scheduleType, AutomationCatalog.WeeklySchedule, StringComparison.OrdinalIgnoreCase) &&
            runDayOfWeek is not >= 0 and <= 6)
            throw new ArgumentException("O dia semanal deve estar entre 0 (domingo) e 6 (sábado).", nameof(runDayOfWeek));
    }
}
