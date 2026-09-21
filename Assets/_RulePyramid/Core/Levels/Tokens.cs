using System.Collections.Generic;

namespace RulePyramid.Core
{
    public static class Tokens
    {
        public const string MechanicsVersion = "RP-v0.5";
        public const int SchemaVersion = 5;

        public static readonly HashSet<string> Colors = new HashSet<string> { "RED", "BLUE", "PINK" };
        public static readonly HashSet<string> Properties = new HashSet<string> { "YOU", "BLOCK", "HOVER", "FLY", "WIN", "JUMP" };
        public static readonly HashSet<string> Operators = new HashSet<string> { "IS", "AND" };

        public static bool IsLegalWord(string token)
        {
            return Colors.Contains(token) || Properties.Contains(token) || Operators.Contains(token);
        }

        public static bool IsColor(string token) => Colors.Contains(token);
        public static bool IsProperty(string token) => Properties.Contains(token);

        public static readonly HashSet<string> SupportedCommands = new HashSet<string> { "E", "W", "N", "S", "J", "WAIT" };
        public static readonly HashSet<string> LegacyJumps = new HashSet<string> { "JE", "JW", "JN", "JS" };
    }
}
