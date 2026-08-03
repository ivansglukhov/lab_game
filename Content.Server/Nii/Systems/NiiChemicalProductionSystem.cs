using System.Linq;
using Content.Server.Nii.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Nii;
using Content.Shared.Nii.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Nii.Systems;

/// <summary>
/// Creates production orders and runs timed, data-driven chemical stages after technicians deliver physical stocks.
/// </summary>
public sealed partial class NiiChemicalProductionSystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private NiiInstituteNarrativeSystem _narrative = default!;
    [Dependency] private NiiResearchWorkOrderSystem _researchOrders = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NiiChemicalReactorComponent>();
        while (query.MoveNext(out var reactorUid, out var reactor))
        {
            if (!reactor.IsProcessing ||
                reactor.ActiveOrder is not { } orderUid ||
                !TryComp<NiiProductionOrderComponent>(orderUid, out var order))
                continue;

            var process = _prototypes.Index(order.Process);
            if (order.StageIndex >= process.Stages.Count)
                continue;

            reactor.ElapsedSeconds += frameTime;
            var stage = process.Stages[order.StageIndex];
            if (reactor.ElapsedSeconds < stage.DurationSeconds)
                continue;

            CompleteStage((reactorUid, reactor), (orderUid, order), process, stage);
        }
    }

    public EntityUid? Create(
        Entity<NiiInstituteComponent> institute,
        Entity<NiiResearchWorkOrderComponent> researchOrder)
    {
        if (researchOrder.Comp.Laboratory is not { } laboratoryUid ||
            !TryComp<NiiLaboratoryComponent>(laboratoryUid, out var laboratory) ||
            laboratory.ChemicalReactor is not { } reactorUid ||
            !TryComp<NiiChemicalReactorComponent>(reactorUid, out var reactor) ||
            laboratory.ActiveProductionOrder is { } active && !Deleted(active))
            return null;

        var technicianUid = laboratory.Technicians
            .Where(uid => !Deleted(uid))
            .OrderBy(uid => uid)
            .FirstOrDefault(uid =>
                TryComp<NiiEmployeeComponent>(uid, out var employee) && employee.ActiveWorkOrder is null);
        if (!technicianUid.Valid || !TryComp<NiiEmployeeComponent>(technicianUid, out var technician))
            return null;

        var project = _prototypes.Index(researchOrder.Comp.Project);
        var orderUid = Spawn("NiiChemicalProductionOrder", Transform(reactorUid).Coordinates);
        var order = Comp<NiiProductionOrderComponent>(orderUid);
        order.Process = project.ProductionProcess;
        order.Status = NiiProductionOrderStatus.FetchingInputs;
        order.Institute = institute.Owner;
        order.Laboratory = laboratoryUid;
        order.ResearchOrder = researchOrder.Owner;
        order.AssignedTo = technicianUid;
        order.Reactor = reactorUid;

        technician.ActiveWorkOrder = orderUid;
        reactor.ActiveOrder = orderUid;
        laboratory.ActiveProductionOrder = orderUid;
        researchOrder.Comp.ProductionOrder = orderUid;
        researchOrder.Comp.Status = NiiWorkOrderStatus.AwaitingProduction;

        _narrative.Record(
            institute,
            NiiInstituteEventType.ProductionOrderCreated,
            data: new NiiInstituteEventData(technicianUid, orderUid, laboratory.LaboratoryId));
        _narrative.Record(
            institute,
            NiiInstituteEventType.TechnicianAssigned,
            data: new NiiInstituteEventData(technicianUid, orderUid, laboratory.LaboratoryId));

        TryReserveNextInput((orderUid, order));
        return orderUid;
    }

    public bool TryReserveNextInput(Entity<NiiProductionOrderComponent> order)
    {
        if (order.Comp.Reactor is not { } reactorUid ||
            !TryComp<NiiChemicalReactorComponent>(reactorUid, out var reactor) ||
            !_solutions.TryGetSolution(reactorUid, reactor.SolutionName, out _, out var reactorSolution))
            return false;

        var process = _prototypes.Index(order.Comp.Process);
        if (order.Comp.StageIndex >= process.Stages.Count)
            return false;

        var stage = process.Stages[order.Comp.StageIndex];
        foreach (var (reagent, required) in stage.Inputs)
        {
            var missing = required - reactorSolution.GetTotalPrototypeQuantity(reagent);
            if (missing <= 0)
                continue;

            var stocks = EntityQueryEnumerator<NiiChemicalStockComponent>();
            while (stocks.MoveNext(out var stockUid, out var stock))
            {
                if (stock.Reagent != reagent || stock.ReservedBy is not null ||
                    !_solutions.TryGetSolution(stockUid, stock.SolutionName, out _, out var stockSolution) ||
                    stockSolution.GetTotalPrototypeQuantity(reagent) < missing)
                    continue;

                stock.ReservedBy = order.Owner;
                order.Comp.CurrentSource = stockUid;
                order.Comp.CurrentInput = reagent;
                order.Comp.CurrentInputAmount = missing;
                order.Comp.Status = NiiProductionOrderStatus.FetchingInputs;
                Record(order, NiiInstituteEventType.ProductionInputReserved, amount: missing.Int());
                return true;
            }

            BlockProduction(order);
            return false;
        }

        foreach (var (gas, required) in stage.GasInputs)
        {
            var missing = required - reactor.GasBuffer.GetMoles(gas);
            if (missing <= 0f)
                continue;

            var stocks = EntityQueryEnumerator<NiiGasStockComponent, GasCanisterComponent>();
            while (stocks.MoveNext(out var stockUid, out var stock, out var canister))
            {
                if (stock.Gas != gas || stock.ReservedBy is not null || canister.Air.GetMoles(gas) < missing)
                    continue;

                stock.ReservedBy = order.Owner;
                order.Comp.CurrentSource = stockUid;
                order.Comp.CurrentGas = gas;
                order.Comp.CurrentGasAmount = missing;
                order.Comp.Status = NiiProductionOrderStatus.FetchingInputs;
                Record(order, NiiInstituteEventType.ProductionInputReserved, amount: (int) MathF.Ceiling(missing));
                return true;
            }

            BlockProduction(order);
            return false;
        }

        return TryStartStage(order, (reactorUid, reactor), process, stage);
    }

    public EntityUid? TryPrepareGasPayload(
        Entity<NiiProductionOrderComponent> order,
        EntityUid sourceUid)
    {
        if (order.Comp.CurrentSource != sourceUid ||
            order.Comp.CurrentPayload is not null ||
            order.Comp.CurrentGas is not { } gas ||
            !TryComp<NiiGasStockComponent>(sourceUid, out var stock) ||
            stock.ReservedBy != order.Owner ||
            stock.Gas != gas ||
            !TryComp<GasCanisterComponent>(sourceUid, out var canister))
            return null;

        var amount = order.Comp.CurrentGasAmount;
        if (amount <= 0f || canister.Air.GetMoles(gas) < amount)
            return null;

        var payloadUid = Spawn(stock.PayloadEntity, Transform(sourceUid).Coordinates);
        if (!TryComp<GasTankComponent>(payloadUid, out var tank))
        {
            QueueDel(payloadUid);
            return null;
        }

        canister.Air.AdjustMoles(gas, -amount);
        tank.Air.AdjustMoles(gas, amount);
        Dirty(sourceUid, canister);
        Dirty(payloadUid, tank);
        order.Comp.CurrentPayload = payloadUid;
        order.Comp.Status = NiiProductionOrderStatus.DeliveringInput;
        return payloadUid;
    }

    public bool TryLoadCurrentGas(
        Entity<NiiProductionOrderComponent> order,
        EntityUid payloadUid,
        EntityUid technicianUid)
    {
        if (order.Comp.CurrentPayload != payloadUid ||
            order.Comp.CurrentSource is not { } sourceUid ||
            order.Comp.CurrentGas is not { } gas ||
            order.Comp.Reactor is not { } reactorUid ||
            !TryComp<NiiChemicalReactorComponent>(reactorUid, out var reactor) ||
            !TryComp<NiiGasStockComponent>(sourceUid, out var stock) ||
            !TryComp<GasTankComponent>(payloadUid, out var tank))
            return false;

        var amount = order.Comp.CurrentGasAmount;
        if (amount <= 0f || tank.Air.GetMoles(gas) < amount)
            return false;

        tank.Air.AdjustMoles(gas, -amount);
        reactor.GasBuffer.AdjustMoles(gas, amount);
        Dirty(payloadUid, tank);
        stock.ReservedBy = null;
        order.Comp.CurrentSource = null;
        order.Comp.CurrentPayload = null;
        order.Comp.CurrentGas = null;
        order.Comp.CurrentGasAmount = 0f;
        order.Comp.Status = NiiProductionOrderStatus.FetchingInputs;
        Record(order, NiiInstituteEventType.ProductionInputDelivered, actor: technicianUid, amount: (int) MathF.Ceiling(amount));
        QueueDel(payloadUid);
        TryReserveNextInput(order);
        return true;
    }

    public bool TryLoadCurrentInput(
        Entity<NiiProductionOrderComponent> order,
        EntityUid sourceUid,
        EntityUid technicianUid)
    {
        if (order.Comp.CurrentSource != sourceUid ||
            order.Comp.CurrentInput is not { } reagent ||
            order.Comp.Reactor is not { } reactorUid ||
            !TryComp<NiiChemicalReactorComponent>(reactorUid, out var reactor) ||
            !TryComp<NiiChemicalStockComponent>(sourceUid, out var stock) ||
            !_solutions.TryGetSolution(sourceUid, stock.SolutionName, out var sourceSolutionEntity, out var sourceSolution) ||
            !_solutions.TryGetSolution(reactorUid, reactor.SolutionName, out var reactorSolutionEntity, out var reactorSolution))
            return false;

        var amount = order.Comp.CurrentInputAmount;
        if (sourceSolution.GetTotalPrototypeQuantity(reagent) < amount || reactorSolution.AvailableVolume < amount)
            return false;

        var removed = _solutions.RemoveReagent(sourceSolutionEntity.Value, reagent, amount);
        if (removed != amount ||
            !_solutions.TryAddReagent(reactorSolutionEntity.Value, reagent, removed, out var accepted) ||
            accepted != removed)
        {
            if (removed > 0)
                _solutions.TryAddReagent(sourceSolutionEntity.Value, reagent, removed, out _);
            return false;
        }
        stock.ReservedBy = null;
        order.Comp.CurrentSource = null;
        order.Comp.CurrentInput = null;
        order.Comp.CurrentInputAmount = 0;
        order.Comp.Status = NiiProductionOrderStatus.FetchingInputs;
        Record(order, NiiInstituteEventType.ProductionInputDelivered, actor: technicianUid, amount: amount.Int());
        TryReserveNextInput(order);
        return true;
    }

    private bool TryStartStage(
        Entity<NiiProductionOrderComponent> order,
        Entity<NiiChemicalReactorComponent> reactor,
        NiiChemicalProcessPrototype process,
        NiiChemicalProcessStage stage)
    {
        if (!_solutions.TryGetSolution(reactor.Owner, reactor.Comp.SolutionName, out var solutionEntity, out var solution))
            return false;

        foreach (var (reagent, amount) in stage.Inputs)
        {
            if (solution.GetTotalPrototypeQuantity(reagent) < amount)
                return false;
        }

        foreach (var (gas, amount) in stage.GasInputs)
        {
            if (reactor.Comp.GasBuffer.GetMoles(gas) < amount)
                return false;
        }

        foreach (var (reagent, amount) in stage.Inputs)
        {
            _solutions.RemoveReagent(solutionEntity.Value, reagent, amount);
        }

        foreach (var (gas, amount) in stage.GasInputs)
        {
            reactor.Comp.GasBuffer.AdjustMoles(gas, -amount);
        }

        reactor.Comp.IsProcessing = true;
        reactor.Comp.ElapsedSeconds = 0f;
        order.Comp.Status = NiiProductionOrderStatus.Processing;
        Record(order, NiiInstituteEventType.ProductionStageStarted, amount: order.Comp.StageIndex + 1);
        return true;
    }

    private void CompleteStage(
        Entity<NiiChemicalReactorComponent> reactor,
        Entity<NiiProductionOrderComponent> order,
        NiiChemicalProcessPrototype process,
        NiiChemicalProcessStage stage)
    {
        if (!_solutions.TryGetSolution(reactor.Owner, reactor.Comp.SolutionName, out var solutionEntity, out _))
            return;

        _solutions.TryAddReagent(solutionEntity.Value, stage.Output, stage.OutputAmount, out _);
        reactor.Comp.IsProcessing = false;
        reactor.Comp.ElapsedSeconds = 0f;
        Record(order, NiiInstituteEventType.ProductionStageCompleted, amount: order.Comp.StageIndex + 1);
        order.Comp.StageIndex++;

        if (order.Comp.StageIndex < process.Stages.Count)
        {
            order.Comp.Status = NiiProductionOrderStatus.FetchingInputs;
            TryReserveNextInput(order);
            return;
        }

        _solutions.RemoveReagent(solutionEntity.Value, stage.Output, stage.OutputAmount);
        var sampleUid = Spawn(process.ProductEntity, Transform(reactor.Owner).Coordinates);
        CompleteOrder(reactor, order, sampleUid, stage.OutputAmount.Int());
    }

    private void CompleteOrder(
        Entity<NiiChemicalReactorComponent> reactor,
        Entity<NiiProductionOrderComponent> order,
        EntityUid sampleUid,
        int amount)
    {
        order.Comp.Status = NiiProductionOrderStatus.Completed;
        ReleaseOrderResources(order, reactor);

        if (order.Comp.ResearchOrder is { } researchOrderUid &&
            TryComp<NiiResearchWorkOrderComponent>(researchOrderUid, out var researchOrder))
            _researchOrders.OnProductionCompleted((researchOrderUid, researchOrder), sampleUid);

        Record(order, NiiInstituteEventType.ReagentProduced, NiiInstituteEventSeverity.Success, amount: amount);
    }

    private void ReleaseOrderResources(
        Entity<NiiProductionOrderComponent> order,
        NiiChemicalReactorComponent? knownReactor = null)
    {
        if (order.Comp.Reactor is { } reactorUid &&
            (knownReactor ?? CompOrNull<NiiChemicalReactorComponent>(reactorUid)) is { } reactor &&
            reactor.ActiveOrder == order.Owner)
        {
            reactor.ActiveOrder = null;
            reactor.IsProcessing = false;
            reactor.ElapsedSeconds = 0f;
        }

        if (order.Comp.CurrentSource is { } sourceUid &&
            TryComp<NiiChemicalStockComponent>(sourceUid, out var stock) &&
            stock.ReservedBy == order.Owner)
            stock.ReservedBy = null;

        if (order.Comp.CurrentSource is { } gasSourceUid &&
            TryComp<NiiGasStockComponent>(gasSourceUid, out var gasStock) &&
            gasStock.ReservedBy == order.Owner)
            gasStock.ReservedBy = null;

        if (order.Comp.AssignedTo is { } technicianUid &&
            TryComp<NiiEmployeeComponent>(technicianUid, out var technician) &&
            technician.ActiveWorkOrder == order.Owner)
            technician.ActiveWorkOrder = null;

        if (order.Comp.Laboratory is { } laboratoryUid &&
            TryComp<NiiLaboratoryComponent>(laboratoryUid, out var laboratory) &&
            laboratory.ActiveProductionOrder == order.Owner)
            laboratory.ActiveProductionOrder = null;
    }

    private void BlockProduction(Entity<NiiProductionOrderComponent> order)
    {
        order.Comp.Status = NiiProductionOrderStatus.Blocked;
        Record(order, NiiInstituteEventType.ProductionBlocked, NiiInstituteEventSeverity.Attention);
        ReleaseOrderResources(order);
        if (order.Comp.ResearchOrder is { } researchOrderUid &&
            TryComp<NiiResearchWorkOrderComponent>(researchOrderUid, out var researchOrder))
            _researchOrders.Block((researchOrderUid, researchOrder), NiiWorkOrderBlockReason.ProductionUnavailable);
    }

    private void Record(
        Entity<NiiProductionOrderComponent> order,
        NiiInstituteEventType type,
        NiiInstituteEventSeverity severity = NiiInstituteEventSeverity.Info,
        EntityUid? actor = null,
        int amount = 0)
    {
        if (order.Comp.Institute is not { } instituteUid ||
            !TryComp<NiiInstituteComponent>(instituteUid, out var institute))
            return;

        var laboratoryId = order.Comp.Laboratory is { } laboratoryUid &&
                           TryComp<NiiLaboratoryComponent>(laboratoryUid, out var laboratory)
            ? laboratory.LaboratoryId
            : string.Empty;
        _narrative.Record(
            (instituteUid, institute),
            type,
            severity,
            new NiiInstituteEventData(actor ?? order.Comp.AssignedTo, order.Owner, laboratoryId, Amount: amount));
    }
}
