using Colossal.Mathematics;
using Elections.Components;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Rendering;
using Game.Tools;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Color = UnityEngine.Color;

namespace Elections.Systems
{
    public partial class ElectionVotingLocationOverlaySystem : SystemBase
    {
        private OverlayRenderSystem m_OverlayRenderSystem;
        private CameraUpdateSystem m_CameraUpdateSystem;
        private EntityQuery m_StateQuery;
        private EntityQuery m_VoteTripQuery;
        private EntityQuery m_SchoolQuery;
        private EntityQuery m_PoliceQuery;
        private EntityQuery m_FireQuery;
        private EntityQuery m_WelfareQuery;
        private EntityQuery m_AdminQuery;
        private readonly Dictionary<Entity, VoteTally> m_VoteTallies = new Dictionary<Entity, VoteTally>(128);
        private readonly HashSet<Entity> m_PollingPlaceSet = new HashSet<Entity>();

        protected override void OnCreate()
        {
            base.OnCreate();

            m_OverlayRenderSystem = World.GetExistingSystemManaged<OverlayRenderSystem>();
            m_CameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_StateQuery = GetEntityQuery(ComponentType.ReadOnly<ElectionState>());
            m_VoteTripQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ElectionVoteTrip>() },
                None = new[] { ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>() }
            });

            m_SchoolQuery = CreatePollingPlaceQuery(ComponentType.ReadOnly<School>());
            m_PoliceQuery = CreatePollingPlaceQuery(ComponentType.ReadOnly<PoliceStation>());
            m_FireQuery = CreatePollingPlaceQuery(ComponentType.ReadOnly<FireStation>());
            m_WelfareQuery = CreatePollingPlaceQuery(ComponentType.ReadOnly<WelfareOffice>());
            m_AdminQuery = CreatePollingPlaceQuery(ComponentType.ReadOnly<AdminBuilding>());
        }

        protected override void OnUpdate()
        {
            if (!ElectionUISystem.ShowVotingLocations || !(Mod.m_Setting?.EnableElections ?? false))
                return;

            ElectionState state = default;
            bool showVoteCounts = false;
            if (!m_StateQuery.IsEmptyIgnoreFilter)
            {
                state = m_StateQuery.GetSingleton<ElectionState>();
                showVoteCounts = state.stage == ElectionCampaignStage.Voting && state.electionDayKey != 0;
            }

            if (showVoteCounts)
                TallyVotes(state.electionDayKey);
            else
                m_VoteTallies.Clear();

            m_PollingPlaceSet.Clear();
            NativeList<VotingLocationMarker> markers = new NativeList<VotingLocationMarker>(128, Allocator.TempJob);
            AddPollingPlaces(m_SchoolQuery, markers, showVoteCounts);
            AddPollingPlaces(m_PoliceQuery, markers, showVoteCounts);
            AddPollingPlaces(m_FireQuery, markers, showVoteCounts);
            AddPollingPlaces(m_WelfareQuery, markers, showVoteCounts);
            AddPollingPlaces(m_AdminQuery, markers, showVoteCounts);

            if (markers.Length == 0)
            {
                markers.Dispose();
                return;
            }

            float3 cameraRight = new float3(1f, 0f, 0f);
            float3 cameraUp = new float3(0f, 1f, 0f);
            float3 cameraPosition = float3.zero;
            if (UnityEngine.Camera.main != null)
            {
                cameraRight = UnityEngine.Camera.main.transform.right;
                cameraUp = UnityEngine.Camera.main.transform.up;
                cameraPosition = UnityEngine.Camera.main.transform.position;
            }

            OverlayRenderSystem.Buffer buffer = m_OverlayRenderSystem.GetBuffer(out JobHandle dependencies);
            DrawVotingLocationOverlayJob drawJob = new DrawVotingLocationOverlayJob
            {
                overlayBuffer = buffer,
                markers = markers.AsArray(),
                showVoteCounts = showVoteCounts,
                zoomLevel = m_CameraUpdateSystem.zoom,
                cameraRight = cameraRight,
                cameraUp = cameraUp,
                cameraPosition = cameraPosition
            };

            JobHandle drawHandle = drawJob.Schedule(dependencies);
            markers.Dispose(drawHandle);
            Dependency = drawHandle;
            m_OverlayRenderSystem.AddBufferWriter(Dependency);
        }

        private EntityQuery CreatePollingPlaceQuery(ComponentType componentType)
        {
            return GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), componentType },
                None = new[] { ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>() }
            });
        }

        private void TallyVotes(int electionDayKey)
        {
            m_VoteTallies.Clear();

            using (NativeArray<ElectionVoteTrip> voteTrips = m_VoteTripQuery.ToComponentDataArray<ElectionVoteTrip>(Allocator.Temp))
            {
                for (int i = 0; i < voteTrips.Length; i++)
                {
                    ElectionVoteTrip voteTrip = voteTrips[i];
                    if (!voteTrip.voted || voteTrip.electionDayKey != electionDayKey || voteTrip.pollingPlace == Entity.Null)
                        continue;

                    m_VoteTallies.TryGetValue(voteTrip.pollingPlace, out VoteTally tally);
                    if (voteTrip.chosenCandidate == 0)
                        tally.votesA++;
                    else if (voteTrip.chosenCandidate == 1)
                        tally.votesB++;

                    m_VoteTallies[voteTrip.pollingPlace] = tally;
                }
            }
        }

        private void AddPollingPlaces(EntityQuery query, NativeList<VotingLocationMarker> markers, bool showVoteCounts)
        {
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    if (!m_PollingPlaceSet.Add(entity) ||
                        !EntityManager.HasComponent<Game.Objects.Transform>(entity))
                    {
                        continue;
                    }

                    Game.Objects.Transform transform = EntityManager.GetComponentData<Game.Objects.Transform>(entity);
                    VoteTally tally = default;
                    if (showVoteCounts)
                        m_VoteTallies.TryGetValue(entity, out tally);

                    markers.Add(new VotingLocationMarker
                    {
                        position = transform.m_Position,
                        votesA = tally.votesA,
                        votesB = tally.votesB
                    });
                }
            }
        }

        private struct VoteTally
        {
            public int votesA;
            public int votesB;
        }

        private struct VotingLocationMarker
        {
            public float3 position;
            public int votesA;
            public int votesB;
        }

        private struct DrawVotingLocationOverlayJob : IJob
        {
            public OverlayRenderSystem.Buffer overlayBuffer;
            [ReadOnly] public NativeArray<VotingLocationMarker> markers;
            public bool showVoteCounts;
            public float zoomLevel;
            public float3 cameraRight;
            public float3 cameraUp;
            public float3 cameraPosition;

            public void Execute()
            {
                float rawZoom = math.clamp((zoomLevel - 900f) / 12500f, 0f, 1f);
                float normalizedZoom = math.pow(rawZoom, 0.68f);
                float thickness = math.lerp(3.0f, 21.0f, normalizedZoom);
                float badgeSize = thickness * 3.0f;
                float groundRadius = thickness * 5.2f;
                float markerHeight = math.lerp(14f, 54f, normalizedZoom);

                float3 right = math.normalizesafe(cameraRight, new float3(1f, 0f, 0f));
                float3 up = math.normalizesafe(cameraUp, new float3(0f, 1f, 0f));
                float3 worldUp = new float3(0f, 1f, 0f);

                for (int i = 0; i < markers.Length; i++)
                {
                    VotingLocationMarker marker = markers[i];
                    float3 ground = marker.position + worldUp * 0.6f;
                    float3 center = marker.position + worldUp * markerHeight;

                    overlayBuffer.DrawCircle(new Color(0.06f, 0.12f, 0.2f, 0.72f), ground, groundRadius + thickness * 0.28f);
                    overlayBuffer.DrawCircle(new Color(0.95f, 0.72f, 0.18f, 0.22f), ground, groundRadius);
                    overlayBuffer.DrawLine(new Color(0.95f, 0.72f, 0.18f, 0.75f), new Line3.Segment(ground, center - up * (badgeSize * 0.9f)), thickness * 0.32f);

                    DrawBallotMarker(center, badgeSize, thickness, right, up);

                    if (showVoteCounts)
                    {
                        float3 labelCenter = center - up * (badgeSize * 1.18f);
                        DrawVoteSplitIndicator(
                            labelCenter,
                            marker.votesA,
                            marker.votesB,
                            new Color(0.06f, 0.12f, 0.2f, 0.94f),
                            new Color(1.0f, 0.84f, 0.36f, 1f),
                            thickness,
                            right,
                            up);
                    }
                }
            }

            private void DrawBallotMarker(float3 center, float size, float thickness, float3 right, float3 up)
            {
                Color navy = new Color(0.04f, 0.11f, 0.2f, 0.96f);
                Color outline = new Color(0.99f, 0.82f, 0.32f, 1f);
                Color paper = new Color(0.98f, 0.98f, 0.94f, 1f);
                Color box = new Color(0.18f, 0.38f, 0.62f, 1f);
                Color hand = new Color(1.0f, 0.72f, 0.45f, 1f);

                float badgeWidth = size * 2.12f;
                float badgeHeight = size * 1.72f;
                DrawPill(center, badgeWidth, badgeHeight, thickness * 0.55f, right, outline);
                DrawPill(center, badgeWidth, badgeHeight, 0f, right, navy);

                float boxWidth = size * 1.12f;
                float boxHeight = size * 0.56f;
                float3 boxCenter = center - up * (size * 0.24f);
                DrawPill(boxCenter, boxWidth, boxHeight, thickness * 0.23f, right, outline);
                DrawPill(boxCenter, boxWidth, boxHeight, 0f, right, box);

                float3 slotCenter = boxCenter + up * (boxHeight * 0.5f);
                overlayBuffer.DrawLine(outline, new Line3.Segment(slotCenter - right * (boxWidth * 0.28f), slotCenter + right * (boxWidth * 0.28f)), thickness * 0.22f);

                float ballotWidth = size * 0.44f;
                float ballotHeight = size * 0.56f;
                float3 ballotCenter = center + up * (size * 0.38f) - right * (size * 0.07f);
                DrawPill(ballotCenter, ballotWidth, ballotHeight, thickness * 0.14f, right, outline);
                DrawPill(ballotCenter, ballotWidth, ballotHeight, 0f, right, paper);
                overlayBuffer.DrawLine(box, new Line3.Segment(ballotCenter - right * (ballotWidth * 0.24f) - up * (ballotHeight * 0.08f), ballotCenter + right * (ballotWidth * 0.24f) - up * (ballotHeight * 0.08f)), thickness * 0.12f);

                float3 wrist = center - right * (size * 0.82f) + up * (size * 0.33f);
                float3 palm = center - right * (size * 0.3f) + up * (size * 0.26f);
                overlayBuffer.DrawLine(hand, new Line3.Segment(wrist, palm), thickness * 0.42f);
                overlayBuffer.DrawLine(hand, new Line3.Segment(palm, ballotCenter - right * (ballotWidth * 0.2f) + up * (ballotHeight * 0.28f)), thickness * 0.18f);
                overlayBuffer.DrawLine(new Color(0.55f, 0.32f, 0.18f, 0.95f), new Line3.Segment(wrist - up * (thickness * 0.12f), palm - up * (thickness * 0.12f)), thickness * 0.08f);
            }

            private void DrawPill(float3 center, float width, float height, float outlinePadding, float3 right, Color color)
            {
                float3 start = center - right * (width * 0.5f);
                float3 end = center + right * (width * 0.5f);
                overlayBuffer.DrawLine(color, new Line3.Segment(start, end), height + outlinePadding);
            }

            private void DrawVoteSplitIndicator(float3 center, int votesA, int votesB, Color bgColor, Color textColor, float thickness, float3 right, float3 up)
            {
                int number = math.max(0, votesA + votesB);
                number = math.max(0, number);
                int digitsCount = GetDigitCount(number);
                NativeList<int> digits = new NativeList<int>(digitsCount, Allocator.Temp);
                int tempNum = number;
                if (tempNum == 0)
                {
                    digits.Add(0);
                }
                else
                {
                    while (tempNum > 0)
                    {
                        digits.Add(tempNum % 10);
                        tempNum /= 10;
                    }
                }

                float digitWidth = thickness * 1.05f;
                float digitHeight = thickness * 2.0f;
                float spacing = thickness * 0.48f;
                float totalWidth = digits.Length * digitWidth + math.max(0, digits.Length - 1) * spacing;
                float bgWidth = totalWidth + thickness * 4.2f;

                overlayBuffer.DrawLine(bgColor, new Line3.Segment(center - right * (bgWidth * 0.5f), center + right * (bgWidth * 0.5f)), digitHeight + thickness * 1.75f);
                overlayBuffer.DrawLine(new Color(0.95f, 0.72f, 0.18f, 0.85f), new Line3.Segment(center - right * (bgWidth * 0.5f), center + right * (bgWidth * 0.5f)), thickness * 0.2f);

                if (number > 0)
                {
                    float barWidth = bgWidth - thickness * 1.7f;
                    float3 barCenter = center + up * (digitHeight * 0.66f);
                    DrawVoteSplitBar(barCenter, barWidth, thickness * 0.72f, votesA, votesB, right);
                }

                float3 cursor = center + right * (totalWidth * 0.5f - digitWidth * 0.5f) - up * (thickness * 0.28f);
                for (int i = 0; i < digits.Length; i++)
                {
                    DrawDigit(cursor, digits[i], textColor, digitWidth, digitHeight, thickness * 0.34f, right, up);
                    cursor -= right * (digitWidth + spacing);
                }

                digits.Dispose();
            }

            private void DrawVoteSplitBar(float3 center, float width, float height, int votesA, int votesB, float3 right)
            {
                int total = math.max(1, votesA + votesB);
                float aShare = math.saturate(votesA / (float)total);
                float3 left = center - right * (width * 0.5f);
                float3 split = left + right * (width * aShare);
                float3 end = center + right * (width * 0.5f);

                overlayBuffer.DrawLine(new Color(0.18f, 0.2f, 0.26f, 1f), new Line3.Segment(left, end), height);

                if (votesA > 0)
                    overlayBuffer.DrawLine(new Color(0.69f, 0.42f, 1.0f, 1f), new Line3.Segment(left, split), height);

                if (votesB > 0)
                    overlayBuffer.DrawLine(new Color(0.38f, 0.82f, 0.44f, 1f), new Line3.Segment(split, end), height);
            }

            private int GetDigitCount(int number)
            {
                if (number <= 0)
                    return 1;

                int count = 0;
                while (number > 0)
                {
                    count++;
                    number /= 10;
                }

                return count;
            }

            private void DrawDigit(float3 center, int digit, Color color, float width, float height, float thickness, float3 right, float3 up)
            {
                byte mask = GetDigitMask(digit);
                float halfWidth = width * 0.5f;
                float halfHeight = height * 0.5f;

                float3 topLeft = center - right * halfWidth + up * halfHeight;
                float3 topRight = center + right * halfWidth + up * halfHeight;
                float3 middleLeft = center - right * halfWidth;
                float3 middleRight = center + right * halfWidth;
                float3 bottomLeft = center - right * halfWidth - up * halfHeight;
                float3 bottomRight = center + right * halfWidth - up * halfHeight;

                float3 insetX = right * (thickness * 0.2f);
                float3 insetY = up * (thickness * 0.2f);

                if ((mask & 1) != 0) overlayBuffer.DrawLine(color, new Line3.Segment(topLeft + insetX, topRight - insetX), thickness);
                if ((mask & 2) != 0) overlayBuffer.DrawLine(color, new Line3.Segment(topRight - insetY, middleRight + insetY), thickness);
                if ((mask & 4) != 0) overlayBuffer.DrawLine(color, new Line3.Segment(middleRight - insetY, bottomRight + insetY), thickness);
                if ((mask & 8) != 0) overlayBuffer.DrawLine(color, new Line3.Segment(bottomRight - insetX, bottomLeft + insetX), thickness);
                if ((mask & 16) != 0) overlayBuffer.DrawLine(color, new Line3.Segment(bottomLeft + insetY, middleLeft - insetY), thickness);
                if ((mask & 32) != 0) overlayBuffer.DrawLine(color, new Line3.Segment(middleLeft + insetY, topLeft - insetY), thickness);
                if ((mask & 64) != 0) overlayBuffer.DrawLine(color, new Line3.Segment(middleLeft + insetX, middleRight - insetX), thickness);
            }

            private byte GetDigitMask(int digit)
            {
                switch (digit)
                {
                    case 0: return 0x3F;
                    case 1: return 0x06;
                    case 2: return 0x5B;
                    case 3: return 0x4F;
                    case 4: return 0x66;
                    case 5: return 0x6D;
                    case 6: return 0x7D;
                    case 7: return 0x07;
                    case 8: return 0x7F;
                    case 9: return 0x6F;
                    default: return 0;
                }
            }
        }
    }
}
