using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Elections.Systems;
using System.IO;

namespace Elections
{
    public class Mod : IMod
    {
        public const string Id = "Elections";
        public static ILog log = LogManager.GetLogger($"{nameof(Elections)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        public static Setting m_Setting;
        public static string ModDirectory { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                log.Info($"Current mod asset at {asset.path}");
                ModDirectory = Path.GetDirectoryName(asset.path);
            }

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));


            AssetDatabase.global.LoadSettings(nameof(Elections), m_Setting, new Setting(this));
            ElectionDebug.Log($"Settings loaded: EnableElections={m_Setting.EnableElections}, ElectionDayActsLikeSunday={m_Setting.ElectionDayActsLikeSunday}, UseUniversalModMenu={m_Setting.UseUniversalModMenu}, PollSamplePercent={m_Setting.PollSamplePercent}, HourlyVotingAttemptPercent={m_Setting.HourlyVotingAttemptPercent}.");
            CandidatePortraitCatalog.WarmupCache();
            ElectionDebug.Log("Candidate portrait cache warmed.");

            updateSystem.UpdateAt<ElectionLifecycleSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<ElectionVotingSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<MayorEffectSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<MayorWorkplaceSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<ElectionUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<ElectionVotingLocationOverlaySystem>(SystemUpdatePhase.Rendering);
            ElectionDebug.Log("Elections systems registered.");
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }
            CandidatePortraitCatalog.ClearCache();
        }
    }
}
