namespace Content.Server.DeadSpace.Revolutionary;

[RegisterComponent, Access(typeof(RevolutionOverthrowConditionSystem))]
public sealed partial class RevolutionOverthrowConditionComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    public float Progress = 0;
}
