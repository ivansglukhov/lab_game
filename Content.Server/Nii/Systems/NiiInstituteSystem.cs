using Content.Server.Nii.Components;
using Content.Shared.Nii;

namespace Content.Server.Nii.Systems;

/// <summary>
/// Advances the small authoritative economy used by the first NII slice.
/// </summary>
public sealed partial class NiiInstituteSystem : EntitySystem
{
    [Dependency] private NiiDirectorTerminalSystem _terminals = default!;
    [Dependency] private NiiInstituteNarrativeSystem _narrative = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NiiInstituteComponent>();
        while (query.MoveNext(out var instituteUid, out var institute))
        {
            if (institute.DayDurationSeconds <= 0f)
                continue;

            institute.ElapsedSeconds += frameTime;
            var elapsedDays = (int) (institute.ElapsedSeconds / institute.DayDurationSeconds);
            if (elapsedDays <= 0)
                continue;

            institute.ElapsedSeconds -= elapsedDays * institute.DayDurationSeconds;
            var wasBankrupt = institute.IsBankrupt;
            var balanceBefore = institute.Balance;
            AdvanceDays(institute, elapsedDays);
            _narrative.Record(
                (instituteUid, institute),
                NiiInstituteEventType.DayAdvanced,
                NiiInstituteEventSeverity.Info,
                new NiiInstituteEventData(Amount: institute.Balance - balanceBefore));
            if (!wasBankrupt && institute.IsBankrupt)
            {
                _narrative.Record(
                    (instituteUid, institute),
                    NiiInstituteEventType.FinancialWarning,
                    NiiInstituteEventSeverity.Critical,
                    new NiiInstituteEventData(Amount: institute.Balance));
            }
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
