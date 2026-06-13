using Colossal.Serialization.Entities;
using Elections.Models;
using Unity.Collections;
using Unity.Entities;

namespace Elections.Components
{
    public enum ElectionCampaignStage
    {
        None = 0,
        CandidatesSelected = 1,
        PollReleased = 2,
        Voting = 3
    }

    public struct ElectionState : IComponentData, IQueryTypeParameter, ISerializable
    {
        // Keep the state marker lower than unpublished layout churn while still mapping it to
        // the current field layout below. Known published/committed layouts include 17, 19, and 22.
        public const int CurrentVersion = 23;
        private const int CurrentSerializedLayoutVersion = 31;
        public const int MinCandidateCount = 2;
        public const int DefaultCandidateCount = 2;
        public const int MaxCandidateCount = 4;

        public int version;
        public bool initialized;
        public int lastProcessedDayKey;

        public ElectionCampaignStage stage;
        public bool acceleratedCycle;

        public int selectionYear;
        public int selectionMonth;
        public int pollYear;
        public int pollMonth;
        public int electionYear;
        public int electionMonth;
        public int mayorTermYear;
        public bool runoffEnabledForCycle;
        public bool runoffActive;
        public int runoffOriginalCandidateCount;
        public int pendingMayorCandidateIndex;
        public Entity pendingMayor;
        public int pendingMayorEffectId;
        public int pendingMayorTagId;
        public bool pendingMayorNegativeSoftened;
        public int pendingMayorPartyIndex;
        public int pendingMayorTermYear;
        public bool pendingMayorInaugurated;

        public int candidateCount;
        public FixedString64Bytes partyAName;
        public FixedString64Bytes partyBName;
        public FixedString64Bytes partyCName;
        public FixedString64Bytes partyDName;
        public int partyAColor;
        public int partyBColor;
        public int partyCColor;
        public int partyDColor;
        public int partyAReputation;
        public int partyBReputation;
        public int partyCReputation;
        public int partyDReputation;
        public int partyAConsecutiveTerms;
        public int partyBConsecutiveTerms;
        public int partyCConsecutiveTerms;
        public int partyDConsecutiveTerms;
        public int partyAWins;
        public int partyBWins;
        public int partyCWins;
        public int partyDWins;
        public int partyALastTagReplacementYear;
        public int partyBLastTagReplacementYear;
        public int partyCLastTagReplacementYear;
        public int partyDLastTagReplacementYear;
        public int partyATagId1;
        public int partyATagId2;
        public int partyATagId3;
        public int partyBTagId1;
        public int partyBTagId2;
        public int partyBTagId3;
        public int partyCTagId1;
        public int partyCTagId2;
        public int partyCTagId3;
        public int partyDTagId1;
        public int partyDTagId2;
        public int partyDTagId3;
        public Entity candidateA;
        public Entity candidateB;
        public Entity candidateC;
        public Entity candidateD;
        public int candidateAEffectId;
        public int candidateBEffectId;
        public int candidateCEffectId;
        public int candidateDEffectId;
        public int candidateAAge;
        public int candidateBAge;
        public int candidateCAge;
        public int candidateDAge;
        public int candidateAEducation;
        public int candidateBEducation;
        public int candidateCEducation;
        public int candidateDEducation;
        public int candidateAWorkType;
        public int candidateBWorkType;
        public int candidateCWorkType;
        public int candidateDWorkType;
        public int candidateAWealth;
        public int candidateBWealth;
        public int candidateCWealth;
        public int candidateDWealth;
        public int candidateAPortraitIndex;
        public int candidateBPortraitIndex;
        public int candidateCPortraitIndex;
        public int candidateDPortraitIndex;
        public int candidateATagId;
        public int candidateBTagId;
        public int candidateCTagId;
        public int candidateDTagId;
        public int candidateASupportModifierPercent;
        public int candidateBSupportModifierPercent;
        public int candidateCSupportModifierPercent;
        public int candidateDSupportModifierPercent;
        public bool candidateAPlatformChirpSent;
        public bool candidateBPlatformChirpSent;
        public bool candidateCPlatformChirpSent;
        public bool candidateDPlatformChirpSent;
        public int candidateAPlatformChirpDayKey;
        public int candidateBPlatformChirpDayKey;
        public int candidateCPlatformChirpDayKey;
        public int candidateDPlatformChirpDayKey;
        public int candidateAPlatformChirpMinute;
        public int candidateBPlatformChirpMinute;
        public int candidateCPlatformChirpMinute;
        public int candidateDPlatformChirpMinute;
        public long candidateAPlatformChirpUtcTicks;
        public long candidateBPlatformChirpUtcTicks;
        public long candidateCPlatformChirpUtcTicks;
        public long candidateDPlatformChirpUtcTicks;

        public int donationA;
        public int donationB;
        public int donationC;
        public int donationD;
        public int donationDayKey;
        public int campaignDonationAmount;
        public int campaignBribeAmount;
        public bool candidateANegativeSoftened;
        public bool candidateBNegativeSoftened;
        public bool candidateCNegativeSoftened;
        public bool candidateDNegativeSoftened;
        public bool candidateASoftenAttempted;
        public bool candidateBSoftenAttempted;
        public bool candidateCSoftenAttempted;
        public bool candidateDSoftenAttempted;
        public int pollVotesA;
        public int pollVotesB;
        public int pollVotesC;
        public int pollVotesD;
        public int pollUndecided;
        public int pollTeenVotesA;
        public int pollTeenVotesB;
        public int pollTeenVotesC;
        public int pollTeenVotesD;
        public int pollTeenUndecided;
        public int pollAdultVotesA;
        public int pollAdultVotesB;
        public int pollAdultVotesC;
        public int pollAdultVotesD;
        public int pollAdultUndecided;
        public int pollElderlyVotesA;
        public int pollElderlyVotesB;
        public int pollElderlyVotesC;
        public int pollElderlyVotesD;
        public int pollElderlyUndecided;
        public int pollEducation0VotesA;
        public int pollEducation0VotesB;
        public int pollEducation0VotesC;
        public int pollEducation0VotesD;
        public int pollEducation0Undecided;
        public int pollEducation1VotesA;
        public int pollEducation1VotesB;
        public int pollEducation1VotesC;
        public int pollEducation1VotesD;
        public int pollEducation1Undecided;
        public int pollEducation2VotesA;
        public int pollEducation2VotesB;
        public int pollEducation2VotesC;
        public int pollEducation2VotesD;
        public int pollEducation2Undecided;
        public int pollEducation3VotesA;
        public int pollEducation3VotesB;
        public int pollEducation3VotesC;
        public int pollEducation3VotesD;
        public int pollEducation3Undecided;
        public int pollEducation4VotesA;
        public int pollEducation4VotesB;
        public int pollEducation4VotesC;
        public int pollEducation4VotesD;
        public int pollEducation4Undecided;
        public int pollIncome0VotesA;
        public int pollIncome0VotesB;
        public int pollIncome0VotesC;
        public int pollIncome0VotesD;
        public int pollIncome0Undecided;
        public int pollIncome1VotesA;
        public int pollIncome1VotesB;
        public int pollIncome1VotesC;
        public int pollIncome1VotesD;
        public int pollIncome1Undecided;
        public int pollIncome2VotesA;
        public int pollIncome2VotesB;
        public int pollIncome2VotesC;
        public int pollIncome2VotesD;
        public int pollIncome2Undecided;
        public int pollIncome3VotesA;
        public int pollIncome3VotesB;
        public int pollIncome3VotesC;
        public int pollIncome3VotesD;
        public int pollIncome3Undecided;
        public int pollIncome4VotesA;
        public int pollIncome4VotesB;
        public int pollIncome4VotesC;
        public int pollIncome4VotesD;
        public int pollIncome4Undecided;
        public bool candidateAPollResponseChirpSent;
        public bool candidateBPollResponseChirpSent;
        public bool candidateCPollResponseChirpSent;
        public bool candidateDPollResponseChirpSent;
        public long candidateAPollResponseChirpUtcTicks;
        public long candidateBPollResponseChirpUtcTicks;
        public long candidateCPollResponseChirpUtcTicks;
        public long candidateDPollResponseChirpUtcTicks;
        public bool candidateAElectionReminderChirpSent;
        public bool candidateBElectionReminderChirpSent;
        public bool candidateCElectionReminderChirpSent;
        public bool candidateDElectionReminderChirpSent;
        public int candidateAElectionReminderChirpDayKey;
        public int candidateBElectionReminderChirpDayKey;
        public int candidateCElectionReminderChirpDayKey;
        public int candidateDElectionReminderChirpDayKey;
        public int candidateAElectionReminderChirpMinute;
        public int candidateBElectionReminderChirpMinute;
        public int candidateCElectionReminderChirpMinute;
        public int candidateDElectionReminderChirpMinute;

        public int electionDayKey;
        public int votingStartMinute;
        public int votingEndMinute;
        public int resultsAnnouncementMinute;
        public int voteRequests;
        public int voteArrivals;
        public int votesA;
        public int votesB;
        public int votesC;
        public int votesD;
        public bool candidateAVotedChirpSent;
        public bool candidateBVotedChirpSent;
        public bool candidateCVotedChirpSent;
        public bool candidateDVotedChirpSent;
        public bool votingClosedChirpSent;
        public Entity victoryPartyVenue;
        public int victoryPartyElectionDayKey;
        public int victoryPartyWinnerIndex;
        public bool victoryPartyTripsRequested;
        public int victoryPartyTripRequests;
        public bool victoryWinnerChirpSent;
        public bool victoryLoserChirpSent;
        public long victoryWinnerChirpUtcTicks;
        public long victoryLoserChirpUtcTicks;
        public int victoryPartyNextVoterIndex;
        public long victoryPartyNextTripBatchTicks;
        public bool victoryPartyWinnerTripRequested;

        public Entity mayor;
        public int mayorEffectId;
        public int mayorEffectTermYear;
        public bool mayorNegativeSoftened;
        public bool mayorMoneyApplied;
        public Entity mayorHome;
        public Entity mayorWorkplace;
        public int mayorTagId;
        public int mayorPartyIndex;

        public int appliedEffectId;
        public bool appliedNegativeSoftened;
        public int appliedModifierType1;
        public float appliedModifierAdd1;
        public float appliedModifierMul1;
        public int appliedModifierType2;
        public float appliedModifierAdd2;
        public float appliedModifierMul2;
        public int appliedEffectTagId;
        public int appliedEffectPartySignature;

