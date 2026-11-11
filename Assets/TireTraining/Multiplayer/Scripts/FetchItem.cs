namespace TireTraining
{
    /// <summary>The shared items the Fetcher brings to the Assembler. <see cref="None"/> means no item is involved.</summary>
    public enum FetchItem
    {
        None,
        LugWrench,
        ScrewJack,
        OldWheel,
        ReplacementWheel,
        LugNutTray
    }

    /// <summary>Display values for <see cref="FetchItem"/>.</summary>
    public static class FetchItemInfo
    {
        /// <summary>Lower-case name of the item for use in a sentence; empty for <see cref="FetchItem.None"/>.</summary>
        public static string GetDisplayName(this FetchItem item)
        {
            switch (item)
            {
                case FetchItem.LugWrench: return "lug wrench";
                case FetchItem.ScrewJack: return "screw jack";
                case FetchItem.OldWheel: return "old wheel";
                case FetchItem.ReplacementWheel: return "replacement wheel";
                case FetchItem.LugNutTray: return "lug nut tray";
                default: return "";
            }
        }
    }
}
