using System;
using RulePyramid.Core;
namespace RulePyramid.Editor
{
    public partial class LevelEditorWindow
    {
        static LevelDefinition CreateEmpty()
        {
            return new LevelDefinition
            {
                schemaVersion = Tokens.SchemaVersion,
                mechanicsVersion = Tokens.MechanicsVersion,
                id = "draft",
                title = "草稿",
                bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(7, 8, 7) },
                terrain = new[] { new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(7, 0, 7) } },
                entities = new[]
                {
                    new EntityDefinition { id = "t_robot", kind = "Text", subject = "", token = "ROBOT", cell = new GridCell(1, 1, 1), anchored = false },
                    new EntityDefinition { id = "t_is", kind = "Text", subject = "", token = "IS", cell = new GridCell(2, 1, 1), anchored = false },
                    new EntityDefinition { id = "t_you", kind = "Text", subject = "", token = "YOU", cell = new GridCell(3, 1, 1), anchored = false },
                    new EntityDefinition { id = "player", kind = "Object", subject = "ROBOT", token = "", cell = new GridCell(3, 1, 3), anchored = false }
                },
                fixedRules = Array.Empty<FixedRuleData>(),
                options = new OptionsData
                {
                    actionMode = "MoveAutoPush",
                    winMode = "DistinctEntitiesSameCell",
                    gravityMode = "WorldDownExceptHoverOrFly",
                    collisionMode = "SolidPairsTerrainUniversal",
                    solidityMode = "YouPushStopOrText",
                    supportMode = "StrictBelow",
                    controlMode = "SingleYouTransfer_NoControlUndo",
                    bounceRiseCells = 3,
                    transformationMode = "PermanentSingleTarget_SimultaneousOncePerEntityPerCommand",
                    ruleSourceMode = "WorldTextOnly",
                    textMobilityMode = "AllWordsMovable_GeometryAccess"
                },
                camera = new CameraData { initialSlot = 0, slot = 0, pitchDegrees = 35.264f, yawDegrees = new[] { 45f, 135f, 225f, 315f }, inputMode = "CameraRelativeGrid", orthographic = true },
                designContract = new DesignContractData { requireActiveInteraction = false, minimumSolutionFamilies = 0, interactionIsAuthoringConstraint = true },
                tutorial = new TutorialData { objective = "", hints = Array.Empty<string>() },
                referenceSolutions = Array.Empty<ReferenceSolutionData>()
            };
        }

    }
}