        public int bribeDayKey;
        public long corruptionInvestigationChirpUtcTicks;
        public Entity corruptionInvestigationMayor;
        public long bribeBlockedUntilTicks;
        public int bribeMeetingCandidateIndex;
        public Entity bribeMeetingCandidate;
        public Entity bribeMeetingVenue;
        public long bribeMeetingDeadlineTicks;
        public long bribeMeetingNextAttemptTicks;
        public bool bribeMeetingTripsRequested;
        public int mayorEndorsementCandidateIndex;
        public Entity mayorEndorsementCandidate;
        public long mayorEndorsementChirpUtcTicks;
        public bool mayorEndorsementChirpSent;
        public int voteTamperingCandidateIndex;
        public Entity voteTamperingCandidate;
        public int voteTamperingScheduledMinute;
        public bool voteTamperingFireStarted;
        public bool voteTamperingResolved;
        public Entity voteTamperingPollingPlace;
        public int voteTamperingLostVotesA;
        public int voteTamperingLostVotesB;
        public int voteTamperingLostVotesC;
        public int voteTamperingLostVotesD;
        public long voteTamperingProtestChirpUtcTicks;
        public bool voteTamperingProtestChirpSent;
        public int candidateACorruptionRiskSteps;
        public int candidateBCorruptionRiskSteps;
        public int candidateCCorruptionRiskSteps;
        public int candidateDCorruptionRiskSteps;
        public bool corruptionArrestCheckCompleted;
        public Entity mayorBribeRecipient;
        public int mayorBribeTotal;
        public Entity outgoingMayor;
        public int outgoingMayorBribeTotal;
        public int outgoingMayorTagId;
        public int outgoingMayorPartyIndex;
        public bool electionDayHolidayScheduled;
        public int supportProgramDayKey;
        public int supportProgramIdToday;
        public int teenTurnoutBonusPercent;
        public int adultTurnoutBonusPercent;
        public int elderlyTurnoutBonusPercent;
        public int uneducatedTurnoutBonusPercent;
        public int educatedTurnoutBonusPercent;
        public int lowIncomeTurnoutBonusPercent;
        public int transitVoucherTurnoutBonusPercent;
        public int civicForumTurnoutBonusPercent;
        public int supportProgramBalanceVersion;
        public bool strictVotingIdLawPassed;
        public bool strictVotingIdProposalPending;
        public bool strictVotingIdProposalPassed;
        public long strictVotingIdChirpUtcTicks;
        public bool strictVotingIdChirpSent;
        public int legislationFlags;
        public int legislationActionDayKey;
        public int partyReputationEventDayKey;
        public int partyMilestoneBonusYear;
        public int trackedMilestoneLevel;
        public int cashAssistanceTurnoutBonusPercent;

        public int ActiveCandidateCount
        {
            get
            {
                if (candidateA == Entity.Null || candidateB == Entity.Null)
                    return 0;

                int count = NormalizeCandidateCount(candidateCount <= 0 ? DefaultCandidateCount : candidateCount);
                for (int i = 0; i < count; i++)
                {
                    if (GetCandidate(i) == Entity.Null)
                        return 0;
                }

                return count;
            }
        }

        public bool HasCandidates => ActiveCandidateCount >= MinCandidateCount;

        public static int NormalizeCandidateCount(int value)
        {
            if (value < MinCandidateCount)
                return MinCandidateCount;

            return value > MaxCandidateCount ? MaxCandidateCount : value;
        }

        public static bool IsCandidateIndex(int index)
        {
            return index >= 0 && index < MaxCandidateCount;
        }

        public bool IsActiveCandidateIndex(int index)
        {
            return index >= 0 && index < ActiveCandidateCount;
        }

        public static string GetCandidateFallbackName(int index)
        {
            switch (index)
            {
                case 0:
                    return "Candidate A";
                case 1:
                    return "Candidate B";
                case 2:
                    return "Candidate C";
                case 3:
                    return "Candidate D";
                default:
                    return "Candidate";
            }
        }

        public static bool IsPartyIndex(int index)
        {
            return IsCandidateIndex(index);
        }

        public int GetCandidatePartyIndex(int candidateIndex)
        {
            return IsCandidateIndex(candidateIndex) ? candidateIndex : -1;
        }

        public string GetPartyName(int index)
        {
            string name;
            switch (index)
            {
                case 0:
                    name = partyAName.ToString();
                    break;
                case 1:
                    name = partyBName.ToString();
                    break;
                case 2:
                    name = partyCName.ToString();
                    break;
                case 3:
                    name = partyDName.ToString();
                    break;
                default:
                    return "Party";
            }

            return string.IsNullOrWhiteSpace(name) ? ElectionPartyTags.GetDefaultName(index) : name;
        }

        public void SetPartyName(int index, string value)
        {
            value = value ?? string.Empty;
            switch (index)
            {
                case 0:
                    partyAName = value;
                    break;
                case 1:
                    partyBName = value;
                    break;
                case 2:
                    partyCName = value;
                    break;
                case 3:
                    partyDName = value;
                    break;
            }
        }

        public int GetPartyColor(int index)
        {
            switch (index)
            {
                case 0:
                    return partyAColor;
                case 1:
                    return partyBColor;
                case 2:
                    return partyCColor;
                case 3:
                    return partyDColor;
                default:
                    return 0;
            }
        }

        public void SetPartyColor(int index, int value)
        {
            value &= 0xffffff;
            switch (index)
            {
                case 0:
                    partyAColor = value;
                    break;
                case 1:
                    partyBColor = value;
                    break;
                case 2:
                    partyCColor = value;
                    break;
                case 3:
                    partyDColor = value;
                    break;
            }
        }

        public int GetPartyReputation(int index)
        {
            switch (index)
            {
                case 0:
                    return partyAReputation;
                case 1:
                    return partyBReputation;
                case 2:
                    return partyCReputation;
                case 3:
                    return partyDReputation;
                default:
                    return ElectionPartyTags.DefaultReputation;
            }
        }

        public void SetPartyReputation(int index, int value)
        {
            value = ElectionPartyTags.ClampReputation(value);
            switch (index)
            {
                case 0:
                    partyAReputation = value;
                    break;
                case 1:
                    partyBReputation = value;
                    break;
                case 2:
                    partyCReputation = value;
                    break;
                case 3:
                    partyDReputation = value;
                    break;
            }
        }

        public void AddPartyReputation(int index, int delta)
        {
            SetPartyReputation(index, GetPartyReputation(index) + delta);
        }

        public int GetPartyConsecutiveTerms(int index)
        {
            switch (index)
            {
                case 0:
                    return partyAConsecutiveTerms;
                case 1:
                    return partyBConsecutiveTerms;
                case 2:
                    return partyCConsecutiveTerms;
                case 3:
                    return partyDConsecutiveTerms;
                default:
                    return 0;
            }
        }

        public void SetPartyConsecutiveTerms(int index, int value)
        {
            value = value < 0 ? 0 : value;
            switch (index)
            {
                case 0:
                    partyAConsecutiveTerms = value;
                    break;
                case 1:
                    partyBConsecutiveTerms = value;
                    break;
                case 2:
                    partyCConsecutiveTerms = value;
                    break;
                case 3:
                    partyDConsecutiveTerms = value;
                    break;
            }
        }

        public int GetPartyWins(int index)
        {
            switch (index)
            {
                case 0:
                    return partyAWins;
                case 1:
                    return partyBWins;
                case 2:
                    return partyCWins;
                case 3:
                    return partyDWins;
                default:
                    return 0;
            }
        }

        public void SetPartyWins(int index, int value)
        {
            value = value < 0 ? 0 : value;
            switch (index)
            {
                case 0:
                    partyAWins = value;
                    break;
                case 1:
                    partyBWins = value;
                    break;
                case 2:
                    partyCWins = value;
                    break;
                case 3:
                    partyDWins = value;
                    break;
            }
        }

        public int GetPartyLastTagReplacementYear(int index)
        {
            switch (index)
            {
                case 0:
                    return partyALastTagReplacementYear;
                case 1:
                    return partyBLastTagReplacementYear;
                case 2:
                    return partyCLastTagReplacementYear;
                case 3:
                    return partyDLastTagReplacementYear;
                default:
                    return 0;
            }
        }

        public void SetPartyLastTagReplacementYear(int index, int value)
        {
            switch (index)
            {
                case 0:
                    partyALastTagReplacementYear = value;
                    break;
                case 1:
                    partyBLastTagReplacementYear = value;
                    break;
                case 2:
                    partyCLastTagReplacementYear = value;
                    break;
                case 3:
                    partyDLastTagReplacementYear = value;
                    break;
            }
        }

        public int GetPartyTagId(int partyIndex, int slotIndex)
        {
            switch (partyIndex)
            {
                case 0:
                    return slotIndex == 0 ? partyATagId1 : slotIndex == 1 ? partyATagId2 : partyATagId3;
                case 1:
                    return slotIndex == 0 ? partyBTagId1 : slotIndex == 1 ? partyBTagId2 : partyBTagId3;
                case 2:
                    return slotIndex == 0 ? partyCTagId1 : slotIndex == 1 ? partyCTagId2 : partyCTagId3;
                case 3:
                    return slotIndex == 0 ? partyDTagId1 : slotIndex == 1 ? partyDTagId2 : partyDTagId3;
                default:
                    return ElectionPartyTags.None;
            }
        }

        public void SetPartyTagId(int partyIndex, int slotIndex, int value)
        {
            value = ElectionPartyTags.NormalizeId(value);
            switch (partyIndex)
            {
                case 0:
                    if (slotIndex == 0)
                        partyATagId1 = value;
                    else if (slotIndex == 1)
                        partyATagId2 = value;
                    else
                        partyATagId3 = value;
                    break;
                case 1:
                    if (slotIndex == 0)
                        partyBTagId1 = value;
                    else if (slotIndex == 1)
                        partyBTagId2 = value;
                    else
                        partyBTagId3 = value;
                    break;
                case 2:
                    if (slotIndex == 0)
                        partyCTagId1 = value;
                    else if (slotIndex == 1)
                        partyCTagId2 = value;
                    else
                        partyCTagId3 = value;
                    break;
                case 3:
                    if (slotIndex == 0)
                        partyDTagId1 = value;
                    else if (slotIndex == 1)
                        partyDTagId2 = value;
                    else
                        partyDTagId3 = value;
                    break;
            }
        }

        public int GetPartyPlatformSignature(int partyIndex)
        {
            unchecked
            {
                int signature = 17;
                signature = signature * 31 + partyIndex;
                signature = signature * 31 + ElectionPartyTags.NormalizeId(GetPartyTagId(partyIndex, 0));
                signature = signature * 31 + ElectionPartyTags.NormalizeId(GetPartyTagId(partyIndex, 1));
                signature = signature * 31 + ElectionPartyTags.NormalizeId(GetPartyTagId(partyIndex, 2));
                return signature;
            }
        }

        public bool HasLegislation(ElectionLegislationType type)
        {
            return ElectionLegislation.IsActive(legislationFlags, type);
        }

        public void SetLegislation(ElectionLegislationType type, bool active)
        {
            legislationFlags = ElectionLegislation.SetActive(legislationFlags, type, active);
            if (type == ElectionLegislationType.VoterIdentification)
                strictVotingIdLawPassed = active;
        }

        public Entity GetCandidate(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateA;
                case 1:
                    return candidateB;
                case 2:
                    return candidateC;
                case 3:
                    return candidateD;
                default:
                    return Entity.Null;
            }
        }

        public void SetCandidate(int index, Entity value)
        {
            switch (index)
            {
                case 0:
                    candidateA = value;
                    break;
                case 1:
                    candidateB = value;
                    break;
                case 2:
                    candidateC = value;
                    break;
                case 3:
                    candidateD = value;
                    break;
            }
        }

