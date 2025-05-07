namespace Content.Server.DeadSpace.MechGun;

[RegisterComponent]
public sealed partial class MechGunComponent : Component
{
}
public sealed class MechShootEvent : CancellableEntityEventArgs
{
    public EntityUid User;

    public MechShootEvent(EntityUid user)
    {
        User = user;
    }
}
