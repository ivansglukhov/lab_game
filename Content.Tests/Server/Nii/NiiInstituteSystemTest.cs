using System;
using Content.Server.Nii.Components;
using Content.Server.Nii.Systems;
using Content.Shared.Nii;
using NUnit.Framework;

namespace Content.Tests.Server.Nii;

[TestFixture]
[TestOf(typeof(NiiInstituteSystem))]
[Parallelizable(ParallelScope.All)]
public sealed class NiiInstituteSystemTest
{
    [Test]
    public void AdvanceDaysAppliesFundingAndExpenses()
    {
        var institute = new NiiInstituteComponent();

        NiiInstituteSystem.AdvanceDays(institute, 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(institute.CurrentDay, Is.EqualTo(3));
            Assert.That(institute.Balance, Is.EqualTo(450_000));
            Assert.That(institute.IsBankrupt, Is.False);
        }
    }

    [Test]
    public void AdvanceDaysMarksSoftBankruptcyWithoutStoppingSimulation()
    {
        var institute = new NiiInstituteComponent();

        NiiInstituteSystem.AdvanceDays(institute, 21);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(institute.CurrentDay, Is.EqualTo(22));
            Assert.That(institute.Balance, Is.EqualTo(-25_000));
            Assert.That(institute.IsBankrupt, Is.True);
        }
    }

    [Test]
    public void AdvanceDaysRejectsNegativeTime()
    {
        var institute = new NiiInstituteComponent();

        Assert.Throws<ArgumentOutOfRangeException>(() => NiiInstituteSystem.AdvanceDays(institute, -1));
    }

    [Test]
    public void EventLogKeepsOnlyTheSixNewestEntries()
    {
        var institute = new NiiInstituteComponent();

        for (var i = 0; i < 8; i++)
            NiiInstituteSystem.AddEvent(institute, NiiInstituteEventType.ResearchStarted);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(institute.EventLog, Has.Count.EqualTo(6));
            Assert.That(institute.EventLog, Has.All.EqualTo(NiiInstituteEventType.ResearchStarted));
        }
    }
}
