using UnityEngine;

namespace RulePyramid.Runtime
{
    /// <summary>记录已完成的最远关卡；阶段内进度不写入存档。</summary>
    public static class CampaignProgress
    {
        public const string LastCompletedLevelKey = "RuleWorkshop.Campaign.LastCompletedLevelId.v1";

        public static bool TryGetNextIndex(LevelCatalog catalog, out int nextIndex)
        {
            nextIndex = -1;
            if (catalog == null || catalog.Count < 2 || !PlayerPrefs.HasKey(LastCompletedLevelKey))
                return false;
            int completedIndex = catalog.IndexOf(PlayerPrefs.GetString(LastCompletedLevelKey));
            if (completedIndex < 0 || completedIndex + 1 >= catalog.Count) return false;
            nextIndex = completedIndex + 1;
            return true;
        }

        public static void RecordCompleted(LevelCatalog catalog, int completedIndex)
        {
            if (catalog == null || completedIndex < 0 || completedIndex >= catalog.Count) return;
            string completedId = catalog.Load(completedIndex).id;
            if (string.IsNullOrEmpty(completedId)) return;
            int savedIndex = catalog.IndexOf(PlayerPrefs.GetString(LastCompletedLevelKey, ""));
            if (savedIndex >= completedIndex) return;
            PlayerPrefs.SetString(LastCompletedLevelKey, completedId);
            PlayerPrefs.Save();
        }
    }
}
