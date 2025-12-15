using Content.Server.Administration.Logs;
using Content.Server.Antag;
using Content.Server.EUI;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Server.Revolutionary;
using Content.Server.Revolutionary.Components;
using Content.Server.Roles;
using Content.Server.RoundEnd;
using Content.Server.Station.Systems;
using Content.Shared.Database;
using Content.Shared.GameTicking.Components;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Mind.Components;
using Content.Shared.Mindshield.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Revolutionary.Components;
using Content.Shared.Roles.Components;
using Content.Shared.Stunnable;
using Content.Shared.Zombies;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.Cuffs.Components;
using Content.Shared.Revolutionary;
using Robust.Server.Player;
using Content.Server.Actions;
using Robust.Shared.Player;

namespace Content.Server.GameTicking.Rules;

/// <summary>
/// Where all the main stuff for Revolutionaries happens (Assigning Head Revs, Command on station, and checking for the game to end.)
/// </summary>
public sealed class RevolutionaryRuleSystem : GameRuleSystem<RevolutionaryRuleComponent>
{
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    [Dependency] private readonly EuiManager _euiMan = default!;
    [Dependency] private readonly IAdminLogManager _adminLogManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly RoleSystem _role = default!;
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly ActionsSystem _actions = default!;

    //Used in OnPostFlash, no reference to the rule component is available
    public readonly ProtoId<NpcFactionPrototype> RevolutionaryNpcFaction = "Revolutionary";
    public readonly ProtoId<NpcFactionPrototype> RevPrototypeId = "Rev";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CommandStaffComponent, MobStateChangedEvent>(OnCommandMobStateChanged);

        SubscribeLocalEvent<HeadRevolutionaryComponent, HeadRevConvertActionEvent>(OnTargetWithConvertWindow);

        SubscribeLocalEvent<HeadRevolutionaryComponent, MobStateChangedEvent>(OnHeadRevMobStateChanged);

        SubscribeLocalEvent<RevolutionaryRoleComponent, GetBriefingEvent>(OnGetBriefing);

