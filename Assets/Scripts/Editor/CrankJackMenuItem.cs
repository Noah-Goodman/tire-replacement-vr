using TireTraining;
using VRBuilder.Core.Conditions;
using VRBuilder.Core.Editor.UI.StepInspector.Menu;

public class CrankJackMenuItem : MenuItem<ICondition>
{
    public override string DisplayedName { get; } =
        "Tire Training/Crank Jack";

    public override ICondition GetNewItem()
    {
        return new CrankJackCondition();
    }
}
