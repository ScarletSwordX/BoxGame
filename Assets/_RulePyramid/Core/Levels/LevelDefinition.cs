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
        public FixedRuleData[] fixedRules;
        public OptionsData options;
        public CameraData camera;
        public TutorialData tutorial;
        public ReferenceSolutionData referenceSolution;

        public LevelDefinition Clone()
        {
            return LevelCloner.Clone(this);
        }
    }

    [Serializable]
    public class GridCellBox
    {
        public GridCell min;
        public GridCell max;
    }

    [Serializable]
    public class EntityDefinition
    {
        public string id;
        public string kind;
        public string color;
        public string token;
        public GridCell cell;
        public bool anchored;
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
        public string supportMode;
        public string jumpMode;
        public int bounceRiseCells;
        public string decisionMode;
        public string[] ruleAxes;
        public string winMode;
        public string winCheckMode;
        public string actionMode;
        public string gravityMode;
        public string playerBlockMode;
    }

    [Serializable]
    public class CameraData
    {
        public int initialSlot;
        public float pitchDegrees;
        public float[] yawDegrees;
        public string inputMode;
    }

    [Serializable]
    public class TutorialData
    {
        public string objective;
        public string[] hints;
    }

    [Serializable]
    public class ReferenceSolutionData
    {
        public string[] commands;
        public string expectedFinalStatus;
    }
}
