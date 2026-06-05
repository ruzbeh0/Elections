using Colossal.Serialization.Entities;
using Unity.Entities;

namespace Elections.Components
{
    public struct ElectionVoteTrip : IComponentData, IQueryTypeParameter, ISerializable
    {
        public int version;
        public int electionDayKey;
        public Entity pollingPlace;
        public bool voted;
        public int chosenCandidate;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(version);
            writer.Write(electionDayKey);
            writer.Write(pollingPlace);
            writer.Write(voted);
            writer.Write(chosenCandidate);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out version);
            reader.Read(out electionDayKey);
            reader.Read(out pollingPlace);
            reader.Read(out voted);
            reader.Read(out chosenCandidate);
        }
    }
}
