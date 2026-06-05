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
        public const int CurrentVersion = 5;

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
        public bool candidateANegativeSoftened;
        public bool candidateBNegativeSoftened;
        public bool candidateASoftenAttempted;
        public bool candidateBSoftenAttempted;
        public int pollVotesA;
        public int pollVotesB;
        public int pollUndecided;
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
            writer.Write(pollVotesA);
            writer.Write(pollVotesB);
            writer.Write(pollUndecided);
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
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out version);
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
            reader.Read(out pollVotesA);
            reader.Read(out pollVotesB);
            reader.Read(out pollUndecided);
            reader.Read(out candidateAPollResponseChirpSent);
            reader.Read(out candidateBPollResponseChirpSent);
            reader.Read(out candidateAPollResponseChirpUtcTicks);
            reader.Read(out candidateBPollResponseChirpUtcTicks);

            if (version >= 3)
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
            if (version >= 4)
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
            if (version >= 5)
            {
                reader.Read(out candidateAVotedChirpSent);
                reader.Read(out candidateBVotedChirpSent);
            }
            else
            {
                candidateAVotedChirpSent = false;
                candidateBVotedChirpSent = false;
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

            if (version >= 2)
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
        }
    }
}
