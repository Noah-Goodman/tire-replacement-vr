using UnityEngine;

namespace TireTraining
{
    /// <summary>The two player roles. <see cref="None"/> means no role has been chosen.</summary>
    public enum Role
    {
        None,
        Fetcher,
        Assembler
    }

    /// <summary>
    /// Per-role display values and the Photon session property keys that mark a role as taken.
    /// A role is taken when its key holds the int <see cref="TakenValue"/> in the session's custom properties.
    /// </summary>
    public static class RoleInfo
    {
        /// <summary>Session property value that marks a role as taken.</summary>
        public const int TakenValue = 1;

        private static readonly Color FetcherColor = new Color32(0xE5, 0x39, 0x35, 0xFF);
        private static readonly Color AssemblerColor = new Color32(0x1E, 0x88, 0xE5, 0xFF);
        private static readonly Color NoneColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        public static Color GetColor(this Role role)
        {
            switch (role)
            {
                case Role.Fetcher: return FetcherColor;
                case Role.Assembler: return AssemblerColor;
                default: return NoneColor;
            }
        }

        public static string GetDisplayName(this Role role)
        {
            switch (role)
            {
                case Role.Fetcher: return "Fetcher";
                case Role.Assembler: return "Assembler";
                default: return "";
            }
        }

        public static string GetSessionPropertyKey(this Role role)
        {
            switch (role)
            {
                case Role.Fetcher: return "role_fetcher";
                case Role.Assembler: return "role_assembler";
                default: return "";
            }
        }
    }
}
