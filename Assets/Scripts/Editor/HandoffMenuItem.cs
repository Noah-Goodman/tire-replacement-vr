using TireTraining;
using VRBuilder.Core.Conditions;
using VRBuilder.Core.Editor.UI.StepInspector.Menu;

public class HandoffMenuItem : MenuItem<ICondition>
{
    public override string DisplayedName { get; } =
        "Tire Training/Hand Over Item";

    public override ICondition GetNewItem()
    {
        return new HandoffCondition();
    }
}
