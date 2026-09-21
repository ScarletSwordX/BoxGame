using UnityEngine;
using RulePyramid.Core;

namespace RulePyramid.Runtime
{
    [CreateAssetMenu(menuName = "RulePyramid/Level Catalog", fileName = "LevelCatalog")]
    public class LevelCatalog : ScriptableObject
    {
        public TextAsset[] levels;

        public LevelDefinition Load(int index)
        {
            if (levels == null || index < 0 || index >= levels.Length)
                throw new System.ArgumentOutOfRangeException(nameof(index));
            var asset = levels[index];
            if (asset == null) throw new System.InvalidOperationException("Missing level TextAsset at " + index);
            return LevelJsonSerializer.FromJson(asset.text);
        }

        public int Count => levels == null ? 0 : levels.Length;

        public int IndexOf(string id)
        {
            if (levels == null) return -1;
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i] == null) continue;
                try
                {
                    var level = LevelJsonSerializer.FromJson(levels[i].text);
                    if (level.id == id) return i;
                }
                catch
                {
                    // skip invalid catalog entries during lookup
                }
            }
            return -1;
        }
    }
}
