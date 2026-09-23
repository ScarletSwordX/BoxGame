using System;

namespace RulePyramid.Core
{
    [Serializable]
    public class LevelDefinition
    {
        public int schemaVersion;
        public string mechanicsVersion;
        public string id;
        public string title;
        public GridCellBox bounds;
        public GridCellBox[] terrain;
        public EntityDefinition[] entities;
        /// <summary>v0.9 禁止；若 JSON 仍含此字段，加载时结构错误。</summary>
        public FixedRuleData[] fixedRules;
        public OptionsData options;
        public CameraData camera;
        public DesignContractData designContract;
        public ReferenceSolutionData[] referenceSolutions;
        /// <summary>旧单数字段；Normalize 时可迁入 referenceSolutions。</summary>
        public ReferenceSolutionData referenceSolution;
        public TutorialData tutorial;

        public LevelDefinition Clone()
        {
            return LevelCloner.Clone(this);
        }
    }

    [Serializable]
    public class GridCellBox
    {
        public string id;
        public GridCell min;
        public GridCell max;
        public string appearance;
    }

    [Serializable]
    public class EntityDefinition
    {
        public string id;
        public string kind;
        public string subject;
        public string token;
        public GridCell cell;
        public bool anchored;
        /// <summary>遗留字段；加载后忽略。</summary>
        public string color;
    }

    [Serializable]
    public class FixedRuleData
    {
        public string id;
        public string[] tokens;
    }

    [Serializable]
    public class OptionsData
    {
        public string actionMode;
        public string winMode;
        public string gravityMode;
        public string collisionMode;
        public string solidityMode;
        public string supportMode;
        public string controlMode;
        public int bounceRiseCells;
        public string transformationMode;
        public string ruleSourceMode;
        public string textMobilityMode;
    }

    [Serializable]
    public class CameraData
    {
        public int initialSlot;
        /// <summary>Pack JSON 字段名；Normalize 复制到 initialSlot。</summary>
        public int slot;
        public float pitchDegrees;
        public float[] yawDegrees;
        public string inputMode;
        public bool orthographic;
    }

    [Serializable]
    public class DesignContractData
    {
        public bool requireActiveInteraction;
        public int minimumSolutionFamilies;
        public bool interactionIsAuthoringConstraint;
    }

    [Serializable]
    public class TutorialData
    {
        public string objective;
        public string concept;
        public string observation;
        public string necessity;
        public string[] hints;
        public string[] risks;
    }

    [Serializable]
    public class ReferenceSolutionData
    {
        public string id;
        public string name;
        public string family;
        public string[] commands;
        public string mustControlAtWin;
        public string mustWinWith;
        public string[] requireEvents;
        public string[] forbidEvents;
        public string expectedFinalStatus;
        public string status;
        public string claim;
    }
}
