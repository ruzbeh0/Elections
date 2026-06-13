using Colossal.Mathematics;
using Elections.Components;
using Elections.Models;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Rendering;
using Game.Tools;
using System.Collections.Generic;
using Unity.Burst;
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
        private EntityQuery m_WelfareQuery;
        private EntityQuery m_AdminQuery;
        private EntityQuery m_PostQuery;
        private readonly Dictionary<Entity, VoteTally> m_VoteTallies = new Dictionary<Entity, VoteTally>(128);
        private readonly Dictionary<Entity, VoteTally> m_CurrentVoteTallies = new Dictionary<Entity, VoteTally>(128);
        private readonly HashSet<Entity> m_PollingPlaceSet = new HashSet<Entity>();
        private int m_LastTalliedElectionDayKey;
        private int m_LastTalliedVoteArrivals;
        private int m_LastTalliedVotesA;
        private int m_LastTalliedVotesB;
        private int m_LastTalliedVotesC;
        private int m_LastTalliedVotesD;

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
            m_WelfareQuery = CreatePollingPlaceQuery(ComponentType.ReadOnly<WelfareOffice>());
            m_AdminQuery = CreatePollingPlaceQuery(ComponentType.ReadOnly<AdminBuilding>());
            m_PostQuery = CreatePollingPlaceQuery(ComponentType.ReadOnly<Game.Buildings.PostFacility>());
        }

        protected override void OnUpdate()
        {
            if (!ElectionUISystem.ShowVotingLocations || !(Mod.m_Setting?.EnableElections ?? false))
                return;

            ElectionState state = default;
            bool showVoteCounts = false;
            int candidateCount = ElectionState.DefaultCandidateCount;
            if (!m_StateQuery.IsEmptyIgnoreFilter)
            {
                state = m_StateQuery.GetSingleton<ElectionState>();
                showVoteCounts = state.stage == ElectionCampaignStage.Voting && state.electionDayKey != 0;
                candidateCount = state.HasCandidates
                    ? state.ActiveCandidateCount
                    : ElectionState.DefaultCandidateCount;
            }

            candidateCount = ElectionState.NormalizeCandidateCount(candidateCount);
            int partyColorA = GetOverlayPartyColor(state, 0);
            int partyColorB = GetOverlayPartyColor(state, 1);
            int partyColorC = GetOverlayPartyColor(state, 2);
            int partyColorD = GetOverlayPartyColor(state, 3);

            if (showVoteCounts)
                TallyVotes(state);
            else
            {
                ClearTallies();
            }

            m_PollingPlaceSet.Clear();
            NativeList<VotingLocationMarker> markers = new NativeList<VotingLocationMarker>(128, Allocator.TempJob);
            AddPollingPlaces(m_SchoolQuery, markers, showVoteCounts, state);
            AddPollingPlaces(m_WelfareQuery, markers, showVoteCounts, state);
            AddPollingPlaces(m_AdminQuery, markers, showVoteCounts, state);
            AddPollingPlaces(m_PostQuery, markers, showVoteCounts, state);

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
                candidateCount = candidateCount,
                partyColorA = partyColorA,
                partyColorB = partyColorB,
                partyColorC = partyColorC,
                partyColorD = partyColorD,
                zoomLevel = m_CameraUpdateSystem.zoom,
                markerScale = math.clamp((Mod.m_Setting?.VotingSiteOverlayScalePercent ?? 120) / 100f, 1f, 2f),
                cameraRight = cameraRight,
                cameraUp = cameraUp,
                cameraPosition = cameraPosition
            };

            JobHandle drawHandle = drawJob.Schedule(dependencies);
            markers.Dispose(drawHandle);
            Dependency = drawHandle;
            m_OverlayRenderSystem.AddBufferWriter(Dependency);
        }

        private static int GetOverlayPartyColor(ElectionState state, int partyIndex)
        {
            if (Mod.m_Setting?.EnableParties ?? false)
            {
                int color = state.GetPartyColor(partyIndex) & 0xffffff;
                if (color != 0)
                    return color;

                return ElectionPartyTags.GetDefaultColor(partyIndex);
            }

            return GetDefaultCandidateColor(partyIndex);
        }

        private static int GetDefaultCandidateColor(int index)
        {
            switch (index)
            {
                case 0:
                    return 0xb16cff;
                case 1:
                    return 0x62d26f;
                case 2:
                    return 0xd8a720;
                case 3:
                    return 0xff6fb3;
                default:
                    return 0xdbf5ff;
            }
        }

        private EntityQuery CreatePollingPlaceQuery(ComponentType componentType)
        {
            return GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), componentType },
                None = new[]
                {
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>(),
                    ComponentType.Exclude<Owner>()
                }
            });
        }

        private void TallyVotes(ElectionState state)
        {
            int electionDayKey = state.electionDayKey;
            if (m_LastTalliedElectionDayKey != electionDayKey)
            {
                m_VoteTallies.Clear();
                m_LastTalliedElectionDayKey = electionDayKey;
                m_LastTalliedVoteArrivals = -1;
                m_LastTalliedVotesA = -1;
                m_LastTalliedVotesB = -1;
                m_LastTalliedVotesC = -1;
                m_LastTalliedVotesD = -1;
            }

            if (m_LastTalliedVoteArrivals == state.voteArrivals &&
                m_LastTalliedVotesA == state.votesA &&
                m_LastTalliedVotesB == state.votesB &&
                m_LastTalliedVotesC == state.votesC &&
                m_LastTalliedVotesD == state.votesD)
            {
                return;
            }

            m_CurrentVoteTallies.Clear();

            using (NativeArray<ElectionVoteTrip> voteTrips = m_VoteTripQuery.ToComponentDataArray<ElectionVoteTrip>(Allocator.TempJob))
            using (NativeParallelHashMap<Entity, VoteTally> tallies = new NativeParallelHashMap<Entity, VoteTally>(math.max(1, voteTrips.Length), Allocator.TempJob))
            {
                new TallyVotesByPollingPlaceJob
                {
                    voteTrips = voteTrips,
                    electionDayKey = electionDayKey,
                    tallies = tallies
                }.Schedule().Complete();

                using (NativeKeyValueArrays<Entity, VoteTally> keyValues = tallies.GetKeyValueArrays(Allocator.Temp))
                {
                    for (int i = 0; i < keyValues.Length; i++)
                    {
                        Entity pollingPlace = keyValues.Keys[i];
                        m_CurrentVoteTallies[pollingPlace] = keyValues.Values[i];
                    }
                }
            }

            foreach (KeyValuePair<Entity, VoteTally> entry in m_CurrentVoteTallies)
            {
                m_VoteTallies.TryGetValue(entry.Key, out VoteTally displayed);
                VoteTally current = entry.Value;
                displayed.votesA = math.max(displayed.votesA, current.votesA);
                displayed.votesB = math.max(displayed.votesB, current.votesB);
                displayed.votesC = math.max(displayed.votesC, current.votesC);
                displayed.votesD = math.max(displayed.votesD, current.votesD);
                m_VoteTallies[entry.Key] = displayed;
            }

            m_LastTalliedVoteArrivals = state.voteArrivals;
            m_LastTalliedVotesA = state.votesA;
            m_LastTalliedVotesB = state.votesB;
            m_LastTalliedVotesC = state.votesC;
            m_LastTalliedVotesD = state.votesD;
        }

        private void ClearTallies()
        {
            m_VoteTallies.Clear();
            m_CurrentVoteTallies.Clear();
            m_LastTalliedElectionDayKey = 0;
            m_LastTalliedVoteArrivals = -1;
            m_LastTalliedVotesA = -1;
            m_LastTalliedVotesB = -1;
            m_LastTalliedVotesC = -1;
            m_LastTalliedVotesD = -1;
        }

        private void AddPollingPlaces(EntityQuery query, NativeList<VotingLocationMarker> markers, bool showVoteCounts, ElectionState state)
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
                    {
                        m_VoteTallies.TryGetValue(entity, out tally);
                        tally = ApplyDisplayedVoteLoss(entity, tally, state);
                    }

                    markers.Add(new VotingLocationMarker
                    {
                        position = transform.m_Position,
                        votesA = tally.votesA,
                        votesB = tally.votesB,
                        votesC = tally.votesC,
                        votesD = tally.votesD
                    });
                }
            }
        }

        private static VoteTally ApplyDisplayedVoteLoss(Entity pollingPlace, VoteTally tally, ElectionState state)
        {
            if (pollingPlace != Entity.Null &&
                pollingPlace == state.voteTamperingPollingPlace &&
                (state.voteTamperingLostVotesA > 0 ||
                 state.voteTamperingLostVotesB > 0 ||
                 state.voteTamperingLostVotesC > 0 ||
                 state.voteTamperingLostVotesD > 0))
            {
                tally.votesA = math.max(0, tally.votesA - state.voteTamperingLostVotesA);
                tally.votesB = math.max(0, tally.votesB - state.voteTamperingLostVotesB);
                tally.votesC = math.max(0, tally.votesC - state.voteTamperingLostVotesC);
                tally.votesD = math.max(0, tally.votesD - state.voteTamperingLostVotesD);
            }

            return tally;
        }

        private struct VoteTally
        {
            public int votesA;
            public int votesB;
            public int votesC;
            public int votesD;
        }

        private struct VotingLocationMarker
        {
            public float3 position;
            public int votesA;
            public int votesB;
            public int votesC;
            public int votesD;
        }

        [BurstCompile]
        private struct TallyVotesByPollingPlaceJob : IJob
        {
            [ReadOnly] public NativeArray<ElectionVoteTrip> voteTrips;
            public int electionDayKey;
            public NativeParallelHashMap<Entity, VoteTally> tallies;

            public void Execute()
            {
                for (int i = 0; i < voteTrips.Length; i++)
                {
                    ElectionVoteTrip voteTrip = voteTrips[i];
                    if (!voteTrip.voted || voteTrip.electionDayKey != electionDayKey || voteTrip.pollingPlace == Entity.Null)
                        continue;

                    tallies.TryGetValue(voteTrip.pollingPlace, out VoteTally tally);
                    switch (voteTrip.chosenCandidate)
                    {
                        case 0:
                            tally.votesA++;
                            break;
                        case 1:
                            tally.votesB++;
                            break;
                        case 2:
                            tally.votesC++;
                            break;
                        case 3:
                            tally.votesD++;
                            break;
                    }

                    tallies[voteTrip.pollingPlace] = tally;
                }
            }
        }

        [BurstCompile]
        private struct DrawVotingLocationOverlayJob : IJob
        {
            public OverlayRenderSystem.Buffer overlayBuffer;
            [ReadOnly] public NativeArray<VotingLocationMarker> markers;
            public bool showVoteCounts;
            public int candidateCount;
            public int partyColorA;
            public int partyColorB;
            public int partyColorC;
            public int partyColorD;
            public float zoomLevel;
            public float markerScale;
            public float3 cameraRight;
            public float3 cameraUp;
            public float3 cameraPosition;

            public void Execute()
            {
                float rawZoom = math.clamp((zoomLevel - 900f) / 12500f, 0f, 1f);
                float normalizedZoom = math.pow(rawZoom, 0.68f);
                const float visualScale = 1.265f;
                float thickness = math.lerp(3.0f, 21.0f, normalizedZoom) * visualScale;
                float markerThickness = thickness * markerScale;
                float badgeSize = thickness * 3.3f * markerScale;
                float markerHeight = math.lerp(14f, 54f, normalizedZoom);
                float anchorHeight = math.lerp(2.0f, 8.0f, normalizedZoom);
                float cameraNudge = math.lerp(8f, 26f, normalizedZoom);
                float sideOffset = badgeSize * 1.65f + thickness * 2.2f;
                float overlapRadius = badgeSize * 2.25f;
                float stackStep = badgeSize * 1.18f;

                float3 right = math.normalizesafe(cameraRight, new float3(1f, 0f, 0f));
                float3 up = math.normalizesafe(cameraUp, new float3(0f, 1f, 0f));
                float3 worldUp = new float3(0f, 1f, 0f);
                NativeList<float3> drawnBadgeCenters = new NativeList<float3>(markers.Length, Allocator.Temp);

                for (int i = 0; i < markers.Length; i++)
                {
                    VotingLocationMarker marker = markers[i];
                    float3 anchor = marker.position + worldUp * anchorHeight;
                    float3 toCamera = math.normalizesafe(cameraPosition - anchor, up);
                    float3 center = anchor + worldUp * markerHeight + right * sideOffset + toCamera * cameraNudge;
                    center = ResolveBadgeOverlap(center, drawnBadgeCenters, overlapRadius, stackStep, up);
                    drawnBadgeCenters.Add(center);

                    DrawLeader(anchor + toCamera * (thickness * 0.6f), center, badgeSize, thickness, right);
                    DrawLocationAnchor(anchor + toCamera * (thickness * 0.65f), thickness, right);

                    DrawBallotMarker(
                        center,
                        badgeSize,
                        markerThickness,
                        right,
                        up,
                        marker.votesA,
                        marker.votesB,
                        marker.votesC,
                        marker.votesD,
                        candidateCount,
                        showVoteCounts);

                    if (showVoteCounts)
                    {
                        float3 labelCenter = center - up * (badgeSize * 1.25f);
                        DrawVoteSplitIndicator(
                            labelCenter,
                            GetVoteTotal(marker.votesA, marker.votesB, marker.votesC, marker.votesD, candidateCount),
                            new Color(0.05f, 0.10f, 0.17f, 1f),
                            new Color(0.94f, 0.98f, 1f, 1f),
                            markerThickness,
                            right,
                            up);
                    }
                }

                drawnBadgeCenters.Dispose();
            }

            private float3 ResolveBadgeOverlap(float3 center, NativeList<float3> drawnBadgeCenters, float overlapRadius, float stackStep, float3 up)
            {
                const int maxStackSteps = 8;
                float3 candidate = center;
                for (int stack = 0; stack < maxStackSteps; stack++)
                {
                    bool overlaps = false;
                    for (int i = 0; i < drawnBadgeCenters.Length; i++)
                    {
                        if (math.distance(candidate, drawnBadgeCenters[i]) < overlapRadius)
                        {
                            overlaps = true;
                            break;
                        }
                    }

                    if (!overlaps)
                        return candidate;

                    candidate += up * stackStep;
                }

                return candidate;
            }

            private void DrawLeader(float3 anchor, float3 center, float badgeSize, float thickness, float3 right)
            {
                float3 markerEdge = center - right * (badgeSize * 1.05f);
                Color shadow = new Color(0.00f, 0.02f, 0.05f, 0.86f);
                Color line = new Color(0.86f, 0.96f, 1f, 0.92f);
                overlayBuffer.DrawLine(shadow, new Line3.Segment(anchor, markerEdge), thickness * 0.52f);
                overlayBuffer.DrawLine(line, new Line3.Segment(anchor, markerEdge), thickness * 0.28f);
            }

            private void DrawLocationAnchor(float3 center, float thickness, float3 right)
            {
                DrawDisc(center, thickness * 2.55f, right, new Color(0.00f, 0.02f, 0.05f, 0.90f));
                DrawDisc(center, thickness * 1.55f, right, new Color(0.86f, 0.96f, 1f, 0.95f));
            }

            private void DrawBallotMarker(float3 center, float size, float thickness, float3 right, float3 up, int votesA, int votesB, int votesC, int votesD, int candidateCount, bool showVoteCounts)
            {
                Color shadow = new Color(0.01f, 0.04f, 0.09f, 1f);
                Color navy = new Color(0.04f, 0.10f, 0.17f, 1f);
                Color paper = new Color(0.96f, 0.99f, 1f, 1f);
                Color paperLine = new Color(0.70f, 0.80f, 0.86f, 1f);
                Color box = new Color(0.08f, 0.45f, 0.50f, 1f);
                Color boxLight = new Color(0.82f, 0.96f, 1f, 1f);
                Color check = new Color(0.04f, 0.45f, 0.67f, 1f);

                DrawDisc(center - up * (thickness * 0.06f), size * 2.06f, right, shadow);
                DrawDisc(center, size * 1.56f, right, navy);
                DrawVoteRing(center, size * 0.92f, thickness * 0.64f, votesA, votesB, votesC, votesD, candidateCount, showVoteCounts, right, up);

                float paperWidth = size * 0.56f;
                float paperHeight = size * 0.72f;
                float3 paperCenter = center + up * (size * 0.10f);
                DrawPill(paperCenter, paperWidth, paperHeight, thickness * 0.08f, right, navy);
                DrawPill(paperCenter, paperWidth, paperHeight, 0f, right, paper);

                float boxWidth = size * 0.76f;
                float boxHeight = size * 0.42f;
                float3 boxCenter = center - up * (size * 0.34f);
                DrawPill(boxCenter, boxWidth, boxHeight, thickness * 0.12f, right, navy);
                DrawPill(boxCenter, boxWidth, boxHeight, 0f, right, box);

                float3 slotCenter = boxCenter + up * (boxHeight * 0.44f);
                overlayBuffer.DrawLine(boxLight, new Line3.Segment(slotCenter - right * (boxWidth * 0.22f), slotCenter + right * (boxWidth * 0.22f)), thickness * 0.14f);

                float3 lineA = paperCenter + up * (paperHeight * 0.24f);
                float3 lineB = paperCenter + up * (paperHeight * 0.02f);
                overlayBuffer.DrawLine(paperLine, new Line3.Segment(lineA - right * (paperWidth * 0.25f), lineA + right * (paperWidth * 0.25f)), thickness * 0.08f);
                overlayBuffer.DrawLine(paperLine, new Line3.Segment(lineB - right * (paperWidth * 0.25f), lineB + right * (paperWidth * 0.12f)), thickness * 0.08f);

                float3 checkStart = paperCenter - right * (paperWidth * 0.23f) - up * (paperHeight * 0.07f);
                float3 checkMiddle = paperCenter - right * (paperWidth * 0.06f) - up * (paperHeight * 0.24f);
                float3 checkEnd = paperCenter + right * (paperWidth * 0.29f) + up * (paperHeight * 0.18f);
                overlayBuffer.DrawLine(check, new Line3.Segment(checkStart, checkMiddle), thickness * 0.18f);
                overlayBuffer.DrawLine(check, new Line3.Segment(checkMiddle, checkEnd), thickness * 0.18f);
            }

            private void DrawVoteRing(float3 center, float radius, float ringThickness, int votesA, int votesB, int votesC, int votesD, int candidateCount, bool showVoteCounts, float3 right, float3 up)
            {
                Color track = new Color(0.11f, 0.17f, 0.25f, 1f);
                int activeCandidateCount = math.clamp(candidateCount, ElectionState.MinCandidateCount, ElectionState.MaxCandidateCount);
                int total = GetVoteTotal(votesA, votesB, votesC, votesD, activeCandidateCount);
                int segments = 64;
                float tau = math.PI * 2f;
                float startAngle = -math.PI * 0.56f;

                for (int i = 0; i < segments; i++)
                {
                    float normalizedStart = i / (float)segments;
                    float normalizedMid = (i + 0.5f) / segments;
                    float angle0 = startAngle + tau * normalizedStart;
                    float angle1 = startAngle + tau * ((i + 0.78f) / segments);
                    float3 start = center + right * (math.cos(angle0) * radius) + up * (math.sin(angle0) * radius);
                    float3 end = center + right * (math.cos(angle1) * radius) + up * (math.sin(angle1) * radius);
                    Color color = GetVoteRingColor(normalizedMid, votesA, votesB, votesC, votesD, activeCandidateCount, total, showVoteCounts, track);
                    overlayBuffer.DrawLine(color, new Line3.Segment(start, end), ringThickness);
                }
            }

            private Color GetVoteRingColor(float normalizedPosition, int votesA, int votesB, int votesC, int votesD, int candidateCount, int total, bool showVoteCounts, Color track)
            {
                if (candidateCount <= 0)
                    return track;

                if (!showVoteCounts || total <= 0)
                {
                    int equalIndex = math.min(candidateCount - 1, (int)math.floor(normalizedPosition * candidateCount));
                    return GetCandidateColor(equalIndex);
                }

                int runningVotes = 0;
                for (int i = 0; i < candidateCount; i++)
                {
                    runningVotes += GetCandidateVoteCount(i, votesA, votesB, votesC, votesD);
                    if (runningVotes > 0 && normalizedPosition <= math.saturate(runningVotes / (float)total))
                        return GetCandidateColor(i);
                }

                return GetCandidateColor(candidateCount - 1);
            }

            private int GetVoteTotal(int votesA, int votesB, int votesC, int votesD, int candidateCount)
            {
                int total = math.max(0, votesA) + math.max(0, votesB);
                if (candidateCount > 2)
                    total += math.max(0, votesC);
                if (candidateCount > 3)
                    total += math.max(0, votesD);
                return math.max(0, total);
            }

            private int GetCandidateVoteCount(int index, int votesA, int votesB, int votesC, int votesD)
            {
                switch (index)
                {
                    case 0:
                        return math.max(0, votesA);
                    case 1:
                        return math.max(0, votesB);
                    case 2:
                        return math.max(0, votesC);
                    case 3:
                        return math.max(0, votesD);
                    default:
                        return 0;
                }
            }

            private Color GetCandidateColor(int index)
            {
                switch (index)
                {
                    case 0:
                        return PackedColorToColor(partyColorA);
                    case 1:
                        return PackedColorToColor(partyColorB);
                    case 2:
                        return PackedColorToColor(partyColorC);
                    case 3:
                        return PackedColorToColor(partyColorD);
                    default:
                        return new Color(0.86f, 0.96f, 1f, 1f);
                }
            }

            private static Color PackedColorToColor(int packedColor)
            {
                packedColor &= 0xffffff;
                if (packedColor == 0)
                    return new Color(0.86f, 0.96f, 1f, 1f);

                return new Color(
                    ((packedColor >> 16) & 0xff) / 255f,
                    ((packedColor >> 8) & 0xff) / 255f,
                    (packedColor & 0xff) / 255f,
                    1f);
            }

            private void DrawDisc(float3 center, float diameter, float3 right, Color color)
            {
                float halfWidth = math.max(0.01f, diameter * 0.015f);
                overlayBuffer.DrawLine(color, new Line3.Segment(center - right * halfWidth, center + right * halfWidth), diameter);
            }

            private void DrawPill(float3 center, float width, float height, float outlinePadding, float3 right, Color color)
            {
                float3 start = center - right * (width * 0.5f);
                float3 end = center + right * (width * 0.5f);
                overlayBuffer.DrawLine(color, new Line3.Segment(start, end), height + outlinePadding);
            }

            private void DrawVoteSplitIndicator(float3 center, int voteTotal, Color bgColor, Color textColor, float thickness, float3 right, float3 up)
            {
                int number = math.max(0, voteTotal);
                number = math.max(0, number);
                int digitsCount = GetDigitCount(number);
                int tempNum = number;

                float digitWidth = thickness * 1.05f;
                float digitHeight = thickness * 1.78f;
                float spacing = thickness * 0.42f;
                float totalWidth = digitsCount * digitWidth + math.max(0, digitsCount - 1) * spacing;
                float bgWidth = totalWidth + thickness * 4.0f;

                overlayBuffer.DrawLine(new Color(0.00f, 0.03f, 0.07f, 1f), new Line3.Segment(center - right * (bgWidth * 0.52f), center + right * (bgWidth * 0.52f)), digitHeight + thickness * 1.92f);
                overlayBuffer.DrawLine(bgColor, new Line3.Segment(center - right * (bgWidth * 0.5f), center + right * (bgWidth * 0.5f)), digitHeight + thickness * 1.62f);

                float3 cursor = center + right * (totalWidth * 0.5f - digitWidth * 0.5f) - up * (thickness * 0.18f);
                for (int i = 0; i < digitsCount; i++)
                {
                    DrawDigit(cursor, tempNum % 10, textColor, digitWidth, digitHeight, thickness * 0.32f, right, up);
                    tempNum /= 10;
                    cursor -= right * (digitWidth + spacing);
                }
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
                    overlayBuffer.DrawLine(GetCandidateColor(0), new Line3.Segment(left, split), height);

                if (votesB > 0)
                    overlayBuffer.DrawLine(GetCandidateColor(1), new Line3.Segment(split, end), height);
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
