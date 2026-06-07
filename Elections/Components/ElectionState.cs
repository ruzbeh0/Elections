using Colossal.Serialization.Entities;
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
        public const int CurrentVersion = 1;
        private const int CurrentSerializedLayoutVersion = 16;

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

        public Entity candidateA;
        public Entity candidateB;
        public int candidateAEffectId;
        public int candidateBEffectId;
        public int candidateAAge;
        public int candidateBAge;
        public int candidateAEducation;
        public int candidateBEducation;
        public int candidateAWorkType;
        public int candidateBWorkType;
        public int candidateAWealth;
        public int candidateBWealth;
        public int candidateAPortraitIndex;
        public int candidateBPortraitIndex;
        public bool candidateAPlatformChirpSent;
        public bool candidateBPlatformChirpSent;
        public int candidateAPlatformChirpDayKey;
        public int candidateBPlatformChirpDayKey;
        public int candidateAPlatformChirpMinute;
        public int candidateBPlatformChirpMinute;
        public long candidateAPlatformChirpUtcTicks;
        public long candidateBPlatformChirpUtcTicks;

        public int donationA;
        public int donationB;
        public int campaignDonationAmount;
        public int campaignBribeAmount;
        public bool candidateANegativeSoftened;
        public bool candidateBNegativeSoftened;
        public bool candidateASoftenAttempted;
        public bool candidateBSoftenAttempted;
        public int pollVotesA;
        public int pollVotesB;
        public int pollUndecided;
        public int pollTeenVotesA;
        public int pollTeenVotesB;
        public int pollTeenUndecided;
        public int pollAdultVotesA;
        public int pollAdultVotesB;
        public int pollAdultUndecided;
        public int pollElderlyVotesA;
        public int pollElderlyVotesB;
        public int pollElderlyUndecided;
        public int pollEducation0VotesA;
        public int pollEducation0VotesB;
        public int pollEducation0Undecided;
        public int pollEducation1VotesA;
        public int pollEducation1VotesB;
        public int pollEducation1Undecided;
        public int pollEducation2VotesA;
        public int pollEducation2VotesB;
        public int pollEducation2Undecided;
        public int pollEducation3VotesA;
        public int pollEducation3VotesB;
        public int pollEducation3Undecided;
        public int pollEducation4VotesA;
        public int pollEducation4VotesB;
        public int pollEducation4Undecided;
        public bool candidateAPollResponseChirpSent;
        public bool candidateBPollResponseChirpSent;
        public long candidateAPollResponseChirpUtcTicks;
        public long candidateBPollResponseChirpUtcTicks;
        public bool candidateAElectionReminderChirpSent;
        public bool candidateBElectionReminderChirpSent;
        public int candidateAElectionReminderChirpDayKey;
        public int candidateBElectionReminderChirpDayKey;
        public int candidateAElectionReminderChirpMinute;
        public int candidateBElectionReminderChirpMinute;

        public int electionDayKey;
        public int votingStartMinute;
        public int votingEndMinute;
        public int resultsAnnouncementMinute;
        public int voteRequests;
        public int voteArrivals;
        public int votesA;
        public int votesB;
        public bool candidateAVotedChirpSent;
        public bool candidateBVotedChirpSent;
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

        public int appliedEffectId;
        public bool appliedNegativeSoftened;
        public int appliedModifierType1;
        public float appliedModifierAdd1;
        public float appliedModifierMul1;
        public int appliedModifierType2;
        public float appliedModifierAdd2;
        public float appliedModifierMul2;

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
        public long voteTamperingProtestChirpUtcTicks;
        public bool voteTamperingProtestChirpSent;
        public int candidateACorruptionRiskSteps;
        public int candidateBCorruptionRiskSteps;
        public bool corruptionArrestCheckCompleted;
        public Entity mayorBribeRecipient;
        public int mayorBribeTotal;
        public Entity outgoingMayor;
        public int outgoingMayorBribeTotal;
        public bool electionDayHolidayScheduled;
        public int supportProgramDayKey;
        public int supportProgramIdToday;
        public int teenTurnoutBonusPercent;
        public int adultTurnoutBonusPercent;
        public int elderlyTurnoutBonusPercent;
        public int uneducatedTurnoutBonusPercent;
        public int educatedTurnoutBonusPercent;
        public bool strictVotingIdLawPassed;
        public bool strictVotingIdProposalPending;
        public bool strictVotingIdProposalPassed;
        public long strictVotingIdChirpUtcTicks;
        public bool strictVotingIdChirpSent;

        public bool HasCandidates => candidateA != Entity.Null && candidateB != Entity.Null;

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
        }

        private static int GetSerializedLayoutVersion(int serializedVersion)
        {
            if (serializedVersion == CurrentVersion)
                return CurrentSerializedLayoutVersion;

            return serializedVersion > CurrentSerializedLayoutVersion
                ? CurrentSerializedLayoutVersion
                : serializedVersion;
        }
    }
}
