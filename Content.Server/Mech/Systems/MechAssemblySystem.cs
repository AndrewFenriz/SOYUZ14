using Content.Server.Mech.Components;
using Content.Shared.Interaction;
using Content.Shared.Tag;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Server.Containers;
using Robust.Shared.Containers;

namespace Content.Server.Mech.Systems;

public sealed class MechAssemblySystem : EntitySystem
{
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedToolSystem _toolSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MechAssemblyComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<MechAssemblyComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnInit(EntityUid uid, MechAssemblyComponent component, ComponentInit args)
    {
        component.PartsContainer = _container.EnsureContainer<Container>(uid, "mech-assembly-container");
    }

    private void OnInteractUsing(EntityUid uid, MechAssemblyComponent component, InteractUsingEvent args)
    {
        if (args.Handled) // DS14: помечаем, что событие обработано
            return;

        if (_toolSystem.HasQuality(args.Used, component.QualityNeeded))
        {
            foreach (var tag in component.RequiredParts.Keys)
            {
                component.RequiredParts[tag] = false;
            }
            _container.EmptyContainer(component.PartsContainer);
            args.Handled = true;
            return;
        }

        if (!TryComp<TagComponent>(args.Used, out var tagComp))
            return;

        bool partAdded = false; // DS14: флаг установки детали
        foreach (var (tag, val) in component.RequiredParts)
        {
            if (!val && _tag.HasTag(tagComp, tag))
            {
                component.RequiredParts[tag] = true;
                if (_container.Insert(args.Used, component.PartsContainer))
                {
                    partAdded = true; // DS14: отслеживание успешной установки
                    args.Handled = true;
                }
                break;
            }
        }

        if (!partAdded)
            return;

        foreach (var val in component.RequiredParts.Values) // DS14: валидация сборки
        {
            if (!val)
                return;
        }

        var coords = Transform(uid).Coordinates;
        var mech = Spawn(component.FinishedPrototype, coords);

        if (mech.Valid)
        {
            EntityManager.DeleteEntity(uid); // DS14: удаление
            args.Handled = true;
        }
    }
}
