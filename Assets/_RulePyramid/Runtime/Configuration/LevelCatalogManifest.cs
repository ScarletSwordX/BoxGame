using System;

namespace RulePyramid.Runtime
{
    /// <summary>正式章节与阶段顺序的文件清单；编辑器导入为构建可用的资源引用。</summary>
    [Serializable]
    public sealed class LevelCatalogManifest
    {
        public int schemaVersion;
        public string mechanicsVersion;
        public string[] levels;
        public LevelStageManifest[] stageSequences;
    }

    [Serializable]
    public sealed class LevelStageManifest
    {
        public string id;
        public string[] maps;
    }
}