        public int GetCandidateEffectId(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateAEffectId;
                case 1:
                    return candidateBEffectId;
                case 2:
                    return candidateCEffectId;
                case 3:
                    return candidateDEffectId;
                default:
                    return 0;
            }
        }

        public void SetCandidateEffectId(int index, int value)
        {
            switch (index)
            {
                case 0:
                    candidateAEffectId = value;
                    break;
                case 1:
                    candidateBEffectId = value;
                    break;
                case 2:
                    candidateCEffectId = value;
                    break;
                case 3:
                    candidateDEffectId = value;
                    break;
            }
        }

        public int GetCandidateAge(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateAAge;
                case 1:
                    return candidateBAge;
                case 2:
                    return candidateCAge;
                case 3:
                    return candidateDAge;
                default:
                    return 0;
            }
        }

        public int GetCandidateEducation(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateAEducation;
                case 1:
                    return candidateBEducation;
                case 2:
                    return candidateCEducation;
                case 3:
                    return candidateDEducation;
                default:
                    return 0;
            }
        }

        public int GetCandidateWorkType(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateAWorkType;
                case 1:
                    return candidateBWorkType;
                case 2:
                    return candidateCWorkType;
                case 3:
                    return candidateDWorkType;
                default:
                    return 0;
            }
        }

        public int GetCandidateWealth(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateAWealth;
                case 1:
                    return candidateBWealth;
                case 2:
                    return candidateCWealth;
                case 3:
                    return candidateDWealth;
                default:
                    return 0;
            }
        }

        public void SetCandidateProfile(int index, int age, int education, int workType, int wealth)
        {
            switch (index)
            {
                case 0:
                    candidateAAge = age;
                    candidateAEducation = education;
                    candidateAWorkType = workType;
                    candidateAWealth = wealth;
                    break;
                case 1:
                    candidateBAge = age;
                    candidateBEducation = education;
                    candidateBWorkType = workType;
                    candidateBWealth = wealth;
                    break;
                case 2:
                    candidateCAge = age;
                    candidateCEducation = education;
                    candidateCWorkType = workType;
                    candidateCWealth = wealth;
                    break;
                case 3:
                    candidateDAge = age;
                    candidateDEducation = education;
                    candidateDWorkType = workType;
                    candidateDWealth = wealth;
                    break;
            }
        }

        public int GetCandidatePortraitIndex(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateAPortraitIndex;
                case 1:
                    return candidateBPortraitIndex;
                case 2:
                    return candidateCPortraitIndex;
                case 3:
                    return candidateDPortraitIndex;
                default:
                    return -1;
            }
        }

        public void SetCandidatePortraitIndex(int index, int value)
        {
            switch (index)
            {
                case 0:
                    candidateAPortraitIndex = value;
                    break;
                case 1:
                    candidateBPortraitIndex = value;
                    break;
                case 2:
                    candidateCPortraitIndex = value;
                    break;
                case 3:
                    candidateDPortraitIndex = value;
                    break;
            }
        }

        public int GetCandidateTagId(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateATagId;
                case 1:
                    return candidateBTagId;
                case 2:
                    return candidateCTagId;
                case 3:
                    return candidateDTagId;
                default:
                    return 0;
            }
        }

        public void SetCandidateTagId(int index, int value)
        {
            switch (index)
            {
                case 0:
                    candidateATagId = value;
                    break;
                case 1:
                    candidateBTagId = value;
                    break;
                case 2:
                    candidateCTagId = value;
                    break;
                case 3:
                    candidateDTagId = value;
                    break;
            }
        }

        public int GetCandidateSupportModifierPercent(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateASupportModifierPercent;
                case 1:
                    return candidateBSupportModifierPercent;
                case 2:
                    return candidateCSupportModifierPercent;
                case 3:
                    return candidateDSupportModifierPercent;
                default:
                    return 0;
            }
        }

        public void SetCandidateSupportModifierPercent(int index, int value)
        {
            value = ClampCandidateSupportModifierPercent(value);
            switch (index)
            {
                case 0:
                    candidateASupportModifierPercent = value;
                    break;
                case 1:
                    candidateBSupportModifierPercent = value;
                    break;
                case 2:
                    candidateCSupportModifierPercent = value;
                    break;
                case 3:
                    candidateDSupportModifierPercent = value;
                    break;
            }
        }

        public static int ClampCandidateSupportModifierPercent(int value)
        {
            if (value < -40)
                return -40;
            if (value > 40)
                return 40;
            return value;
        }

        public bool GetCandidateNegativeSoftened(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateANegativeSoftened;
                case 1:
                    return candidateBNegativeSoftened;
                case 2:
                    return candidateCNegativeSoftened;
                case 3:
                    return candidateDNegativeSoftened;
                default:
                    return false;
            }
        }

        public void SetCandidateNegativeSoftened(int index, bool value)
        {
            switch (index)
            {
                case 0:
                    candidateANegativeSoftened = value;
                    break;
                case 1:
                    candidateBNegativeSoftened = value;
                    break;
                case 2:
                    candidateCNegativeSoftened = value;
                    break;
                case 3:
                    candidateDNegativeSoftened = value;
                    break;
            }
        }

        public bool GetCandidateSoftenAttempted(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateASoftenAttempted;
                case 1:
                    return candidateBSoftenAttempted;
                case 2:
                    return candidateCSoftenAttempted;
                case 3:
                    return candidateDSoftenAttempted;
                default:
                    return false;
            }
        }

        public void SetCandidateSoftenAttempted(int index, bool value)
        {
            switch (index)
            {
                case 0:
                    candidateASoftenAttempted = value;
                    break;
                case 1:
                    candidateBSoftenAttempted = value;
                    break;
                case 2:
                    candidateCSoftenAttempted = value;
                    break;
                case 3:
                    candidateDSoftenAttempted = value;
                    break;
            }
        }

        public int GetCandidateDonation(int index)
        {
            switch (index)
            {
                case 0:
                    return donationA;
                case 1:
                    return donationB;
                case 2:
                    return donationC;
                case 3:
                    return donationD;
                default:
                    return 0;
            }
        }

        public void SetCandidateDonation(int index, int value)
        {
            switch (index)
            {
                case 0:
                    donationA = value;
                    break;
                case 1:
                    donationB = value;
                    break;
                case 2:
                    donationC = value;
                    break;
                case 3:
                    donationD = value;
                    break;
            }
        }

        public void AddCandidateDonation(int index, int amount)
        {
            SetCandidateDonation(index, GetCandidateDonation(index) + amount);
        }

        public int GetCandidateVotes(int index)
        {
            switch (index)
            {
                case 0:
                    return votesA;
                case 1:
                    return votesB;
                case 2:
                    return votesC;
                case 3:
                    return votesD;
                default:
                    return 0;
            }
        }

        public void SetCandidateVotes(int index, int value)
        {
            switch (index)
            {
                case 0:
                    votesA = value;
                    break;
                case 1:
                    votesB = value;
                    break;
                case 2:
                    votesC = value;
                    break;
                case 3:
                    votesD = value;
                    break;
            }
        }

        public void AddCandidateVote(int index)
        {
            SetCandidateVotes(index, GetCandidateVotes(index) + 1);
        }

        public void SubtractCandidateVotes(int index, int amount)
        {
            if (amount <= 0)
                return;

            int votes = GetCandidateVotes(index) - amount;
            SetCandidateVotes(index, votes < 0 ? 0 : votes);
        }

        public int GetCandidatePollVotes(int index)
        {
            switch (index)
            {
                case 0:
                    return pollVotesA;
                case 1:
                    return pollVotesB;
                case 2:
                    return pollVotesC;
                case 3:
                    return pollVotesD;
                default:
                    return 0;
            }
        }

        public void SetCandidatePollVotes(int index, int value)
        {
            switch (index)
            {
                case 0:
                    pollVotesA = value;
                    break;
                case 1:
                    pollVotesB = value;
                    break;
                case 2:
                    pollVotesC = value;
                    break;
                case 3:
                    pollVotesD = value;
                    break;
            }
        }

        public void AddCandidatePollVote(int index)
        {
            SetCandidatePollVotes(index, GetCandidatePollVotes(index) + 1);
        }

        public int GetVoteTamperingLostVotes(int index)
        {
            switch (index)
            {
                case 0:
                    return voteTamperingLostVotesA;
                case 1:
                    return voteTamperingLostVotesB;
                case 2:
                    return voteTamperingLostVotesC;
                case 3:
                    return voteTamperingLostVotesD;
                default:
                    return 0;
            }
        }

        public void AddVoteTamperingLostVotes(int index, int amount)
        {
            switch (index)
            {
                case 0:
                    voteTamperingLostVotesA += amount;
                    break;
                case 1:
                    voteTamperingLostVotesB += amount;
                    break;
                case 2:
                    voteTamperingLostVotesC += amount;
                    break;
                case 3:
                    voteTamperingLostVotesD += amount;
                    break;
            }
        }

        public int GetCandidateCorruptionRiskSteps(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateACorruptionRiskSteps;
                case 1:
                    return candidateBCorruptionRiskSteps;
                case 2:
                    return candidateCCorruptionRiskSteps;
                case 3:
                    return candidateDCorruptionRiskSteps;
                default:
                    return 0;
            }
        }

        public void SetCandidateCorruptionRiskSteps(int index, int value)
        {
            switch (index)
            {
                case 0:
                    candidateACorruptionRiskSteps = value;
                    break;
                case 1:
                    candidateBCorruptionRiskSteps = value;
                    break;
                case 2:
                    candidateCCorruptionRiskSteps = value;
                    break;
                case 3:
                    candidateDCorruptionRiskSteps = value;
                    break;
            }
        }

        public bool GetCandidatePlatformChirpSent(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateAPlatformChirpSent;
                case 1:
                    return candidateBPlatformChirpSent;
                case 2:
                    return candidateCPlatformChirpSent;
                case 3:
                    return candidateDPlatformChirpSent;
                default:
                    return true;
            }
        }

        public void SetCandidatePlatformChirpSent(int index, bool value)
        {
            switch (index)
            {
                case 0:
                    candidateAPlatformChirpSent = value;
                    break;
                case 1:
                    candidateBPlatformChirpSent = value;
                    break;
                case 2:
                    candidateCPlatformChirpSent = value;
                    break;
                case 3:
                    candidateDPlatformChirpSent = value;
                    break;
            }
        }

        public long GetCandidatePlatformChirpUtcTicks(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateAPlatformChirpUtcTicks;
                case 1:
                    return candidateBPlatformChirpUtcTicks;
                case 2:
                    return candidateCPlatformChirpUtcTicks;
                case 3:
                    return candidateDPlatformChirpUtcTicks;
                default:
                    return 0;
            }
        }

        public void SetCandidatePlatformChirpUtcTicks(int index, long value)
        {
            switch (index)
            {
                case 0:
                    candidateAPlatformChirpUtcTicks = value;
                    break;
                case 1:
                    candidateBPlatformChirpUtcTicks = value;
                    break;
                case 2:
                    candidateCPlatformChirpUtcTicks = value;
                    break;
                case 3:
                    candidateDPlatformChirpUtcTicks = value;
                    break;
            }
        }

        public int GetCandidatePlatformChirpDayKey(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateAPlatformChirpDayKey;
                case 1:
                    return candidateBPlatformChirpDayKey;
                case 2:
                    return candidateCPlatformChirpDayKey;
                case 3:
                    return candidateDPlatformChirpDayKey;
                default:
                    return 0;
            }
        }

        public void SetCandidatePlatformChirpDayKey(int index, int value)
        {
            switch (index)
            {
                case 0:
                    candidateAPlatformChirpDayKey = value;
                    break;
                case 1:
                    candidateBPlatformChirpDayKey = value;
                    break;
                case 2:
                    candidateCPlatformChirpDayKey = value;
                    break;
                case 3:
                    candidateDPlatformChirpDayKey = value;
                    break;
            }
        }

        public int GetCandidatePlatformChirpMinute(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateAPlatformChirpMinute;
                case 1:
                    return candidateBPlatformChirpMinute;
                case 2:
                    return candidateCPlatformChirpMinute;
                case 3:
                    return candidateDPlatformChirpMinute;
                default:
                    return 0;
            }
        }

        public void SetCandidatePlatformChirpMinute(int index, int value)
        {
            switch (index)
            {
                case 0:
                    candidateAPlatformChirpMinute = value;
                    break;
                case 1:
                    candidateBPlatformChirpMinute = value;
                    break;
                case 2:
                    candidateCPlatformChirpMinute = value;
                    break;
                case 3:
                    candidateDPlatformChirpMinute = value;
                    break;
            }
        }

        public bool GetCandidatePollResponseChirpSent(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateAPollResponseChirpSent;
                case 1:
                    return candidateBPollResponseChirpSent;
                case 2:
                    return candidateCPollResponseChirpSent;
                case 3:
                    return candidateDPollResponseChirpSent;
                default:
                    return true;
            }
        }

        public void SetCandidatePollResponseChirpSent(int index, bool value)
        {
            switch (index)
            {
                case 0:
                    candidateAPollResponseChirpSent = value;
                    break;
                case 1:
                    candidateBPollResponseChirpSent = value;
                    break;
                case 2:
                    candidateCPollResponseChirpSent = value;
                    break;
                case 3:
                    candidateDPollResponseChirpSent = value;
                    break;
            }
        }

        public long GetCandidatePollResponseChirpUtcTicks(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateAPollResponseChirpUtcTicks;
                case 1:
                    return candidateBPollResponseChirpUtcTicks;
                case 2:
                    return candidateCPollResponseChirpUtcTicks;
                case 3:
                    return candidateDPollResponseChirpUtcTicks;
                default:
                    return 0;
            }
        }

        public void SetCandidatePollResponseChirpUtcTicks(int index, long value)
        {
            switch (index)
            {
                case 0:
                    candidateAPollResponseChirpUtcTicks = value;
                    break;
                case 1:
                    candidateBPollResponseChirpUtcTicks = value;
                    break;
                case 2:
                    candidateCPollResponseChirpUtcTicks = value;
                    break;
                case 3:
                    candidateDPollResponseChirpUtcTicks = value;
                    break;
            }
        }

        public bool GetCandidateElectionReminderChirpSent(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateAElectionReminderChirpSent;
                case 1:
                    return candidateBElectionReminderChirpSent;
                case 2:
                    return candidateCElectionReminderChirpSent;
                case 3:
                    return candidateDElectionReminderChirpSent;
                default:
                    return true;
            }
        }

        public void SetCandidateElectionReminderChirpSent(int index, bool value)
        {
            switch (index)
            {
                case 0:
                    candidateAElectionReminderChirpSent = value;
                    break;
                case 1:
                    candidateBElectionReminderChirpSent = value;
                    break;
                case 2:
                    candidateCElectionReminderChirpSent = value;
                    break;
                case 3:
                    candidateDElectionReminderChirpSent = value;
                    break;
            }
        }

        public int GetCandidateElectionReminderChirpDayKey(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateAElectionReminderChirpDayKey;
                case 1:
                    return candidateBElectionReminderChirpDayKey;
                case 2:
                    return candidateCElectionReminderChirpDayKey;
                case 3:
                    return candidateDElectionReminderChirpDayKey;
                default:
                    return 0;
            }
        }

        public void SetCandidateElectionReminderChirpDayKey(int index, int value)
        {
            switch (index)
            {
                case 0:
                    candidateAElectionReminderChirpDayKey = value;
                    break;
                case 1:
                    candidateBElectionReminderChirpDayKey = value;
                    break;
                case 2:
                    candidateCElectionReminderChirpDayKey = value;
                    break;
                case 3:
                    candidateDElectionReminderChirpDayKey = value;
                    break;
            }
        }

        public int GetCandidateElectionReminderChirpMinute(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateAElectionReminderChirpMinute;
                case 1:
                    return candidateBElectionReminderChirpMinute;
                case 2:
                    return candidateCElectionReminderChirpMinute;
                case 3:
                    return candidateDElectionReminderChirpMinute;
                default:
                    return 0;
            }
        }

        public void SetCandidateElectionReminderChirpMinute(int index, int value)
        {
            switch (index)
            {
                case 0:
                    candidateAElectionReminderChirpMinute = value;
                    break;
                case 1:
                    candidateBElectionReminderChirpMinute = value;
                    break;
                case 2:
                    candidateCElectionReminderChirpMinute = value;
                    break;
                case 3:
                    candidateDElectionReminderChirpMinute = value;
                    break;
            }
        }

        public bool GetCandidateVotedChirpSent(int index)
        {
            switch (index)
            {
                case 0:
                    return candidateAVotedChirpSent;
                case 1:
                    return candidateBVotedChirpSent;
                case 2:
                    return candidateCVotedChirpSent;
                case 3:
                    return candidateDVotedChirpSent;
                default:
                    return true;
            }
        }

        public void SetCandidateVotedChirpSent(int index, bool value)
        {
            switch (index)
            {
                case 0:
                    candidateAVotedChirpSent = value;
                    break;
                case 1:
                    candidateBVotedChirpSent = value;
                    break;
                case 2:
                    candidateCVotedChirpSent = value;
                    break;
                case 3:
                    candidateDVotedChirpSent = value;
                    break;
            }
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(initialized);
            writer.Write(lastProcessedDayKey);
            writer.Write((int)stage);
            writer.Write(acceleratedCycle);
            writer.Write(selectionYear);
            writer.Write(selectionMonth);
            writer.Write(pollYear);
            writer.Write(pollMonth);
            writer.Write(electionYear);
            writer.Write(electionMonth);
            writer.Write(mayorTermYear);
            writer.Write(candidateA);
            writer.Write(candidateB);
            writer.Write(candidateAEffectId);
            writer.Write(candidateBEffectId);
            writer.Write(candidateAAge);
            writer.Write(candidateBAge);
            writer.Write(candidateAEducation);
            writer.Write(candidateBEducation);
            writer.Write(candidateAWorkType);
            writer.Write(candidateBWorkType);
            writer.Write(candidateAWealth);
            writer.Write(candidateBWealth);
            writer.Write(candidateAPortraitIndex);
            writer.Write(candidateBPortraitIndex);
            writer.Write(candidateAPlatformChirpSent);
            writer.Write(candidateBPlatformChirpSent);
            writer.Write(candidateAPlatformChirpDayKey);
            writer.Write(candidateBPlatformChirpDayKey);
            writer.Write(candidateAPlatformChirpMinute);
            writer.Write(candidateBPlatformChirpMinute);
            writer.Write(candidateAPlatformChirpUtcTicks);
            writer.Write(candidateBPlatformChirpUtcTicks);
            writer.Write(donationA);
            writer.Write(donationB);
            writer.Write(campaignDonationAmount);
            writer.Write(campaignBribeAmount);
            writer.Write(pollVotesA);
            writer.Write(pollVotesB);
            writer.Write(pollUndecided);
            writer.Write(pollTeenVotesA);
            writer.Write(pollTeenVotesB);
            writer.Write(pollTeenUndecided);
            writer.Write(pollAdultVotesA);
            writer.Write(pollAdultVotesB);
            writer.Write(pollAdultUndecided);
            writer.Write(pollElderlyVotesA);
            writer.Write(pollElderlyVotesB);
            writer.Write(pollElderlyUndecided);
            writer.Write(pollEducation0VotesA);
            writer.Write(pollEducation0VotesB);
            writer.Write(pollEducation0Undecided);
            writer.Write(pollEducation1VotesA);
            writer.Write(pollEducation1VotesB);
            writer.Write(pollEducation1Undecided);
            writer.Write(pollEducation2VotesA);
            writer.Write(pollEducation2VotesB);
            writer.Write(pollEducation2Undecided);
            writer.Write(pollEducation3VotesA);
            writer.Write(pollEducation3VotesB);
            writer.Write(pollEducation3Undecided);
            writer.Write(pollEducation4VotesA);
            writer.Write(pollEducation4VotesB);
            writer.Write(pollEducation4Undecided);
            writer.Write(pollIncome0VotesA);
            writer.Write(pollIncome0VotesB);
            writer.Write(pollIncome0Undecided);
            writer.Write(pollIncome1VotesA);
            writer.Write(pollIncome1VotesB);
            writer.Write(pollIncome1Undecided);
            writer.Write(pollIncome2VotesA);
            writer.Write(pollIncome2VotesB);
            writer.Write(pollIncome2Undecided);
            writer.Write(pollIncome3VotesA);
            writer.Write(pollIncome3VotesB);
            writer.Write(pollIncome3Undecided);
            writer.Write(pollIncome4VotesA);
            writer.Write(pollIncome4VotesB);
            writer.Write(pollIncome4Undecided);
            writer.Write(candidateAPollResponseChirpSent);
            writer.Write(candidateBPollResponseChirpSent);
            writer.Write(candidateAPollResponseChirpUtcTicks);
            writer.Write(candidateBPollResponseChirpUtcTicks);
            writer.Write(candidateAElectionReminderChirpSent);
            writer.Write(candidateBElectionReminderChirpSent);
            writer.Write(candidateAElectionReminderChirpDayKey);
            writer.Write(candidateBElectionReminderChirpDayKey);
            writer.Write(candidateAElectionReminderChirpMinute);
            writer.Write(candidateBElectionReminderChirpMinute);
            writer.Write(electionDayKey);
            writer.Write(votingStartMinute);
            writer.Write(votingEndMinute);
            writer.Write(resultsAnnouncementMinute);
            writer.Write(voteRequests);
            writer.Write(voteArrivals);
            writer.Write(votesA);
            writer.Write(votesB);
            writer.Write(candidateAVotedChirpSent);
            writer.Write(candidateBVotedChirpSent);
            writer.Write(votingClosedChirpSent);
            writer.Write(victoryPartyVenue);
            writer.Write(victoryPartyElectionDayKey);
            writer.Write(victoryPartyWinnerIndex);
            writer.Write(victoryPartyTripsRequested);
            writer.Write(victoryPartyTripRequests);
            writer.Write(victoryWinnerChirpSent);
            writer.Write(victoryLoserChirpSent);
            writer.Write(victoryWinnerChirpUtcTicks);
            writer.Write(victoryLoserChirpUtcTicks);
            writer.Write(victoryPartyNextVoterIndex);
            writer.Write(victoryPartyNextTripBatchTicks);
            writer.Write(victoryPartyWinnerTripRequested);
            writer.Write(mayor);
            writer.Write(mayorEffectId);
            writer.Write(mayorEffectTermYear);
            writer.Write(mayorMoneyApplied);
            writer.Write(appliedEffectId);
            writer.Write(appliedModifierType1);
            writer.Write(appliedModifierAdd1);
            writer.Write(appliedModifierMul1);
            writer.Write(appliedModifierType2);
            writer.Write(appliedModifierAdd2);
            writer.Write(appliedModifierMul2);
            writer.Write(candidateANegativeSoftened);
            writer.Write(candidateBNegativeSoftened);
            writer.Write(candidateASoftenAttempted);
            writer.Write(candidateBSoftenAttempted);
            writer.Write(mayorNegativeSoftened);
            writer.Write(appliedNegativeSoftened);
            writer.Write(bribeDayKey);
            writer.Write(corruptionInvestigationChirpUtcTicks);
            writer.Write(corruptionInvestigationMayor);
            writer.Write(bribeBlockedUntilTicks);
            writer.Write(bribeMeetingCandidateIndex);
            writer.Write(bribeMeetingCandidate);
            writer.Write(bribeMeetingVenue);
            writer.Write(bribeMeetingDeadlineTicks);
            writer.Write(bribeMeetingNextAttemptTicks);
            writer.Write(bribeMeetingTripsRequested);
            writer.Write(mayorEndorsementCandidateIndex);
            writer.Write(mayorEndorsementCandidate);
            writer.Write(mayorEndorsementChirpUtcTicks);
            writer.Write(mayorEndorsementChirpSent);
            writer.Write(voteTamperingCandidateIndex);
            writer.Write(voteTamperingCandidate);
            writer.Write(voteTamperingScheduledMinute);
            writer.Write(voteTamperingFireStarted);
            writer.Write(voteTamperingResolved);
            writer.Write(voteTamperingPollingPlace);
            writer.Write(voteTamperingLostVotesA);
            writer.Write(voteTamperingLostVotesB);
            writer.Write(voteTamperingProtestChirpUtcTicks);
            writer.Write(voteTamperingProtestChirpSent);
            writer.Write(candidateACorruptionRiskSteps);
            writer.Write(candidateBCorruptionRiskSteps);
            writer.Write(corruptionArrestCheckCompleted);
            writer.Write(mayorBribeRecipient);
            writer.Write(mayorBribeTotal);
            writer.Write(outgoingMayor);
            writer.Write(outgoingMayorBribeTotal);
            writer.Write(electionDayHolidayScheduled);
            writer.Write(supportProgramDayKey);
            writer.Write(supportProgramIdToday);
            writer.Write(teenTurnoutBonusPercent);
            writer.Write(adultTurnoutBonusPercent);
            writer.Write(elderlyTurnoutBonusPercent);
            writer.Write(uneducatedTurnoutBonusPercent);
            writer.Write(educatedTurnoutBonusPercent);
            writer.Write(strictVotingIdLawPassed);
            writer.Write(strictVotingIdProposalPending);
            writer.Write(strictVotingIdProposalPassed);
            writer.Write(strictVotingIdChirpUtcTicks);
            writer.Write(strictVotingIdChirpSent);
            writer.Write(supportProgramBalanceVersion);
            writer.Write(mayorHome);
            writer.Write(mayorWorkplace);
            writer.Write(candidateATagId);
            writer.Write(candidateBTagId);
            writer.Write(mayorTagId);
            writer.Write(outgoingMayorTagId);
            writer.Write(appliedEffectTagId);
            writer.Write(lowIncomeTurnoutBonusPercent);
            writer.Write(transitVoucherTurnoutBonusPercent);
            writer.Write(cashAssistanceTurnoutBonusPercent);
            writer.Write(civicForumTurnoutBonusPercent);
            writer.Write(donationDayKey);
            writer.Write(candidateCount);
            writer.Write(candidateC);
            writer.Write(candidateD);
            writer.Write(candidateCEffectId);
            writer.Write(candidateDEffectId);
            writer.Write(candidateCAge);
            writer.Write(candidateDAge);
            writer.Write(candidateCEducation);
            writer.Write(candidateDEducation);
            writer.Write(candidateCWorkType);
            writer.Write(candidateDWorkType);
            writer.Write(candidateCWealth);
            writer.Write(candidateDWealth);
            writer.Write(candidateCPortraitIndex);
            writer.Write(candidateDPortraitIndex);
            writer.Write(candidateCTagId);
            writer.Write(candidateDTagId);
            writer.Write(candidateCPlatformChirpSent);
            writer.Write(candidateDPlatformChirpSent);
            writer.Write(candidateCPlatformChirpDayKey);
            writer.Write(candidateDPlatformChirpDayKey);
            writer.Write(candidateCPlatformChirpMinute);
            writer.Write(candidateDPlatformChirpMinute);
            writer.Write(candidateCPlatformChirpUtcTicks);
            writer.Write(candidateDPlatformChirpUtcTicks);
            writer.Write(donationC);
            writer.Write(donationD);
            writer.Write(candidateCNegativeSoftened);
            writer.Write(candidateDNegativeSoftened);
            writer.Write(candidateCSoftenAttempted);
            writer.Write(candidateDSoftenAttempted);
            writer.Write(pollVotesC);
            writer.Write(pollVotesD);
            writer.Write(pollTeenVotesC);
            writer.Write(pollTeenVotesD);
            writer.Write(pollAdultVotesC);
            writer.Write(pollAdultVotesD);
            writer.Write(pollElderlyVotesC);
            writer.Write(pollElderlyVotesD);
            writer.Write(pollEducation0VotesC);
            writer.Write(pollEducation0VotesD);
            writer.Write(pollEducation1VotesC);
            writer.Write(pollEducation1VotesD);
            writer.Write(pollEducation2VotesC);
            writer.Write(pollEducation2VotesD);
            writer.Write(pollEducation3VotesC);
            writer.Write(pollEducation3VotesD);
            writer.Write(pollEducation4VotesC);
            writer.Write(pollEducation4VotesD);
            writer.Write(pollIncome0VotesC);
            writer.Write(pollIncome0VotesD);
            writer.Write(pollIncome1VotesC);
            writer.Write(pollIncome1VotesD);
            writer.Write(pollIncome2VotesC);
            writer.Write(pollIncome2VotesD);
            writer.Write(pollIncome3VotesC);
            writer.Write(pollIncome3VotesD);
            writer.Write(pollIncome4VotesC);
            writer.Write(pollIncome4VotesD);
            writer.Write(candidateCPollResponseChirpSent);
            writer.Write(candidateDPollResponseChirpSent);
            writer.Write(candidateCPollResponseChirpUtcTicks);
            writer.Write(candidateDPollResponseChirpUtcTicks);
            writer.Write(candidateCElectionReminderChirpSent);
            writer.Write(candidateDElectionReminderChirpSent);
            writer.Write(candidateCElectionReminderChirpDayKey);
            writer.Write(candidateDElectionReminderChirpDayKey);
            writer.Write(candidateCElectionReminderChirpMinute);
            writer.Write(candidateDElectionReminderChirpMinute);
            writer.Write(votesC);
            writer.Write(votesD);
            writer.Write(candidateCVotedChirpSent);
            writer.Write(candidateDVotedChirpSent);
            writer.Write(voteTamperingLostVotesC);
            writer.Write(voteTamperingLostVotesD);
            writer.Write(candidateCCorruptionRiskSteps);
            writer.Write(candidateDCorruptionRiskSteps);
            writer.Write(partyAName.ToString());
            writer.Write(partyBName.ToString());
            writer.Write(partyCName.ToString());
            writer.Write(partyDName.ToString());
            writer.Write(partyAColor);
            writer.Write(partyBColor);
            writer.Write(partyCColor);
            writer.Write(partyDColor);
            writer.Write(partyAReputation);
            writer.Write(partyBReputation);
            writer.Write(partyCReputation);
            writer.Write(partyDReputation);
            writer.Write(partyAConsecutiveTerms);
            writer.Write(partyBConsecutiveTerms);
            writer.Write(partyCConsecutiveTerms);
            writer.Write(partyDConsecutiveTerms);
            writer.Write(partyAWins);
            writer.Write(partyBWins);
            writer.Write(partyCWins);
            writer.Write(partyDWins);
            writer.Write(partyALastTagReplacementYear);
            writer.Write(partyBLastTagReplacementYear);
            writer.Write(partyCLastTagReplacementYear);
            writer.Write(partyDLastTagReplacementYear);
            writer.Write(partyATagId1);
            writer.Write(partyATagId2);
            writer.Write(partyATagId3);
            writer.Write(partyBTagId1);
            writer.Write(partyBTagId2);
            writer.Write(partyBTagId3);
            writer.Write(partyCTagId1);
            writer.Write(partyCTagId2);
            writer.Write(partyCTagId3);
            writer.Write(partyDTagId1);
            writer.Write(partyDTagId2);
            writer.Write(partyDTagId3);
            writer.Write(mayorPartyIndex);
            writer.Write(outgoingMayorPartyIndex);
            writer.Write(appliedEffectPartySignature);
            writer.Write(ElectionLegislation.NormalizeFlags(legislationFlags));
            writer.Write(legislationActionDayKey);
            writer.Write(partyReputationEventDayKey);
            writer.Write(partyMilestoneBonusYear);
            writer.Write(trackedMilestoneLevel);
            writer.Write(candidateASupportModifierPercent);
            writer.Write(candidateBSupportModifierPercent);
            writer.Write(candidateCSupportModifierPercent);
            writer.Write(candidateDSupportModifierPercent);
            writer.Write(runoffEnabledForCycle);
            writer.Write(runoffActive);
            writer.Write(runoffOriginalCandidateCount);
            writer.Write(pendingMayorCandidateIndex);
            writer.Write(pendingMayor);
            writer.Write(pendingMayorEffectId);
            writer.Write(pendingMayorTagId);
            writer.Write(pendingMayorNegativeSoftened);
            writer.Write(pendingMayorPartyIndex);
            writer.Write(pendingMayorTermYear);
            writer.Write(pendingMayorInaugurated);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int serializedVersion);
            int layoutVersion = GetSerializedLayoutVersion(serializedVersion);
            version = CurrentVersion;
            reader.Read(out initialized);
            reader.Read(out lastProcessedDayKey);
            reader.Read(out int stageValue);
            stage = (ElectionCampaignStage)stageValue;
            reader.Read(out acceleratedCycle);
            reader.Read(out selectionYear);
            reader.Read(out selectionMonth);
            reader.Read(out pollYear);
            reader.Read(out pollMonth);
            reader.Read(out electionYear);
            reader.Read(out electionMonth);
            reader.Read(out mayorTermYear);
            reader.Read(out candidateA);
            reader.Read(out candidateB);
            reader.Read(out candidateAEffectId);
            reader.Read(out candidateBEffectId);
            reader.Read(out candidateAAge);
            reader.Read(out candidateBAge);
            reader.Read(out candidateAEducation);
            reader.Read(out candidateBEducation);
            reader.Read(out candidateAWorkType);
            reader.Read(out candidateBWorkType);
            reader.Read(out candidateAWealth);
            reader.Read(out candidateBWealth);
            reader.Read(out candidateAPortraitIndex);
            reader.Read(out candidateBPortraitIndex);
            reader.Read(out candidateAPlatformChirpSent);
            reader.Read(out candidateBPlatformChirpSent);
            reader.Read(out candidateAPlatformChirpDayKey);
            reader.Read(out candidateBPlatformChirpDayKey);
            reader.Read(out candidateAPlatformChirpMinute);
            reader.Read(out candidateBPlatformChirpMinute);
            reader.Read(out candidateAPlatformChirpUtcTicks);
            reader.Read(out candidateBPlatformChirpUtcTicks);
            reader.Read(out donationA);
            reader.Read(out donationB);
            if (layoutVersion >= 10)
            {
                reader.Read(out campaignDonationAmount);
                reader.Read(out campaignBribeAmount);
            }
            else
            {
                campaignDonationAmount = 1000000;
                campaignBribeAmount = 5000000;
            }

            reader.Read(out pollVotesA);
            reader.Read(out pollVotesB);
            reader.Read(out pollUndecided);
            if (layoutVersion >= 12)
            {
                reader.Read(out pollTeenVotesA);
                reader.Read(out pollTeenVotesB);
                reader.Read(out pollTeenUndecided);
                reader.Read(out pollAdultVotesA);
                reader.Read(out pollAdultVotesB);
                reader.Read(out pollAdultUndecided);
                reader.Read(out pollElderlyVotesA);
                reader.Read(out pollElderlyVotesB);
                reader.Read(out pollElderlyUndecided);
                reader.Read(out pollEducation0VotesA);
                reader.Read(out pollEducation0VotesB);
                reader.Read(out pollEducation0Undecided);
                reader.Read(out pollEducation1VotesA);
                reader.Read(out pollEducation1VotesB);
                reader.Read(out pollEducation1Undecided);
                reader.Read(out pollEducation2VotesA);
                reader.Read(out pollEducation2VotesB);
                reader.Read(out pollEducation2Undecided);
                reader.Read(out pollEducation3VotesA);
                reader.Read(out pollEducation3VotesB);
                reader.Read(out pollEducation3Undecided);
                reader.Read(out pollEducation4VotesA);
                reader.Read(out pollEducation4VotesB);
                reader.Read(out pollEducation4Undecided);
            }
            else
            {
                pollTeenVotesA = 0;
                pollTeenVotesB = 0;
                pollTeenUndecided = 0;
                pollAdultVotesA = pollVotesA;
                pollAdultVotesB = pollVotesB;
                pollAdultUndecided = pollUndecided;
                pollElderlyVotesA = 0;
                pollElderlyVotesB = 0;
                pollElderlyUndecided = 0;
                pollEducation0VotesA = 0;
                pollEducation0VotesB = 0;
                pollEducation0Undecided = 0;
                pollEducation1VotesA = 0;
                pollEducation1VotesB = 0;
                pollEducation1Undecided = 0;
                pollEducation2VotesA = pollVotesA;
                pollEducation2VotesB = pollVotesB;
                pollEducation2Undecided = pollUndecided;
                pollEducation3VotesA = 0;
                pollEducation3VotesB = 0;
                pollEducation3Undecided = 0;
                pollEducation4VotesA = 0;
                pollEducation4VotesB = 0;
                pollEducation4Undecided = 0;
            }

            if (layoutVersion >= 20)
            {
                reader.Read(out pollIncome0VotesA);
                reader.Read(out pollIncome0VotesB);
                reader.Read(out pollIncome0Undecided);
                reader.Read(out pollIncome1VotesA);
                reader.Read(out pollIncome1VotesB);
                reader.Read(out pollIncome1Undecided);
                reader.Read(out pollIncome2VotesA);
                reader.Read(out pollIncome2VotesB);
                reader.Read(out pollIncome2Undecided);
                reader.Read(out pollIncome3VotesA);
                reader.Read(out pollIncome3VotesB);
                reader.Read(out pollIncome3Undecided);
                reader.Read(out pollIncome4VotesA);
                reader.Read(out pollIncome4VotesB);
                reader.Read(out pollIncome4Undecided);
            }
            else
            {
                ClearIncomePollBreakdown();
            }

            reader.Read(out candidateAPollResponseChirpSent);
            reader.Read(out candidateBPollResponseChirpSent);
            reader.Read(out candidateAPollResponseChirpUtcTicks);
            reader.Read(out candidateBPollResponseChirpUtcTicks);

            if (layoutVersion >= 3)
            {
                reader.Read(out candidateAElectionReminderChirpSent);
                reader.Read(out candidateBElectionReminderChirpSent);
                reader.Read(out candidateAElectionReminderChirpDayKey);
                reader.Read(out candidateBElectionReminderChirpDayKey);
                reader.Read(out candidateAElectionReminderChirpMinute);
                reader.Read(out candidateBElectionReminderChirpMinute);
            }
            else
            {
                candidateAElectionReminderChirpSent = false;
                candidateBElectionReminderChirpSent = false;
                candidateAElectionReminderChirpDayKey = 0;
                candidateBElectionReminderChirpDayKey = 0;
                candidateAElectionReminderChirpMinute = 0;
                candidateBElectionReminderChirpMinute = 0;
            }

            reader.Read(out electionDayKey);
            if (layoutVersion >= 4)
            {
                reader.Read(out votingStartMinute);
                reader.Read(out votingEndMinute);
                reader.Read(out resultsAnnouncementMinute);
            }
            else
            {
                votingStartMinute = Systems.ElectionUtility.DefaultVotingStartMinute;
                votingEndMinute = Systems.ElectionUtility.DefaultVotingEndMinute;
                resultsAnnouncementMinute = Systems.ElectionUtility.ResultsAnnouncementMinute;
            }

            reader.Read(out voteRequests);
            reader.Read(out voteArrivals);
            reader.Read(out votesA);
            reader.Read(out votesB);
            if (layoutVersion >= 5)
            {
                reader.Read(out candidateAVotedChirpSent);
                reader.Read(out candidateBVotedChirpSent);
            }
            else
            {
                candidateAVotedChirpSent = false;
                candidateBVotedChirpSent = false;
            }

            if (layoutVersion >= 16)
            {
                reader.Read(out votingClosedChirpSent);
            }
            else
            {
                votingClosedChirpSent = false;
            }

            if (layoutVersion >= 6)
            {
                reader.Read(out victoryPartyVenue);
                reader.Read(out victoryPartyElectionDayKey);
                reader.Read(out victoryPartyWinnerIndex);
                reader.Read(out victoryPartyTripsRequested);
                reader.Read(out victoryPartyTripRequests);
                reader.Read(out victoryWinnerChirpSent);
                reader.Read(out victoryLoserChirpSent);
                reader.Read(out victoryWinnerChirpUtcTicks);
                reader.Read(out victoryLoserChirpUtcTicks);
                if (layoutVersion >= 10)
                {
                    reader.Read(out victoryPartyNextVoterIndex);
                    reader.Read(out victoryPartyNextTripBatchTicks);
                    reader.Read(out victoryPartyWinnerTripRequested);
                }
                else
                {
                    victoryPartyNextVoterIndex = 0;
                    victoryPartyNextTripBatchTicks = 0;
                    victoryPartyWinnerTripRequested = false;
                }
            }
            else
            {
                victoryPartyVenue = Entity.Null;
                victoryPartyElectionDayKey = 0;
                victoryPartyWinnerIndex = -1;
                victoryPartyTripsRequested = false;
                victoryPartyTripRequests = 0;
                victoryWinnerChirpSent = false;
                victoryLoserChirpSent = false;
                victoryWinnerChirpUtcTicks = 0;
                victoryLoserChirpUtcTicks = 0;
                victoryPartyNextVoterIndex = 0;
                victoryPartyNextTripBatchTicks = 0;
                victoryPartyWinnerTripRequested = false;
            }
            reader.Read(out mayor);
            reader.Read(out mayorEffectId);
            reader.Read(out mayorEffectTermYear);
            reader.Read(out mayorMoneyApplied);
            reader.Read(out appliedEffectId);
            reader.Read(out appliedModifierType1);
            reader.Read(out appliedModifierAdd1);
            reader.Read(out appliedModifierMul1);
            reader.Read(out appliedModifierType2);
            reader.Read(out appliedModifierAdd2);
            reader.Read(out appliedModifierMul2);

            if (layoutVersion >= 2)
            {
                reader.Read(out candidateANegativeSoftened);
                reader.Read(out candidateBNegativeSoftened);
                reader.Read(out candidateASoftenAttempted);
                reader.Read(out candidateBSoftenAttempted);
                reader.Read(out mayorNegativeSoftened);
                reader.Read(out appliedNegativeSoftened);
                reader.Read(out bribeDayKey);
                reader.Read(out corruptionInvestigationChirpUtcTicks);
                reader.Read(out corruptionInvestigationMayor);
            }
            else
            {
                candidateANegativeSoftened = false;
                candidateBNegativeSoftened = false;
                candidateASoftenAttempted = false;
                candidateBSoftenAttempted = false;
                mayorNegativeSoftened = false;
                appliedNegativeSoftened = false;
                bribeDayKey = 0;
                corruptionInvestigationChirpUtcTicks = 0;
                corruptionInvestigationMayor = Entity.Null;
            }

            if (layoutVersion >= 7)
            {
                reader.Read(out bribeBlockedUntilTicks);
                reader.Read(out bribeMeetingCandidateIndex);
                reader.Read(out bribeMeetingCandidate);
                reader.Read(out bribeMeetingVenue);
                reader.Read(out bribeMeetingDeadlineTicks);
                reader.Read(out bribeMeetingNextAttemptTicks);
                reader.Read(out bribeMeetingTripsRequested);
            }
            else
            {
                bribeBlockedUntilTicks = 0;
                bribeMeetingCandidateIndex = -1;
                bribeMeetingCandidate = Entity.Null;
                bribeMeetingVenue = Entity.Null;
                bribeMeetingDeadlineTicks = 0;
                bribeMeetingNextAttemptTicks = 0;
                bribeMeetingTripsRequested = false;
            }

            if (layoutVersion >= 8)
            {
                reader.Read(out mayorEndorsementCandidateIndex);
                reader.Read(out mayorEndorsementCandidate);
                reader.Read(out mayorEndorsementChirpUtcTicks);
                reader.Read(out mayorEndorsementChirpSent);
            }
            else
            {
                mayorEndorsementCandidateIndex = -1;
                mayorEndorsementCandidate = Entity.Null;
                mayorEndorsementChirpUtcTicks = 0;
                mayorEndorsementChirpSent = false;
            }

            if (layoutVersion >= 9)
            {
                reader.Read(out voteTamperingCandidateIndex);
                reader.Read(out voteTamperingCandidate);
                reader.Read(out voteTamperingScheduledMinute);
                reader.Read(out voteTamperingFireStarted);
                reader.Read(out voteTamperingResolved);
                reader.Read(out voteTamperingPollingPlace);
                reader.Read(out voteTamperingLostVotesA);
                reader.Read(out voteTamperingLostVotesB);
                reader.Read(out voteTamperingProtestChirpUtcTicks);
                reader.Read(out voteTamperingProtestChirpSent);
                reader.Read(out candidateACorruptionRiskSteps);
                reader.Read(out candidateBCorruptionRiskSteps);
                reader.Read(out corruptionArrestCheckCompleted);
                if (layoutVersion >= 15)
                {
                    reader.Read(out mayorBribeRecipient);
                    reader.Read(out mayorBribeTotal);
                    reader.Read(out outgoingMayor);
                    reader.Read(out outgoingMayorBribeTotal);
                }
                else
                {
                    mayorBribeRecipient = Entity.Null;
                    mayorBribeTotal = 0;
                    outgoingMayor = Entity.Null;
                    outgoingMayorBribeTotal = 0;
                }
            }
            else
            {
                voteTamperingCandidateIndex = -1;
                voteTamperingCandidate = Entity.Null;
                voteTamperingScheduledMinute = 0;
                voteTamperingFireStarted = false;
                voteTamperingResolved = false;
                voteTamperingPollingPlace = Entity.Null;
                voteTamperingLostVotesA = 0;
                voteTamperingLostVotesB = 0;
                voteTamperingProtestChirpUtcTicks = 0;
                voteTamperingProtestChirpSent = false;
                candidateACorruptionRiskSteps = 0;
                candidateBCorruptionRiskSteps = 0;
                corruptionArrestCheckCompleted = false;
                mayorBribeRecipient = Entity.Null;
                mayorBribeTotal = 0;
                outgoingMayor = Entity.Null;
                outgoingMayorBribeTotal = 0;
            }

            if (layoutVersion >= 11)
            {
                reader.Read(out electionDayHolidayScheduled);
                reader.Read(out supportProgramDayKey);
                reader.Read(out supportProgramIdToday);
                reader.Read(out teenTurnoutBonusPercent);
                reader.Read(out adultTurnoutBonusPercent);
                reader.Read(out elderlyTurnoutBonusPercent);
                if (layoutVersion >= 14)
                {
                    reader.Read(out uneducatedTurnoutBonusPercent);
                    reader.Read(out educatedTurnoutBonusPercent);
                }
                else
                {
                    uneducatedTurnoutBonusPercent = 0;
                    educatedTurnoutBonusPercent = 0;
                }
            }
            else
            {
                electionDayHolidayScheduled = false;
                supportProgramDayKey = 0;
                supportProgramIdToday = -1;
                teenTurnoutBonusPercent = 0;
                adultTurnoutBonusPercent = 0;
                elderlyTurnoutBonusPercent = 0;
                uneducatedTurnoutBonusPercent = 0;
                educatedTurnoutBonusPercent = 0;
            }

            if (layoutVersion >= 13)
            {
                reader.Read(out strictVotingIdLawPassed);
                reader.Read(out strictVotingIdProposalPending);
                reader.Read(out strictVotingIdProposalPassed);
                reader.Read(out strictVotingIdChirpUtcTicks);
                reader.Read(out strictVotingIdChirpSent);
            }
            else
            {
                strictVotingIdLawPassed = false;
                strictVotingIdProposalPending = false;
                strictVotingIdProposalPassed = false;
                strictVotingIdChirpUtcTicks = 0;
                strictVotingIdChirpSent = false;
            }

            if (layoutVersion >= 17)
            {
                reader.Read(out supportProgramBalanceVersion);
            }
            else
            {
                supportProgramBalanceVersion = 0;
            }

            if (layoutVersion >= 18)
            {
                reader.Read(out mayorHome);
                reader.Read(out mayorWorkplace);
            }
            else
            {
                mayorHome = Entity.Null;
                mayorWorkplace = Entity.Null;
            }

            if (layoutVersion >= 19)
            {
                reader.Read(out candidateATagId);
                reader.Read(out candidateBTagId);
                reader.Read(out mayorTagId);
                reader.Read(out outgoingMayorTagId);
            }
            else
            {
                candidateATagId = 0;
                candidateBTagId = 0;
                mayorTagId = 0;
                outgoingMayorTagId = 0;
            }

            if (layoutVersion >= 24)
            {
                reader.Read(out appliedEffectTagId);
                reader.Read(out lowIncomeTurnoutBonusPercent);
                reader.Read(out transitVoucherTurnoutBonusPercent);
                reader.Read(out cashAssistanceTurnoutBonusPercent);
                reader.Read(out civicForumTurnoutBonusPercent);
                reader.Read(out donationDayKey);
            }
            else if (layoutVersion >= 23)
            {
                reader.Read(out appliedEffectTagId);
                reader.Read(out lowIncomeTurnoutBonusPercent);
                reader.Read(out transitVoucherTurnoutBonusPercent);
                reader.Read(out cashAssistanceTurnoutBonusPercent);
                reader.Read(out civicForumTurnoutBonusPercent);
                donationDayKey = 0;
            }
            else if (layoutVersion >= 22)
            {
                reader.Read(out appliedEffectTagId);
                reader.Read(out lowIncomeTurnoutBonusPercent);
                reader.Read(out transitVoucherTurnoutBonusPercent);
                reader.Read(out cashAssistanceTurnoutBonusPercent);
                civicForumTurnoutBonusPercent = 0;
                donationDayKey = 0;
            }
            else if (layoutVersion >= 21)
            {
                reader.Read(out appliedEffectTagId);
                reader.Read(out lowIncomeTurnoutBonusPercent);
                reader.Read(out transitVoucherTurnoutBonusPercent);
                reader.Read(out int legacyCandidateACashAssistanceSupportPercent);
                reader.Read(out int legacyCandidateBCashAssistanceSupportPercent);
                cashAssistanceTurnoutBonusPercent = legacyCandidateACashAssistanceSupportPercent > legacyCandidateBCashAssistanceSupportPercent
                    ? legacyCandidateACashAssistanceSupportPercent
                    : legacyCandidateBCashAssistanceSupportPercent;
                civicForumTurnoutBonusPercent = 0;
                donationDayKey = 0;
            }
            else
            {
                appliedEffectTagId = 0;
                lowIncomeTurnoutBonusPercent = 0;
                transitVoucherTurnoutBonusPercent = 0;
                cashAssistanceTurnoutBonusPercent = 0;
                civicForumTurnoutBonusPercent = 0;
                donationDayKey = 0;
            }

            if (layoutVersion >= 25)
            {
                reader.Read(out candidateCount);
                reader.Read(out candidateC);
                reader.Read(out candidateD);
                reader.Read(out candidateCEffectId);
                reader.Read(out candidateDEffectId);
                reader.Read(out candidateCAge);
                reader.Read(out candidateDAge);
                reader.Read(out candidateCEducation);
                reader.Read(out candidateDEducation);
                reader.Read(out candidateCWorkType);
                reader.Read(out candidateDWorkType);
                reader.Read(out candidateCWealth);
                reader.Read(out candidateDWealth);
                reader.Read(out candidateCPortraitIndex);
                reader.Read(out candidateDPortraitIndex);
                reader.Read(out candidateCTagId);
                reader.Read(out candidateDTagId);
                reader.Read(out candidateCPlatformChirpSent);
                reader.Read(out candidateDPlatformChirpSent);
                reader.Read(out candidateCPlatformChirpDayKey);
                reader.Read(out candidateDPlatformChirpDayKey);
                reader.Read(out candidateCPlatformChirpMinute);
                reader.Read(out candidateDPlatformChirpMinute);
                reader.Read(out candidateCPlatformChirpUtcTicks);
                reader.Read(out candidateDPlatformChirpUtcTicks);
                reader.Read(out donationC);
                reader.Read(out donationD);
                reader.Read(out candidateCNegativeSoftened);
                reader.Read(out candidateDNegativeSoftened);
                reader.Read(out candidateCSoftenAttempted);
                reader.Read(out candidateDSoftenAttempted);
                reader.Read(out pollVotesC);
                reader.Read(out pollVotesD);
                reader.Read(out pollTeenVotesC);
                reader.Read(out pollTeenVotesD);
                reader.Read(out pollAdultVotesC);
                reader.Read(out pollAdultVotesD);
                reader.Read(out pollElderlyVotesC);
                reader.Read(out pollElderlyVotesD);
                reader.Read(out pollEducation0VotesC);
                reader.Read(out pollEducation0VotesD);
                reader.Read(out pollEducation1VotesC);
                reader.Read(out pollEducation1VotesD);
                reader.Read(out pollEducation2VotesC);
                reader.Read(out pollEducation2VotesD);
                reader.Read(out pollEducation3VotesC);
                reader.Read(out pollEducation3VotesD);
                reader.Read(out pollEducation4VotesC);
                reader.Read(out pollEducation4VotesD);
                reader.Read(out pollIncome0VotesC);
                reader.Read(out pollIncome0VotesD);
                reader.Read(out pollIncome1VotesC);
                reader.Read(out pollIncome1VotesD);
                reader.Read(out pollIncome2VotesC);
                reader.Read(out pollIncome2VotesD);
                reader.Read(out pollIncome3VotesC);
                reader.Read(out pollIncome3VotesD);
                reader.Read(out pollIncome4VotesC);
                reader.Read(out pollIncome4VotesD);
                reader.Read(out candidateCPollResponseChirpSent);
                reader.Read(out candidateDPollResponseChirpSent);
                reader.Read(out candidateCPollResponseChirpUtcTicks);
                reader.Read(out candidateDPollResponseChirpUtcTicks);
                reader.Read(out candidateCElectionReminderChirpSent);
                reader.Read(out candidateDElectionReminderChirpSent);
                reader.Read(out candidateCElectionReminderChirpDayKey);
                reader.Read(out candidateDElectionReminderChirpDayKey);
                reader.Read(out candidateCElectionReminderChirpMinute);
                reader.Read(out candidateDElectionReminderChirpMinute);
                reader.Read(out votesC);
                reader.Read(out votesD);
                reader.Read(out candidateCVotedChirpSent);
                reader.Read(out candidateDVotedChirpSent);
                reader.Read(out voteTamperingLostVotesC);
                reader.Read(out voteTamperingLostVotesD);
                reader.Read(out candidateCCorruptionRiskSteps);
                reader.Read(out candidateDCorruptionRiskSteps);
            }
            else
            {
                ClearExtendedCandidateState();
                candidateCount = DefaultCandidateCount;
            }

            if (layoutVersion >= 26)
            {
                reader.Read(out string partyANameValue);
                reader.Read(out string partyBNameValue);
                reader.Read(out string partyCNameValue);
                reader.Read(out string partyDNameValue);
                partyAName = partyANameValue ?? string.Empty;
                partyBName = partyBNameValue ?? string.Empty;
                partyCName = partyCNameValue ?? string.Empty;
                partyDName = partyDNameValue ?? string.Empty;
                reader.Read(out partyAColor);
                reader.Read(out partyBColor);
                reader.Read(out partyCColor);
                reader.Read(out partyDColor);
                reader.Read(out partyAReputation);
                reader.Read(out partyBReputation);
                reader.Read(out partyCReputation);
                reader.Read(out partyDReputation);
                reader.Read(out partyAConsecutiveTerms);
                reader.Read(out partyBConsecutiveTerms);
                reader.Read(out partyCConsecutiveTerms);
                reader.Read(out partyDConsecutiveTerms);
                reader.Read(out partyAWins);
                reader.Read(out partyBWins);
                reader.Read(out partyCWins);
                reader.Read(out partyDWins);
                reader.Read(out partyALastTagReplacementYear);
                reader.Read(out partyBLastTagReplacementYear);
                reader.Read(out partyCLastTagReplacementYear);
                reader.Read(out partyDLastTagReplacementYear);
                reader.Read(out partyATagId1);
                reader.Read(out partyATagId2);
                reader.Read(out partyATagId3);
                reader.Read(out partyBTagId1);
                reader.Read(out partyBTagId2);
                reader.Read(out partyBTagId3);
                reader.Read(out partyCTagId1);
                reader.Read(out partyCTagId2);
                reader.Read(out partyCTagId3);
                reader.Read(out partyDTagId1);
                reader.Read(out partyDTagId2);
                reader.Read(out partyDTagId3);
                reader.Read(out mayorPartyIndex);
                reader.Read(out outgoingMayorPartyIndex);
                reader.Read(out appliedEffectPartySignature);
            }
            else
            {
                ClearPartyState();
            }

            if (layoutVersion >= 27)
            {
                reader.Read(out legislationFlags);
                legislationFlags = ElectionLegislation.NormalizeFlags(legislationFlags);
            }
            else
            {
                legislationFlags = strictVotingIdLawPassed
                    ? ElectionLegislation.GetFlag(ElectionLegislationType.VoterIdentification)
                    : 0;
            }

            if (layoutVersion >= 28)
                reader.Read(out legislationActionDayKey);
            else
                legislationActionDayKey = 0;

            if (layoutVersion >= 29)
            {
                reader.Read(out partyReputationEventDayKey);
                reader.Read(out partyMilestoneBonusYear);
                reader.Read(out trackedMilestoneLevel);
            }
            else
            {
                partyReputationEventDayKey = 0;
                partyMilestoneBonusYear = 0;
                trackedMilestoneLevel = -1;
            }

            if (layoutVersion >= 30)
            {
                reader.Read(out candidateASupportModifierPercent);
                reader.Read(out candidateBSupportModifierPercent);
                reader.Read(out candidateCSupportModifierPercent);
                reader.Read(out candidateDSupportModifierPercent);
                candidateASupportModifierPercent = ClampCandidateSupportModifierPercent(candidateASupportModifierPercent);
                candidateBSupportModifierPercent = ClampCandidateSupportModifierPercent(candidateBSupportModifierPercent);
                candidateCSupportModifierPercent = ClampCandidateSupportModifierPercent(candidateCSupportModifierPercent);
                candidateDSupportModifierPercent = ClampCandidateSupportModifierPercent(candidateDSupportModifierPercent);
            }
            else
            {
                candidateASupportModifierPercent = 0;
                candidateBSupportModifierPercent = 0;
                candidateCSupportModifierPercent = 0;
                candidateDSupportModifierPercent = 0;
            }

            if (layoutVersion >= 31)
            {
                reader.Read(out runoffEnabledForCycle);
                reader.Read(out runoffActive);
                reader.Read(out runoffOriginalCandidateCount);
                reader.Read(out pendingMayorCandidateIndex);
                reader.Read(out pendingMayor);
                reader.Read(out pendingMayorEffectId);
                reader.Read(out pendingMayorTagId);
                reader.Read(out pendingMayorNegativeSoftened);
                reader.Read(out pendingMayorPartyIndex);
                reader.Read(out pendingMayorTermYear);
                reader.Read(out pendingMayorInaugurated);
            }
            else
            {
                runoffEnabledForCycle = false;
                runoffActive = false;
                runoffOriginalCandidateCount = 0;
                pendingMayorCandidateIndex = -1;
                pendingMayor = Entity.Null;
                pendingMayorEffectId = 0;
                pendingMayorTagId = 0;
                pendingMayorNegativeSoftened = false;
                pendingMayorPartyIndex = -1;
                pendingMayorTermYear = 0;
                pendingMayorInaugurated = false;
            }

            strictVotingIdLawPassed = HasLegislation(ElectionLegislationType.VoterIdentification);

            candidateCount = NormalizeCandidateCount(candidateCount <= 0 ? DefaultCandidateCount : candidateCount);
            runoffOriginalCandidateCount = runoffOriginalCandidateCount <= 0 ? candidateCount : NormalizeCandidateCount(runoffOriginalCandidateCount);
            if (pendingMayor == Entity.Null)
            {
                pendingMayorCandidateIndex = -1;
                pendingMayorEffectId = 0;
                pendingMayorTagId = ElectionCandidateTags.None;
                pendingMayorNegativeSoftened = false;
                pendingMayorPartyIndex = -1;
                pendingMayorTermYear = 0;
                pendingMayorInaugurated = false;
            }
        }

        private void ClearPartyState()
        {
            partyAName = string.Empty;
            partyBName = string.Empty;
            partyCName = string.Empty;
            partyDName = string.Empty;
            partyAColor = 0;
            partyBColor = 0;
            partyCColor = 0;
            partyDColor = 0;
            partyAReputation = 0;
            partyBReputation = 0;
            partyCReputation = 0;
            partyDReputation = 0;
            partyAConsecutiveTerms = 0;
            partyBConsecutiveTerms = 0;
            partyCConsecutiveTerms = 0;
            partyDConsecutiveTerms = 0;
            partyAWins = 0;
            partyBWins = 0;
            partyCWins = 0;
            partyDWins = 0;
            partyALastTagReplacementYear = 0;
            partyBLastTagReplacementYear = 0;
            partyCLastTagReplacementYear = 0;
            partyDLastTagReplacementYear = 0;
            partyATagId1 = ElectionPartyTags.None;
            partyATagId2 = ElectionPartyTags.None;
            partyATagId3 = ElectionPartyTags.None;
            partyBTagId1 = ElectionPartyTags.None;
            partyBTagId2 = ElectionPartyTags.None;
            partyBTagId3 = ElectionPartyTags.None;
            partyCTagId1 = ElectionPartyTags.None;
            partyCTagId2 = ElectionPartyTags.None;
            partyCTagId3 = ElectionPartyTags.None;
            partyDTagId1 = ElectionPartyTags.None;
            partyDTagId2 = ElectionPartyTags.None;
            partyDTagId3 = ElectionPartyTags.None;
            mayorPartyIndex = -1;
            outgoingMayorPartyIndex = -1;
            appliedEffectPartySignature = 0;
        }

        private void ClearIncomePollBreakdown()
        {
            pollIncome0VotesA = 0;
            pollIncome0VotesB = 0;
            pollIncome0Undecided = 0;
            pollIncome1VotesA = 0;
            pollIncome1VotesB = 0;
            pollIncome1Undecided = 0;
            pollIncome2VotesA = 0;
            pollIncome2VotesB = 0;
            pollIncome2Undecided = 0;
            pollIncome3VotesA = 0;
            pollIncome3VotesB = 0;
            pollIncome3Undecided = 0;
            pollIncome4VotesA = 0;
            pollIncome4VotesB = 0;
            pollIncome4Undecided = 0;
        }

        private void ClearExtendedCandidateState()
        {
            candidateC = Entity.Null;
            candidateD = Entity.Null;
            candidateCEffectId = 0;
            candidateDEffectId = 0;
            candidateCAge = 0;
            candidateDAge = 0;
            candidateCEducation = 0;
            candidateDEducation = 0;
            candidateCWorkType = 0;
            candidateDWorkType = 0;
            candidateCWealth = 0;
            candidateDWealth = 0;
            candidateCPortraitIndex = -1;
            candidateDPortraitIndex = -1;
            candidateCTagId = 0;
            candidateDTagId = 0;
            candidateCSupportModifierPercent = 0;
            candidateDSupportModifierPercent = 0;
            candidateCPlatformChirpSent = true;
            candidateDPlatformChirpSent = true;
            candidateCPlatformChirpDayKey = 0;
            candidateDPlatformChirpDayKey = 0;
            candidateCPlatformChirpMinute = 0;
            candidateDPlatformChirpMinute = 0;
            candidateCPlatformChirpUtcTicks = 0;
            candidateDPlatformChirpUtcTicks = 0;
            donationC = 0;
            donationD = 0;
            candidateCNegativeSoftened = false;
            candidateDNegativeSoftened = false;
            candidateCSoftenAttempted = false;
            candidateDSoftenAttempted = false;
            pollVotesC = 0;
            pollVotesD = 0;
            pollTeenVotesC = 0;
            pollTeenVotesD = 0;
            pollAdultVotesC = 0;
            pollAdultVotesD = 0;
            pollElderlyVotesC = 0;
            pollElderlyVotesD = 0;
            pollEducation0VotesC = 0;
            pollEducation0VotesD = 0;
            pollEducation1VotesC = 0;
            pollEducation1VotesD = 0;
            pollEducation2VotesC = 0;
            pollEducation2VotesD = 0;
            pollEducation3VotesC = 0;
            pollEducation3VotesD = 0;
            pollEducation4VotesC = 0;
            pollEducation4VotesD = 0;
            pollIncome0VotesC = 0;
            pollIncome0VotesD = 0;
            pollIncome1VotesC = 0;
            pollIncome1VotesD = 0;
            pollIncome2VotesC = 0;
            pollIncome2VotesD = 0;
            pollIncome3VotesC = 0;
            pollIncome3VotesD = 0;
            pollIncome4VotesC = 0;
            pollIncome4VotesD = 0;
            candidateCPollResponseChirpSent = true;
            candidateDPollResponseChirpSent = true;
            candidateCPollResponseChirpUtcTicks = 0;
            candidateDPollResponseChirpUtcTicks = 0;
            candidateCElectionReminderChirpSent = true;
            candidateDElectionReminderChirpSent = true;
            candidateCElectionReminderChirpDayKey = 0;
            candidateDElectionReminderChirpDayKey = 0;
            candidateCElectionReminderChirpMinute = 0;
            candidateDElectionReminderChirpMinute = 0;
            votesC = 0;
            votesD = 0;
            candidateCVotedChirpSent = false;
            candidateDVotedChirpSent = false;
            voteTamperingLostVotesC = 0;
            voteTamperingLostVotesD = 0;
            candidateCCorruptionRiskSteps = 0;
            candidateDCorruptionRiskSteps = 0;
        }

        private static int GetSerializedLayoutVersion(int serializedVersion)
        {
            if (serializedVersion == 1)
                return 16;

            if (serializedVersion == 23)
                return 31;

            if (serializedVersion == CurrentVersion)
                return CurrentSerializedLayoutVersion;

            return serializedVersion > CurrentSerializedLayoutVersion
                ? CurrentSerializedLayoutVersion
                : serializedVersion;
        }
    }
}
