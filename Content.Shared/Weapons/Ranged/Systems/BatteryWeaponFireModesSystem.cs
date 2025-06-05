using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared.Weapons.Ranged.Systems;

public sealed class BatteryWeaponFireModesSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BatteryWeaponFireModesComponent, UseInHandEvent>(OnUseInHandEvent);
        SubscribeLocalEvent<BatteryWeaponFireModesComponent, GetVerbsEvent<Verb>>(OnGetVerb);
        SubscribeLocalEvent<BatteryWeaponFireModesComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, BatteryWeaponFireModesComponent component, ExaminedEvent args)
    {
        if (component.FireModes.Count < 2)
            return;

        var fireMode = GetMode(component);
        var protoName = GetPrototypeNameSafe(fireMode.Prototype);

        if (protoName != null)
            args.PushMarkup(Loc.GetString("gun-set-fire-mode", ("mode", protoName)));
    }

    private BatteryWeaponFireMode GetMode(BatteryWeaponFireModesComponent component)
    {
        return component.FireModes[component.CurrentFireMode];
    }

    // Добавлено: Безопасное получение имени прототипа без ошибок
    private string? GetPrototypeNameSafe(string prototypeId)
    {
        if (_prototypeManager.HasIndex<EntityPrototype>(prototypeId))
            return _prototypeManager.Index<EntityPrototype>(prototypeId).Name;

        if (_prototypeManager.HasIndex<HitscanPrototype>(prototypeId))
            return _prototypeManager.Index<HitscanPrototype>(prototypeId).ID;

        return null;
    }

    private void OnGetVerb(EntityUid uid, BatteryWeaponFireModesComponent component, GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract)
            return;

        if (component.FireModes.Count < 2)
            return;

        if (!_accessReaderSystem.IsAllowed(args.User, uid))
            return;

        for (var i = 0; i < component.FireModes.Count; i++)
        {
            var fireMode = component.FireModes[i];
            var protoName = GetPrototypeNameSafe(fireMode.Prototype);

            if (protoName == null)
                continue;

            var index = i;
            var v = new Verb
            {
                Priority = 1,
                Category = VerbCategory.SelectType,
                Text = protoName,
                Disabled = i == component.CurrentFireMode,
                Impact = LogImpact.Medium,
                DoContactInteraction = true,
                Act = () => TrySetFireMode(uid, component, index, args.User)
            };

            args.Verbs.Add(v);
        }
    }

    private void OnUseInHandEvent(EntityUid uid, BatteryWeaponFireModesComponent component, UseInHandEvent args)
    {
        TryCycleFireMode(uid, component, args.User);
    }

    public void TryCycleFireMode(EntityUid uid, BatteryWeaponFireModesComponent component, EntityUid? user = null)
    {
        if (component.FireModes.Count < 2)
            return;

        var index = (component.CurrentFireMode + 1) % component.FireModes.Count;
        TrySetFireMode(uid, component, index, user);
    }

    public bool TrySetFireMode(EntityUid uid, BatteryWeaponFireModesComponent component, int index, EntityUid? user = null)
    {
        if (index < 0 || index >= component.FireModes.Count)
            return false;

        if (user != null && !_accessReaderSystem.IsAllowed(user.Value, uid))
            return false;

        SetFireMode(uid, component, index, user);

        return true;
    }

    private void SetFireMode(EntityUid uid, BatteryWeaponFireModesComponent component, int index, EntityUid? user = null)
    {
        var fireMode = component.FireModes[index];
        component.CurrentFireMode = index;
        Dirty(uid, component);

        var protoName = GetPrototypeNameSafe(fireMode.Prototype);
        if (protoName != null)
        {
            if (TryComp<AppearanceComponent>(uid, out var appearance))
                _appearanceSystem.SetData(uid, BatteryWeaponFireModeVisuals.State, fireMode.Prototype, appearance);

            if (user != null)
                _popupSystem.PopupClient(Loc.GetString("gun-set-fire-mode", ("mode", protoName)), uid, user.Value);
        }

        // Обновляем компоненты только если прототип существует
        if (_prototypeManager.HasIndex<HitscanPrototype>(fireMode.Prototype) &&
            TryComp(uid, out HitscanBatteryAmmoProviderComponent? hitscanProvider))
        {
            hitscanProvider.Prototype = fireMode.Prototype;
            hitscanProvider.FireCost = fireMode.FireCost;
            Dirty(uid, hitscanProvider);
        }
        else if (_prototypeManager.HasIndex<EntityPrototype>(fireMode.Prototype) &&
                 TryComp(uid, out ProjectileBatteryAmmoProviderComponent? projectileProvider))
        {
            var oldFireCost = projectileProvider.FireCost;
            projectileProvider.Prototype = fireMode.Prototype;
            projectileProvider.FireCost = fireMode.FireCost;

            float fireCostDiff = (float)fireMode.FireCost / (float)oldFireCost;
            projectileProvider.Shots = (int)Math.Round(projectileProvider.Shots / fireCostDiff);
            projectileProvider.Capacity = (int)Math.Round(projectileProvider.Capacity / fireCostDiff);

            Dirty(uid, projectileProvider);
        }

        var updateClientAmmoEvent = new UpdateClientAmmoEvent();
        RaiseLocalEvent(uid, ref updateClientAmmoEvent);
    }
}
