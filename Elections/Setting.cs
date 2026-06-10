using Colossal;
using Colossal.IO.AssetDatabase;
using Elections.Systems;
using Game.Modding;
using Game.SceneFlow;
using Game.Settings;
using Game.UI;
using Game.UI.Widgets;
using System.Collections.Generic;
using Unity.Entities;

namespace Elections
{
    [FileLocation($"ModsSettings\\{nameof(Elections)}\\{nameof(Elections)}")]
    [SettingsUIGroupOrder(kGeneralGroup, kVotingGroup, kCampaignGroup, kDebugGroup)]
    [SettingsUIShowGroupName(kGeneralGroup, kVotingGroup, kCampaignGroup, kDebugGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kGeneralGroup = "General";
        public const string kVotingGroup = "Voting";
        public const string kCampaignGroup = "Campaign";
        public const string kDebugGroup = "Debug";

        public enum VotingStartHourOption
        {
            Hour6 = 6,
            Hour7 = 7,
            Hour8 = 8,
            Hour9 = 9,
            Hour10 = 10
        }

        public enum VotingEndHourOption
        {
            Hour16 = 16,
            Hour17 = 17,
            Hour18 = 18
        }

        public enum TurnoutCountryPreset
        {
            Baseline = 0,
            EuropeanUnion = 2,
            Brazil = 25,
            Canada = 34,
            France = 62,
            Germany = 66,
            UK = 187,
            USA = 188
        }

        public Setting(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool EnableElections { get; set; }

        [SettingsUISection(kSection, kGeneralGroup)]
        [SettingsUISetter(typeof(Setting), nameof(SetUseUniversalModMenu))]
        public bool UseUniversalModMenu { get; set; }

        [SettingsUISlider(min = 100, max = 200, step = 5, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kSection, kGeneralGroup)]
        public int VotingSiteOverlayScalePercent { get; set; } = 120;

        [SettingsUISlider(min = 1, max = 10, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kSection, kVotingGroup)]
        public int PollSamplePercent { get; set; }

        [SettingsUISection(kSection, kVotingGroup)]
        public TurnoutCountryPreset AgeTurnoutCountryPreset { get; set; } = TurnoutCountryPreset.Baseline;

        [SettingsUIButton]
        [SettingsUISection(kSection, kVotingGroup)]
        public bool ApplyAgeTurnoutCountryPreset
        {
            set
            {
                if (!value)
                    return;

                ApplyAgeTurnoutPreset(AgeTurnoutCountryPreset);
            }
        }

        [SettingsUISlider(min = 1, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kSection, kVotingGroup)]
        public int TeenDailyVotingTurnoutPercent { get; set; } = ElectionUtility.DefaultTeenDailyVotingTurnoutPercent;

        [SettingsUISlider(min = 1, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kSection, kVotingGroup)]
        public int AdultDailyVotingTurnoutPercent { get; set; } = ElectionUtility.DefaultAdultDailyVotingTurnoutPercent;

        [SettingsUISlider(min = 1, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kSection, kVotingGroup)]
        public int ElderlyDailyVotingTurnoutPercent { get; set; } = ElectionUtility.DefaultElderlyDailyVotingTurnoutPercent;

        [SettingsUISection(kSection, kVotingGroup)]
        public VotingStartHourOption ElectionVotingStartHour { get; set; } = VotingStartHourOption.Hour8;

        [SettingsUISection(kSection, kVotingGroup)]
        public VotingEndHourOption ElectionVotingEndHour { get; set; } = VotingEndHourOption.Hour17;

        [SettingsUISection(kSection, kDebugGroup)]
        public bool EnableDebugLogging { get; set; }

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kSection, kDebugGroup)]
        public bool ForceStartCampaign
        {
            set
            {
                if (!value)
                    return;

                var world = World.DefaultGameObjectInjectionWorld;
                var system = world?.GetExistingSystemManaged<ElectionLifecycleSystem>();
                system?.ForceStartCampaignFromSettings();
            }
        }

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kSection, kDebugGroup)]
        public bool ForceElectionToday
        {
            set
            {
                if (!value)
                    return;

                var world = World.DefaultGameObjectInjectionWorld;
                var system = world?.GetExistingSystemManaged<ElectionLifecycleSystem>();
                system?.ForceElectionTodayFromSettings();
            }
        }

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kSection, kDebugGroup)]
        public bool ForceGeneratePoll
        {
            set
            {
                if (!value)
                    return;

                var world = World.DefaultGameObjectInjectionWorld;
                var system = world?.GetExistingSystemManaged<ElectionLifecycleSystem>();
                system?.ForceGeneratePollFromSettings();
            }
        }

        public override void SetDefaults()
        {
            EnableElections = true;
            UseUniversalModMenu = false;
            VotingSiteOverlayScalePercent = 120;
            PollSamplePercent = 2;
            AgeTurnoutCountryPreset = TurnoutCountryPreset.Baseline;
            TeenDailyVotingTurnoutPercent = ElectionUtility.DefaultTeenDailyVotingTurnoutPercent;
            AdultDailyVotingTurnoutPercent = ElectionUtility.DefaultAdultDailyVotingTurnoutPercent;
            ElderlyDailyVotingTurnoutPercent = ElectionUtility.DefaultElderlyDailyVotingTurnoutPercent;
            ElectionVotingStartHour = VotingStartHourOption.Hour8;
            ElectionVotingEndHour = VotingEndHourOption.Hour17;
            EnableDebugLogging = false;
        }

        public void SetUseUniversalModMenu(bool value)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var system = world?.GetExistingSystemManaged<ElectionUISystem>();
            system?.UpdateUseUniversalModMenu(value);
        }

        public void ApplyAgeTurnoutPreset(TurnoutCountryPreset preset)
        {
            // Rounded from published age turnout tables; adult values average available adult sub-bands.
            switch (preset)
            {
                case TurnoutCountryPreset.EuropeanUnion:
                    SetDailyTurnoutPercentages(36, 49, 58);
                    break;
                case TurnoutCountryPreset.Brazil:
                    SetDailyTurnoutPercentages(79, 84, 56);
                    break;
                case TurnoutCountryPreset.Canada:
                    SetDailyTurnoutPercentages(56, 67, 74);
                    break;
                case TurnoutCountryPreset.France:
                    SetDailyTurnoutPercentages(28, 52, 61);
                    break;
                case TurnoutCountryPreset.Germany:
                    SetDailyTurnoutPercentages(79, 83, 82);
                    break;
                case TurnoutCountryPreset.UK:
                    SetDailyTurnoutPercentages(37, 52, 74);
                    break;
                case TurnoutCountryPreset.USA:
                    SetDailyTurnoutPercentages(48, 65, 75);
                    break;
                default:
                    SetDailyTurnoutPercentages(
                        ElectionUtility.DefaultTeenDailyVotingTurnoutPercent,
                        ElectionUtility.DefaultAdultDailyVotingTurnoutPercent,
                        ElectionUtility.DefaultElderlyDailyVotingTurnoutPercent);
                    break;
            }
        }

        private void SetDailyTurnoutPercentages(int teen, int adult, int elderly)
        {
            TeenDailyVotingTurnoutPercent = ClampTurnoutPercent(teen);
            AdultDailyVotingTurnoutPercent = ClampTurnoutPercent(adult);
            ElderlyDailyVotingTurnoutPercent = ClampTurnoutPercent(elderly);
        }

        private static int ClampTurnoutPercent(int value)
        {
            if (value < 1)
                return 1;

            return value > 100 ? 100 : value;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Elections" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup), "General" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kVotingGroup), "Voting" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kCampaignGroup), "Campaign" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kDebugGroup), "Debug" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableElections)), "Enable Elections" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableElections)), "Run the mayoral election cycle." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UseUniversalModMenu)), "Use Universal Mod Menu" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UseUniversalModMenu)), "Show the Elections button in Universal Mod Menu instead of the top-left game overlay." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.VotingSiteOverlayScalePercent)), "Voting Site Overlay Size" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.VotingSiteOverlayScalePercent)), "Size of the voting-site marker, ballot icon, and vote count. 100% = 1.0 scale, 120% is the default, and changes apply immediately." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.PollSamplePercent)), "Poll Sample Percent" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.PollSamplePercent)), "Approximate share of total city population represented in the campaign poll. Default is 2% and the maximum is 10%." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AgeTurnoutCountryPreset)), "Age Turnout Country" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AgeTurnoutCountryPreset)), "Choose a real-world age turnout preset from countries with published age turnout data, plus the European Union." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ApplyAgeTurnoutCountryPreset)), "Apply Age Turnout Preset" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ApplyAgeTurnoutCountryPreset)), "Copies the selected country's rounded age turnout values into the teen, adult, and elderly daily turnout sliders." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.TeenDailyVotingTurnoutPercent)), "Teen Daily Turnout" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.TeenDailyVotingTurnoutPercent)), "Daily probability that an eligible teen resident votes. Default is 36%; the simulation divides this by the configured voting hours." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AdultDailyVotingTurnoutPercent)), "Adult Daily Turnout" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AdultDailyVotingTurnoutPercent)), "Daily probability that an eligible adult resident votes. Default is 49%; the simulation divides this by the configured voting hours." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ElderlyDailyVotingTurnoutPercent)), "Elderly Daily Turnout" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ElderlyDailyVotingTurnoutPercent)), "Daily probability that an eligible elderly resident votes. Default is 58%; the simulation divides this by the configured voting hours." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ElectionVotingStartHour)), "Voting Start Hour" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ElectionVotingStartHour)), "Election day voting start time. Default is 8:00 AM." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ElectionVotingEndHour)), "Voting End Hour" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ElectionVotingEndHour)), "Election day voting end time. Default is 5:00 PM. Results are announced at 8:00 PM." },

                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour6), "6:00 AM" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour7), "7:00 AM" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour8), "8:00 AM" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour9), "9:00 AM" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour10), "10:00 AM" },

                { m_Setting.GetEnumValueLocaleID(Setting.VotingEndHourOption.Hour16), "4:00 PM" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingEndHourOption.Hour17), "5:00 PM" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingEndHourOption.Hour18), "6:00 PM" },

                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.Baseline), "Default" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.EuropeanUnion), "European Union" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.Brazil), "Brazil" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.Canada), "Canada" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.France), "France" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.Germany), "Germany" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.UK), "United Kingdom" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.USA), "United States of America" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableDebugLogging)), "Enable Debug Logging" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableDebugLogging)), "Write detailed Elections lifecycle, campaign, poll, voting, donation, mayor, and repair events to the game log." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ForceStartCampaign)), "Force Start Campaign" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ForceStartCampaign)), "Debug action: immediately select candidates and announce the campaign." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ForceStartCampaign)), "Start a new campaign now?" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ForceElectionToday)), "Force Election Today" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ForceElectionToday)), "Debug action: begin voting today if candidates exist." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ForceElectionToday)), "Begin election voting today?" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ForceGeneratePoll)), "Generate Poll" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ForceGeneratePoll)), "Debug action: immediately generate and release poll results for the active race." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ForceGeneratePoll)), "Generate a poll now?" }
            };
        }

        public void Unload()
        {
        }
    }

    public class LocalePTBR : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocalePTBR(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Eleições" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Principal" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup), "Geral" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kVotingGroup), "Votação" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kCampaignGroup), "Campanha" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kDebugGroup), "Depuração" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableElections)), "Ativar Eleições" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableElections)), "Executa o ciclo de eleições para prefeito." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UseUniversalModMenu)), "Usar Universal Mod Menu" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UseUniversalModMenu)), "Mostra o botão de Eleições no Universal Mod Menu em vez da sobreposição superior esquerda do jogo." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.VotingSiteOverlayScalePercent)), "Tamanho da Sobreposicao dos Locais de Votacao" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.VotingSiteOverlayScalePercent)), "Tamanho do marcador do local de votacao, icone de urna e contagem de votos. 100% = escala 1.0, 120% e o padrao, e as alteracoes sao aplicadas imediatamente." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.PollSamplePercent)), "Percentual da Amostra da Pesquisa" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.PollSamplePercent)), "Parcela aproximada da população total da cidade representada na pesquisa de campanha. O padrão é 2% e o máximo é 10%." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AgeTurnoutCountryPreset)), "Pais para Comparecimento por Idade" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AgeTurnoutCountryPreset)), "Escolha uma predefinicao real de comparecimento por idade a partir de paises com dados publicados por idade, alem da Uniao Europeia." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ApplyAgeTurnoutCountryPreset)), "Aplicar Predefinicao por Idade" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ApplyAgeTurnoutCountryPreset)), "Copia os valores arredondados do pais selecionado para os controles diarios de adolescentes, adultos e idosos." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.TeenDailyVotingTurnoutPercent)), "Comparecimento Diário dos Adolescentes" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.TeenDailyVotingTurnoutPercent)), "Probabilidade diária de um adolescente elegível votar. O padrão é 36%; a simulação divide isso pelas horas de votação configuradas." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AdultDailyVotingTurnoutPercent)), "Comparecimento Diário dos Adultos" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AdultDailyVotingTurnoutPercent)), "Probabilidade diária de um adulto elegível votar. O padrão é 49%; a simulação divide isso pelas horas de votação configuradas." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ElderlyDailyVotingTurnoutPercent)), "Comparecimento Diário dos Idosos" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ElderlyDailyVotingTurnoutPercent)), "Probabilidade diária de um idoso elegível votar. O padrão é 58%; a simulação divide isso pelas horas de votação configuradas." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ElectionVotingStartHour)), "Hora de Início da Votação" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ElectionVotingStartHour)), "Horário de início da votação no dia da eleição. O padrão é 8:00." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ElectionVotingEndHour)), "Hora de Encerramento da Votação" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ElectionVotingEndHour)), "Horário de encerramento da votação no dia da eleição. O padrão é 17:00. Os resultados são anunciados às 20:00." },

                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour6), "6:00" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour7), "7:00" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour8), "8:00" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour9), "9:00" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour10), "10:00" },

                { m_Setting.GetEnumValueLocaleID(Setting.VotingEndHourOption.Hour16), "16:00" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingEndHourOption.Hour17), "17:00" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingEndHourOption.Hour18), "18:00" },

                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.Baseline), "Padrao" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.EuropeanUnion), "Uniao Europeia" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.Brazil), "Brasil" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.Canada), "Canada" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.France), "Franca" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.Germany), "Alemanha" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.UK), "Reino Unido" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.USA), "Estados Unidos da America" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableDebugLogging)), "Ativar Registro de Depuração" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableDebugLogging)), "Grava detalhes do ciclo eleitoral, campanha, pesquisa, votação, doações, prefeito e reparos no log do jogo." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ForceStartCampaign)), "Forçar Início da Campanha" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ForceStartCampaign)), "Ação de depuração: seleciona candidatos imediatamente e anuncia a campanha." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ForceStartCampaign)), "Iniciar uma nova campanha agora?" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ForceElectionToday)), "Forçar Eleição Hoje" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ForceElectionToday)), "Ação de depuração: inicia a votação hoje se houver candidatos." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ForceElectionToday)), "Iniciar a votação da eleição hoje?" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ForceGeneratePoll)), "Gerar Pesquisa" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ForceGeneratePoll)), "Ação de depuração: gera e divulga imediatamente os resultados da pesquisa para a disputa ativa." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ForceGeneratePoll)), "Gerar uma pesquisa agora?" }
            };
        }

        public void Unload()
        {
        }
    }
}
