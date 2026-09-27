namespace RulePyramid.Core
{
    /// <summary>所有关卡共用的当前状态提示，优先于区域教学与手动提示。</summary>
    public static class GlobalStatusHint
    {
        public const string NoControlText = "没有可控制的对象了，按 Z 撤销。";

        public static string Current(GameSession session)
        {
            return session != null && session.Phase == MotionPhase.NoControl ? NoControlText : null;
        }

        public static string DisplayText(GameSession session, string regionText, string manualText)
        {
            return Current(session) ?? regionText ?? manualText ?? "";
        }
    }
}
