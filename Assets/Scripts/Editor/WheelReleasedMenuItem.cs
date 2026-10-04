using TireTraining;
using VRBuilder.Core.Conditions;
using VRBuilder.Core.Editor.UI.StepInspector.Menu;

public class WheelReleasedMenuItem : MenuItem<ICondition>
{
    public override string DisplayedName { get; } = "Tire Training/Release Removed Wheel";

    public override ICondition GetNewItem()
    {
        return new WheelReleasedCondition();
    }
}
