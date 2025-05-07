using Content.Server.Mech.Systems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Mech.Components;
using Content.Shared.Mech.Equipment.Components;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.Containers;
using Robust.Shared.Random;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.MechGun;

public sealed class MechGunSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly MechSystem _mech = default!;
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedGunSystem _gunSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MechEquipmentComponent, GunShotEvent>(OnMechGunShot);
        SubscribeLocalEvent<GunComponent, ComponentRemove>(OnGunRemoved);
    }

    private void OnGunRemoved(EntityUid uid, GunComponent component, ComponentRemove args)
    {
        _gunSystem.ResetMechGun(uid, component);
    }

    private void OnMechGunShot(EntityUid uid, MechEquipmentComponent component, ref GunShotEvent args)
    {
        if (!TryComp<GunComponent>(uid, out var gunComp))
            return;

        // Блокировка неавторизованного использования
        if (!component.EquipmentOwner.HasValue)
        {
            args.Cancel(); // Теперь это будет работать
            ParalysisAndThrowUser(args.User, paralysisSeconds: 10, throwForce: 20);
            return;
        }

        var mechUid = component.EquipmentOwner.Value;

        if (!TryComp<MechComponent>(mechUid, out var mech))
        {
            args.Cancel();
            ParalysisAndThrowUser(args.User, paralysisSeconds: 10, throwForce: 20);
            return;
        }

        // Зарядка батареи
        if (TryComp<BatteryComponent>(uid, out var battery))
        {
            var neededCharge = battery.MaxCharge - battery.CurrentCharge;
            if (neededCharge > 0 && mech.Energy >= neededCharge)
            {
                _mech.TryChangeEnergy(mechUid, -neededCharge, mech);
                _battery.SetCharge(uid, battery.MaxCharge, battery);
            }
        }

        // Выброс гильз
        foreach (var (entOpt, _) in args.Ammo)
        {
            if (!entOpt.HasValue || !mech.EquipmentContainer.Contains(entOpt.Value))
                continue;

            _container.Remove(entOpt.Value, mech.EquipmentContainer);
            _throwing.TryThrow(entOpt.Value, _random.NextVector2(), _random.Next(5));
        }

        // Сброс состояния оружия
        _gunSystem.ResetMechGun(uid, gunComp);
    }

    private void ParalysisAndThrowUser(EntityUid? user, int paralysisSeconds, float throwForce)
    {
        if (user == null)
            return;

        _stun.TryParalyze(user.Value, TimeSpan.FromSeconds(paralysisSeconds), true);
        _throwing.TryThrow(user.Value, _random.NextVector2(), (int)throwForce);
    }
}
