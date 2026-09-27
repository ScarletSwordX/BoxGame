using System.Collections.Generic;

namespace RulePyramid.Core
{
    public static class Tokens
    {
        public const string MechanicsVersion = "RW-v0.9";
        public const int SchemaVersion = 9;

        public static readonly HashSet<string> Subjects = new HashSet<string>
        {
            "ROBOT", "ROCK", "CLOUD", "SPRING", "FLAG", "WALL", "LAVA"
        };

        public static readonly HashSet<string> Props = new HashSet<string>
        {
            "YOU", "PUSH", "STOP", "HOVER", "FLY", "WIN", "BOUNCY", "HOT", "MELT"
        };

        public static readonly HashSet<string> Operators = new HashSet<string>
        {
            "IS", "AND"
        };

        public static readonly HashSet<string> Commands = new HashSet<string>
        {
            "E", "W", "N", "S", "PE", "PW", "PN", "PS", "J", "WAIT"
        };

        // 旧参考解的兼容别名；实际执行和新录制统一为方向命令。
        public static readonly HashSet<string> PushCommands = new HashSet<string>
        {
            "PE", "PW", "PN", "PS"
        };

        public static readonly HashSet<string> LegacyJumps = new HashSet<string>
        {
            "JE", "JW", "JN", "JS"
        };

        /// <summary>兼容旧名；等同 Commands。</summary>
        public static readonly HashSet<string> SupportedCommands = Commands;

        public static string NormalizeCommand(string command) =>
            command != null && PushCommands.Contains(command) ? command.Substring(1) : command;

        public static bool IsSubject(string token) => !string.IsNullOrEmpty(token) && Subjects.Contains(token);
        public static bool IsProp(string token) => !string.IsNullOrEmpty(token) && Props.Contains(token);
        public static bool IsOperator(string token) => !string.IsNullOrEmpty(token) && Operators.Contains(token);

        public static bool IsLegalWord(string token)
        {
            return IsSubject(token) || IsProp(token) || IsOperator(token);
        }

        public static bool IsProperty(string token) => IsProp(token);
    }
}
