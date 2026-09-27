using System;
using UnityEngine;
using RulePyramid.Core;

namespace RulePyramid.Runtime
{
    [Serializable]
    public sealed class LevelStageSequence
    {
        public string id;
        public TextAsset[] maps;
    }

    [CreateAssetMenu(menuName = "规则工坊/关卡目录", fileName = "LevelCatalog")]
    public class LevelCatalog : ScriptableObject
    {
        public TextAsset[] levels;
        public LevelStageSequence[] stageSequences;

        LevelStageSequence Sequence(int index)
        {
            if (levels == null || index < 0 || index >= levels.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (levels[index] == null) throw new InvalidOperationException("缺少关卡资源：" + index);
            foreach (var sequence in stageSequences ?? Array.Empty<LevelStageSequence>())
                if (sequence?.maps != null && sequence.maps.Length > 0 && sequence.maps[0] == levels[index])
                    return sequence;
            return null;
        }

        public int StageCount(int index) => Sequence(index)?.maps.Length ?? 1;
        public LevelDefinition Load(int index) => LoadStage(index, 0);

        public LevelDefinition LoadStage(int index, int stage)
        {
            var sequence = Sequence(index);
            int count = sequence?.maps.Length ?? 1;
            if (stage < 0 || stage >= count) throw new ArgumentOutOfRangeException(nameof(stage));
            var asset = sequence == null ? levels[index] : sequence.maps[stage];
            if (asset == null) throw new InvalidOperationException("缺少阶段地图：" + index + "/" + stage);
            return LevelJsonSerializer.FromJson(asset.text);
        }

        public int Count => levels == null ? 0 : levels.Length;

        public int IndexOf(string id)
        {
            if (levels == null) return -1;
            for (int i = 0; i < levels.Length; i++)
            {
                try
                {
                    var sequence = Sequence(i);
                    if (sequence != null && sequence.id == id) return i;
                    for (int stage = 0; stage < StageCount(i); stage++)
                        if (LoadStage(i, stage).id == id) return i;
                }
                catch { /* 忽略无效目录项，加载时仍会报告错误。 */ }
            }
            return -1;
        }
    }
}
