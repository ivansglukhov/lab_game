using Content.Server.Nii.Components;

namespace Content.Server.Nii.Systems;

/// <summary>
/// Advances the small authoritative economy used by the first NII slice.
/// </summary>
public sealed partial class NiiInstituteSystem : EntitySystem
{
    [Dependency] private NiiDirectorTerminalSystem _terminals = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NiiInstituteComponent>();
        while (query.MoveNext(out _, out var institute))
        {
            if (institute.DayDurationSeconds <= 0f)
                continue;

            institute.ElapsedSeconds += frameTime;
            var elapsedDays = (int) (institute.ElapsedSeconds / institute.DayDurationSeconds);
            if (elapsedDays <= 0)
                continue;

            institute.ElapsedSeconds -= elapsedDays * institute.DayDurationSeconds;
            AdvanceDays(institute, elapsedDays);
            _terminals.RefreshAll(institute);
        }
    }

    /// <summary>
    /// Applies one or more complete institute days. Kept deterministic for saves and tests.
    /// </summary>
    public static void AdvanceDays(NiiInstituteComponent institute, int days)
    {
        if (days < 0)
            throw new ArgumentOutOfRangeException(nameof(days));

        institute.Balance += (institute.DailyFunding - institute.DailyExpenses) * days;
        institute.CurrentDay += days;
        institute.IsBankrupt = institute.Balance < 0;
    }
}
