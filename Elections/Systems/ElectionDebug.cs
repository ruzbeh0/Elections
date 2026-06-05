namespace Elections.Systems
{
    internal static class ElectionDebug
    {
        public static bool Enabled => Mod.m_Setting?.EnableDebugLogging == true;

        public static void Log(string message)
        {
            if (Enabled)
                Mod.log.Info($"[Elections Debug] {message}");
        }
    }
}
