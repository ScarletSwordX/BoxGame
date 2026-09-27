using System;
using System.Globalization;
using UnityEngine;

namespace RulePyramid.Editor
{
    public struct CellLabelLayout
    {
        public string Text;
        public int FontSize;
    }

    public static class EditorCellLabel
    {
        const int MaximumSize = 18;
        const int MinimumFullSize = 9;
        const int MinimumShortSize = 8;

        public static CellLabelLayout Fit(string name, string prefix, string suffix, Vector2 available,
            Func<string, int, Vector2> measure)
        {
            if (string.IsNullOrEmpty(name) || available.x <= 0 || available.y <= 0) return default;
            var full = (prefix ?? "") + name + (suffix ?? "");
            var result = FitText(full, MinimumFullSize, available, measure);
            if (!string.IsNullOrEmpty(result.Text)) return result;
            // 固定缩写只取名称，不包含相邻层箭头或同格数量。
            var characters = StringInfo.ParseCombiningCharacters(name);
            string three = characters.Length > 3 ? name.Substring(0, characters[3]) : name;
            result = FitText(three, MinimumFullSize, available, measure);
            if (!string.IsNullOrEmpty(result.Text)) return result;
            string ends = characters.Length > 2
                ? name.Substring(0, characters[1]) + name.Substring(characters[characters.Length - 1])
                : name;
            return FitText(ends, MinimumShortSize, available, measure);
        }

        static CellLabelLayout FitText(string text, int minimum, Vector2 available, Func<string, int, Vector2> measure)
        {
            for (int size = MaximumSize; size >= minimum; size--)
            {
                var measured = measure(text, size);
                if (measured.x <= available.x && measured.y <= available.y)
                    return new CellLabelLayout { Text = text, FontSize = size };
            }
            return default;
        }
    }
}
