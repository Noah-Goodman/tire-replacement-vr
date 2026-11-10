using TireTraining;
using VRBuilder.Core.Conditions;
using VRBuilder.Core.Editor.UI.StepInspector.Menu;

public class ItemStoredMenuItem : MenuItem<ICondition>
{
    public override string DisplayedName { get; } =
        "Tire Training/Item Stored";

    public override ICondition GetNewItem()
    {
        return new ItemStoredCondition();
    }
}
