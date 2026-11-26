using System.Globalization;
using System.Text.Json;

namespace APS.WebApp.Services.RobotRun;

/// <summary>
/// Agenda do Robô Principal. Horários editáveis na configuração do robô.
/// Salvador/Bahia é UTC-03; horários não dependem do momento do startup.
/// </summary>
public static class RobotScheduleClock
{
    public const string BahiaId = "America/Bahia";
    public const string EveryTwoHours = "06:00,08:00,10:00,12:00,14:00,16:00,18:00,20:00,22:00";
    public static readonly TimeSpan BahiaOffset = TimeSpan.FromHours(-3);

    // Horários são definidos pelo usuário em Configuração do Robô. A ordem é
    // canônica e o fuso sempre Bahia; não há agenda oculta fora do JSON salvo.
    private static int[]? ParseMinutes(RobotSchedule? schedule)
    {
        if (schedule is null || schedule.TimeZone != BahiaId || string.IsNullOrWhiteSpace(schedule.Time))
            return null;
        var raw = schedule.Time.Split(',', StringSplitOptions.TrimEntries);
        if (raw.Length is < 1 or > 48) return null;
        var minutes = new List<int>(raw.Length);
        foreach (var time in raw)
        {
            if (time.Length != 5 || time[2] != ':' || !int.TryParse(time.AsSpan(0, 2), out var hour)
                || !int.TryParse(time.AsSpan(3, 2), out var minute) || hour < 0 || hour > 23 || minute < 0 || minute > 59)
                return null;
            var value = hour * 60 + minute;
            if (minutes.Count > 0 && value <= minutes[^1]) return null;
            minutes.Add(value);
        }
        return minutes.ToArray();
    }

    public static bool IsValid(RobotSchedule schedule) => ParseMinutes(schedule) is not null;

    /// <summary>Janela de 2 minutos por horário, sem recuperação de horários antigos.
    /// Um mesmo slot não executa duas vezes por causa do claim durável do Worker.</summary>
    public static string? DueSlot(RobotSchedule? schedule, DateTimeOffset nowUtc)
    {
        var minutes = ParseMinutes(schedule);
        if (minutes is null) return null;
        var local = nowUtc.ToUniversalTime().ToOffset(BahiaOffset);
        foreach (var minuteOfDay in minutes)
        {
            var date = local.Date;
            var slot = new DateTimeOffset(date.Year, date.Month, date.Day,
                minuteOfDay / 60, minuteOfDay % 60, 0, BahiaOffset);
            if (local >= slot && local < slot.AddMinutes(2))
                return slot.ToString("yyyy-MM-ddTHH:mmzzz", CultureInfo.InvariantCulture);
        }
        return null;
    }

    public static DateTimeOffset? NextSlot(RobotSchedule? schedule, DateTimeOffset nowUtc)
    {
        var minutes = ParseMinutes(schedule);
        if (minutes is null) return null;
        var local = nowUtc.ToUniversalTime().ToOffset(BahiaOffset);
        for (var day = 0; day <= 1; day++)
        {
            foreach (var minuteOfDay in minutes)
            {
                var date = local.Date.AddDays(day);
                var slot = new DateTimeOffset(date.Year, date.Month, date.Day,
                    minuteOfDay / 60, minuteOfDay % 60, 0, BahiaOffset);
                if (slot > local) return slot;
            }
        }
        return null;
    }
}

/// <summary>
/// Claim durável ANTES de iniciar a fila: um slot nunca é iniciado duas vezes após reinício.
/// Trava exclusiva entre processos. Corrupção ou falha de escrita bloqueia a execução,
/// sem apagar estado, sem criar execução de recuperação.
/// </summary>
public sealed class RobotScheduleStateStore(string path)
{
    private readonly string _path = Path.GetFullPath(path);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private sealed record State(string Slot, DateTimeOffset ClaimedAtUtc);

    public async Task<bool> TryClaimAsync(string slot, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var dir = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(dir);
            await using var fileLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            if (File.Exists(_path))
            {
                var previous = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(_path, ct))
                    ?? throw new InvalidDataException("Estado de agendamento inválido.");
                if (string.IsNullOrWhiteSpace(previous.Slot))
                    throw new InvalidDataException("Slot de agendamento persistido inválido.");
                if (previous.Slot == slot) return false;
            }
            var tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(new State(slot, DateTimeOffset.UtcNow)), ct);
                ct.ThrowIfCancellationRequested();
                if (File.Exists(_path)) File.Replace(tmp, _path, _path + ".bak");
                else File.Move(tmp, _path);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
            return true;
        }
        finally { _gate.Release(); }
    }
}
