using System.Collections.Generic;

namespace RulePyramid.Runtime
{
    /// <summary>玩家界面英文文案；保留作者关卡文件中的原始设计文字。</summary>
    public static class PlayerText
    {
        static readonly Dictionary<string, string> Translations = new Dictionary<string, string>
        {
            { "文字改变世界 · 阶段 1", "Words Change Worlds" },
            { "文字改变世界 · 阶段 2", "Words Change Worlds" },
            { "文字改变世界 · 阶段 3", "Words Change Worlds" },
            { "借力向上", "Bounce Up" },
            { "把弹性搬过去", "Move the Bounce" },
            { "换一个我", "A New You" },
            { "同一间规则室：共用 IS", "Share IS" },
            { "同一座旗台：岩浆弹跳", "Lava Bounce" },
            { "回到左室：两个主体共用一句", "Share a Rule" },
            { "方向键 / WASD 移动", "WASD / Arrow keys to move" },
            { "没有可控制的对象了，按 Z 撤销。", "No YOU left. Press Z to undo." }
        };

        public static string English(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return Translations.TryGetValue(text, out var english) ? english : text;
        }
    }
}
