namespace RulePyramid.Core
{
    public enum WorldDirection
    {
        East,
        West,
        North,
        South
    }

    public static class WorldDirections
    {
        public static bool TryParse(string token, out WorldDirection direction)
        {
            switch (token)
            {
                case "E":
                    direction = WorldDirection.East;
                    return true;
                case "W":
                    direction = WorldDirection.West;
                    return true;
                case "N":
                    direction = WorldDirection.North;
                    return true;
                case "S":
                    direction = WorldDirection.South;
                    return true;
                default:
                    direction = WorldDirection.East;
                    return false;
            }
        }

        public static string ToToken(WorldDirection direction)
        {
            switch (direction)
            {
                case WorldDirection.East: return "E";
                case WorldDirection.West: return "W";
                case WorldDirection.North: return "N";
                case WorldDirection.South: return "S";
                default: return "?";
            }
        }

        public static GridCell ToOffset(WorldDirection direction)
        {
            switch (direction)
            {
                case WorldDirection.East: return GridCell.East;
                case WorldDirection.West: return GridCell.West;
                case WorldDirection.North: return GridCell.North;
                case WorldDirection.South: return GridCell.South;
                default: return default;
            }
        }
    }
}
