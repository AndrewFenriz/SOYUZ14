using Content.Server.Revolutionary.Components;
using Content.Shared.Cuffs.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Objectives.Components;
using Content.Shared.Revolutionary.Components;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.Revolutionary;

public sealed class RevolutionOverthrowConditionSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private TimeSpan _nextUpdate = TimeSpan.Zero;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(5);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RevolutionOverthrowConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;

        var progress = CalculateOverthrowProgress();

        var query = EntityQueryEnumerator<RevolutionOverthrowConditionComponent>();
        while (query.MoveNext(out var entity, out var comp))
        {
            comp.Progress = progress;
        }
    }

    private void OnGetProgress(EntityUid uid, RevolutionOverthrowConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        args.Progress = comp.Progress;
    }

    public float CalculateOverthrowProgress()
    {
        float total = 0f;
        float commandCount = 0f;

        var query = AllEntityQuery<CommandStaffComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out _, out var mob))
        {
            commandCount++;

            if (HasComp<RevolutionaryComponent>(uid))
            {
                total += 1f;
                continue;
            }

            if (TryComp<CuffableComponent>(uid, out var cuffed) && cuffed.CuffedHandCount > 0)
            {
                total += 1f;
                continue;
            }

            if (mob.CurrentState == MobState.Dead || mob.CurrentState == MobState.Invalid)
            {
                total += 1f;
            }
        }

        if (commandCount == 0)
            return 1f;

        return total / commandCount;
    }
}