        SubscribeLocalEvent<HeadRevolutionaryComponent, MapInitEvent>(OnPendingMapInit);
    }

    protected override void Started(EntityUid uid, RevolutionaryRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        component.Check = _timing.CurTime + component.TimerWait;
    }

    protected override void ActiveTick(EntityUid uid, RevolutionaryRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (component.Check > _timing.CurTime)
            return;

        component.Check = _timing.CurTime + component.TimerWait;

        var outcome = GetRevolutionaryOutcome();

        if (outcome == "rev-all-command-revs")
        {
            _roundEnd.DoRoundEndBehavior(RoundEndBehavior.ShuttleCall, component.ShuttleCallTime);
            GameTicker.EndGameRule(uid, gameRule);
        }
    }

    protected override void AppendRoundEndText(EntityUid uid,
        RevolutionaryRuleComponent component,
        GameRuleComponent gameRule,
        ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);

        var outcome = GetRevolutionaryOutcome();
        args.AddLine(Loc.GetString(outcome));

        var sessionData = _antag.GetAntagIdentifiers(uid);
        args.AddLine(Loc.GetString("rev-headrev-count", ("initialCount", sessionData.Count)));
        foreach (var (mind, data, name) in sessionData)
        {
            _role.MindHasRole<RevolutionaryRoleComponent>(mind, out var role);
            var count = CompOrNull<RevolutionaryRoleComponent>(role)?.ConvertedCount ?? 0;

            args.AddLine(Loc.GetString("rev-headrev-name-user",
                ("name", name),
                ("username", data.UserName),
                ("count", count)));

            // TODO: someone suggested listing all alive? revs maybe implement at some point
        }
    }

    private void OnGetBriefing(EntityUid uid, RevolutionaryRoleComponent comp, ref GetBriefingEvent args)
    {
        var ent = args.Mind.Comp.OwnedEntity;
        var head = HasComp<HeadRevolutionaryComponent>(ent);
        args.Append(Loc.GetString(head ? "head-rev-briefing" : "rev-briefing"));
    }

    private void OnPendingMapInit(EntityUid uid, HeadRevolutionaryComponent comp, MapInitEvent args)
    {
        _actions.AddAction(uid, comp.HeadRevConvertAction, comp.HeadRevConvertActionEntity);
    }

    /// <summary>
    /// Called when a Head Rev clicks on player using ability.
    /// </summary>
    private void OnTargetWithConvertWindow(EntityUid uid, HeadRevolutionaryComponent comp, ref HeadRevConvertActionEvent ev)
    {
        var alwaysConvertible = HasComp<AlwaysRevolutionaryConvertibleComponent>(ev.Target);
        var targetName = MetaData(ev.Target).EntityName;

        if (!_mind.TryGetMind(ev.Target, out var mindId, out var mind) && !alwaysConvertible)
            return;

        if (HasComp<RevolutionaryComponent>(ev.Target) ||
            HasComp<MindShieldComponent>(ev.Target) ||
            !HasComp<HumanoidAppearanceComponent>(ev.Target) &&
            !alwaysConvertible ||
            !_mobState.IsAlive(ev.Target) ||
            HasComp<ZombieComponent>(ev.Target))
        {
            _popup.PopupEntity(Loc.GetString("head-rev-cant-convert-attempt", ("target", targetName)), ev.Target, uid);
            return;
        }

        if (mind == null || _role.MindHasRole<RevolutionaryRoleComponent>(mindId))
        {
            _popup.PopupEntity(Loc.GetString("head-rev-cant-convert-attempt", ("target", targetName)), ev.Target, uid);
            return;
        }

        // Yes, we still need to track down the client because we need to open the Eui
        if (mind.UserId == null || !_playerManager.TryGetSessionById(mind.UserId.Value, out var client))
        {
            _popup.PopupEntity(Loc.GetString("head-rev-cant-convert-attempt", ("target", targetName)), ev.Target, uid);
            return; // If we can't track down the client, we can't offer transfer. That'd be quite bad.
        }

        _adminLogManager.Add(LogType.Mind,
            LogImpact.Medium,
            $"{ToPrettyString(ev.Performer)} sended invite to {ToPrettyString(ev.Target)} into a Revolutionary");

        _popup.PopupEntity(Loc.GetString("head-rev-on-convert-attempt", ("target", targetName)), ev.Target, uid);

        _euiMan.OpenEui(new BecomeRevEui(uid, ev.Target, this), client);
    }

    /// <summary>
    /// Called when a Head Rev accepts a voluntary convert request.
    /// </summary>
    public void Convert(EntityUid headRevUid, EntityUid targetUid)
    {
        var alwaysConvertible = HasComp<AlwaysRevolutionaryConvertibleComponent>(targetUid);

        if (!_mind.TryGetMind(targetUid, out var mindId, out var mind) && !alwaysConvertible)
            return;

        if (HasComp<RevolutionaryComponent>(targetUid) ||
            HasComp<MindShieldComponent>(targetUid) ||
            !HasComp<HumanoidAppearanceComponent>(targetUid) &&
            !alwaysConvertible ||
            !_mobState.IsAlive(targetUid) ||
            HasComp<ZombieComponent>(targetUid))
        {
            return;
        }

        _npcFaction.AddFaction(targetUid, RevolutionaryNpcFaction);
        var revComp = EnsureComp<RevolutionaryComponent>(targetUid);

        _adminLogManager.Add(LogType.Mind,
            LogImpact.Medium,
            $"{ToPrettyString(headRevUid)} converted {ToPrettyString(targetUid)} into a Revolutionary");

        if (_mind.TryGetMind(headRevUid, out var revMindId, out _))
        {
            if (_role.MindHasRole<RevolutionaryRoleComponent>(revMindId, out var role))
            {
                role.Value.Comp2.ConvertedCount++;
                Dirty(role.Value.Owner, role.Value.Comp2);
            }
        }

        if (mindId != default && !_role.MindHasRole<RevolutionaryRoleComponent>(mindId))
        {
            _role.MindAddRole(mindId, "MindRoleRevolutionary");
        }

        if (mind is { UserId: not null } && _player.TryGetSessionById(mind.UserId, out var session))
            _antag.SendBriefing(session, Loc.GetString("rev-role-greeting"), Color.Red, revComp.RevStartSound);
    }

    //TODO: Enemies of the revolution
    private void OnCommandMobStateChanged(EntityUid uid, CommandStaffComponent comp, MobStateChangedEvent ev)
    {
        if (ev.NewMobState == MobState.Dead || ev.NewMobState == MobState.Invalid)
            CheckCommandLose();
    }

    /// <summary>
    /// Checks if all of command is dead and if so will remove all sec and command jobs if there were any left.
    /// </summary>
    private bool CheckCommandLose()
    {
        return AreAllCommandStaffDead() || AreAllCommandStaffDetained() || AreAllCommandStaffConverted();
    }

    /// <summary>
    /// Get the fraction of players that join revolutionary, between 0 and 1
    /// </summary>
    private float GetRevsFraction()
    {
        var players = GetHealthyHumanoids();
        var revsCount = 0;
        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, RevolutionaryComponent>();
        while (query.MoveNext(out _, out _, out _))
        {
            revsCount++;
        }

        return revsCount / (float)players.Count;
    }

    /// <summary>
    /// Gets the list of humanoids who are alive and are on a station.
    /// Flying off via a shuttle disqualifies you.
    /// </summary>
    /// <returns></returns>
    private List<EntityUid> GetHealthyHumanoids()
    {
        var humanoids = new List<EntityUid>();
        var stationGrids = new HashSet<EntityUid>();

        foreach (var station in _stationSystem.GetStationsSet())
        {
            if (_stationSystem.GetLargestGrid(station) is { } grid)
                stationGrids.Add(grid);
        }

        var players = AllEntityQuery<HumanoidAppearanceComponent, ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out _, out var mob, out var xform))
        {
            if (!_mobState.IsAlive(uid, mob))
                continue;

            if (!stationGrids.Contains(xform.GridUid ?? EntityUid.Invalid))
                continue;

            humanoids.Add(uid);
        }
        return humanoids;
    }

    private void OnHeadRevMobStateChanged(EntityUid uid, HeadRevolutionaryComponent comp, MobStateChangedEvent ev)
    {
        if (ev.NewMobState == MobState.Dead || ev.NewMobState == MobState.Invalid)
            CheckRevsLose();
    }

    /// <summary>
    /// Checks if all the Head Revs are dead and if so will deconvert all regular revs.
    /// </summary>
    private bool CheckRevsLose()
    {
        var stunTime = TimeSpan.FromSeconds(4);

        // If no Head Revs are alive all normal Revs will lose their Rev status and rejoin Nanotrasen
        // Cuffing Head Revs is not enough - they must be killed.
        if (AreAllHeadRevsDead())
        {
            var rev = AllEntityQuery<RevolutionaryComponent, MindContainerComponent>();
            while (rev.MoveNext(out var uid, out _, out var mc))
            {
                if (HasComp<HeadRevolutionaryComponent>(uid))
                    continue;

                _npcFaction.RemoveFaction(uid, RevolutionaryNpcFaction);
                _stun.TryUpdateParalyzeDuration(uid, stunTime);
                RemCompDeferred<RevolutionaryComponent>(uid);
                _popup.PopupEntity(Loc.GetString("rev-break-control", ("name", Identity.Entity(uid, EntityManager))), uid);
                _adminLogManager.Add(LogType.Mind, LogImpact.Medium, $"{ToPrettyString(uid)} was deconverted due to all Head Revolutionaries dying.");

                if (!_mind.TryGetMind(uid, out var mindId, out var mind, mc))
                    continue;

                // remove their antag role
                _role.MindRemoveRole<RevolutionaryRoleComponent>(mindId);

                // make it very obvious to the rev they've been deconverted since
                // they may not see the popup due to antag and/or new player tunnel vision
                if (_player.TryGetSessionById(mind.UserId, out var session))
                    _euiMan.OpenEui(new DeconvertedEui(), session);
            }
            return true;
        }

        return false;
    }

    private bool AreAllCommandStaffConverted()
    {
        var heads = AllEntityQuery<CommandStaffComponent>();
        while (heads.MoveNext(out var uid, out _))
        {
            if (!HasComp<RevolutionaryComponent>(uid))
                return false;
        }

        return true;
    }

    private bool AreAllCommandStaffDetained()
    {
        var heads = AllEntityQuery<CommandStaffComponent>();
        while (heads.MoveNext(out var uid, out _))
        {
            if (TryComp<CuffableComponent>(uid, out var cuffed))
            {
                if (cuffed.CuffedHandCount == 0)
                    return false;
            }
            else
                return false;
        }

        return true;
    }

    private bool AreAllCommandStaffDead()
    {
        var heads = AllEntityQuery<CommandStaffComponent, MobStateComponent>();
        while (heads.MoveNext(out _, out _, out var mob))
        {
            if (mob.CurrentState != MobState.Dead &&
                mob.CurrentState != MobState.Invalid)
                return false;
        }

        return true;
    }

    private bool AreAllHeadRevsDead()
    {
        var headRevs = AllEntityQuery<HeadRevolutionaryComponent, MobStateComponent>();
        while (headRevs.MoveNext(out _, out _, out var mob))
        {
            if (mob.CurrentState != MobState.Dead &&
                mob.CurrentState != MobState.Invalid)
                return false;
        }

        return true;
    }

    private string GetRevolutionaryOutcome()
    {
        var allCommandConverted = AreAllCommandStaffConverted();
        var allCommandDetained = AreAllCommandStaffDetained();
        var allCommandDead = AreAllCommandStaffDead();
        var allHeadRevsDead = AreAllHeadRevsDead();

        // All command staff were converted to revolutionaries
        if (allCommandConverted)
            return "rev-all-command-revs";

        // All command staff are detained or cuffed
        if (allCommandDetained)
            return "rev-command-detained";

        // All command staff are dead but head revs survived
        if (allCommandDead && !allHeadRevsDead)
            return "rev-command-dead";

        // All head revs are dead but command staff survived
        if (allHeadRevsDead && !allCommandDead)
            return "rev-lost";

        // Both head revs and command staff survived
        if (!allHeadRevsDead && !allCommandDead)
            return "rev-reverse-stalemate";

        // Both head revs and command staff are dead
        return "rev-stalemate";
    }
}
