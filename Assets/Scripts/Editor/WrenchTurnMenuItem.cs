using TireTraining;
using VRBuilder.Core.Conditions;
using VRBuilder.Core.Editor.UI.StepInspector.Menu;

public class WrenchTurnMenuItem : MenuItem<ICondition>
{
    public override string DisplayedName { get; } =
        "Tire Training/Turn Wrench Once";

    public override ICondition GetNewItem()
    {
        return new WrenchTurnCondition();
    }
}