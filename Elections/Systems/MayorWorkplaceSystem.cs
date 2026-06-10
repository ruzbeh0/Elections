using Elections.Components;
using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.Tools;
using Game.Zones;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Elections.Systems
{
    public partial class MayorWorkplaceSystem : GameSystemBase
    {
        public const int MaxMayorHomeChoices = 512;
        public const int MaxMayorWorkplaceChoices = 64;

        private EntityQuery m_StateQuery;
        private EntityQuery m_CityHallQuery;
        private EntityQuery m_LowDensityResidentialQuery;
        private EntityQuery m_SignatureResidentialQuery;
        private PrefabSystem m_PrefabSystem;
        private EntityArchetype m_RentEventArchetype;
        private Entity m_AssignedMayor;
        private Entity m_AssignedHome;
        private Entity m_AssignedWorkplace;
        private bool m_LoggedNoCityHall;
        private bool m_LoggedNoMayorHome;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 512;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_StateQuery = GetEntityQuery(ComponentType.ReadWrite<ElectionState>());
            m_CityHallQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<AdminBuilding>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<WorkProvider>(),
                    ComponentType.ReadWrite<Employee>()
                },
                None = new[]
                {
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Destroyed>(),
                    ComponentType.Exclude<Temp>()
                }
            });
            m_LowDensityResidentialQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<ResidentialProperty>(),
                    ComponentType.ReadOnly<PrefabRef>()
                },
                None = new[]
                {
                    ComponentType.Exclude<Abandoned>(),
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Destroyed>(),
                    ComponentType.Exclude<Temp>()
                }
            });
            m_SignatureResidentialQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<Signature>(),
                    ComponentType.ReadOnly<ResidentialProperty>(),
                    ComponentType.ReadOnly<PrefabRef>()
                },
                None = new[]
                {
                    ComponentType.Exclude<Abandoned>(),
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Destroyed>(),
                    ComponentType.Exclude<Temp>()
                }
            });
            m_RentEventArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<Game.Common.Event>(),
                ComponentType.ReadWrite<RentersUpdated>());
        }

        protected override void OnUpdate()
        {
            if (Mod.m_Setting == null || !Mod.m_Setting.EnableElections || m_StateQuery.IsEmptyIgnoreFilter)
                return;

            Entity stateEntity = m_StateQuery.GetSingletonEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            bool stateChanged = EnsureMayorTargets(ref state);

            Entity mayor = state.mayor;
            if (!IsValidCitizen(mayor))
            {
                if (m_AssignedMayor != Entity.Null)
                {
                    ElectionDebug.Log($"Mayor assignments cleared: assigned mayor {FormatEntity(m_AssignedMayor)} is no longer the current valid mayor.");
                    MakeUnemployed(m_AssignedMayor);
                    m_AssignedMayor = Entity.Null;
                    m_AssignedHome = Entity.Null;
                    m_AssignedWorkplace = Entity.Null;
                }

                if (stateChanged)
                    EntityManager.SetComponentData(stateEntity, state);

                return;
            }

            if (m_AssignedMayor != Entity.Null && m_AssignedMayor != mayor)
            {
                ElectionDebug.Log($"Mayor changed: previous mayor {FormatEntity(m_AssignedMayor)} is being made unemployed before assigning {FormatEntity(mayor)}.");
                MakeUnemployed(m_AssignedMayor);
            }

            Entity previousMayor = m_AssignedMayor;
            Entity previousHome = m_AssignedHome;
            Entity previousWorkplace = m_AssignedWorkplace;
            bool assignedHome = false;
            bool assignedWorkplace = false;

            if (state.mayorHome != Entity.Null)
            {
                assignedHome = AssignMayorToHome(mayor, state.mayorHome);
                if (assignedHome)
                {
                    m_AssignedHome = state.mayorHome;
                    m_LoggedNoMayorHome = false;
                }
            }
            else if (!m_LoggedNoMayorHome)
            {
                m_LoggedNoMayorHome = true;
                ElectionDebug.Log($"Mayor home assignment skipped: no low-density residential building was found for mayor {FormatEntity(mayor)}.");
            }

            if (state.mayorWorkplace != Entity.Null)
            {
                assignedWorkplace = AssignMayorToCityHall(mayor, state.mayorWorkplace);
                if (assignedWorkplace)
                {
                    m_AssignedWorkplace = state.mayorWorkplace;
                    m_LoggedNoCityHall = false;
                }
            }
            else if (!m_LoggedNoCityHall)
            {
                m_LoggedNoCityHall = true;
                ElectionDebug.Log($"Mayor workplace assignment skipped: no City Hall admin building was found for mayor {FormatEntity(mayor)}.");
            }

            if (assignedHome || assignedWorkplace)
                m_AssignedMayor = mayor;

            if (assignedHome && (previousMayor != mayor || previousHome != state.mayorHome))
                ElectionDebug.Log($"Mayor home assigned: mayor={FormatEntity(mayor)}, home={FormatEntity(state.mayorHome)}.");

            if (assignedWorkplace && (previousMayor != mayor || previousWorkplace != state.mayorWorkplace))
                ElectionDebug.Log($"Mayor workplace assigned: mayor={FormatEntity(mayor)}, cityHall={FormatEntity(state.mayorWorkplace)}.");

            if (stateChanged)
                EntityManager.SetComponentData(stateEntity, state);
        }

        public bool TrySetMayorHome(Entity home)
        {
            if (!IsValidMayorHome(home) || !TryGetStateEntity(out Entity stateEntity))
                return false;

            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (state.mayorHome == home)
                return true;

            state.mayorHome = home;
            EntityManager.SetComponentData(stateEntity, state);
            ElectionDebug.Log($"Mayor home selection changed: home={FormatEntity(home)}.");
            return true;
        }

        public bool TrySetMayorWorkplace(Entity workplace)
        {
            if (!IsValidMayorWorkplace(workplace) || !TryGetStateEntity(out Entity stateEntity))
                return false;

            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (state.mayorWorkplace == workplace)
                return true;

            state.mayorWorkplace = workplace;
            EntityManager.SetComponentData(stateEntity, state);
            ElectionDebug.Log($"Mayor workplace selection changed: cityHall={FormatEntity(workplace)}.");
            return true;
        }

        public Entity GetEffectiveMayorHome(ElectionState state)
        {
            if (IsValidMayorHome(state.mayorHome))
                return state.mayorHome;

            return TryFindDefaultMayorHome(out Entity home) ? home : Entity.Null;
        }

        public Entity GetEffectiveMayorWorkplace(ElectionState state)
        {
            if (IsValidMayorWorkplace(state.mayorWorkplace))
                return state.mayorWorkplace;

            return TryFindDefaultMayorWorkplace(out Entity workplace) ? workplace : Entity.Null;
        }

        public Entity GetCurrentMayorHome(Entity mayor)
        {
            if (!IsValidCitizen(mayor) ||
                !TryGetComponentData(mayor, out HouseholdMember householdMember))
            {
                return Entity.Null;
            }

            return GetHouseholdHome(householdMember.m_Household);
        }

        public Entity GetCurrentMayorWorkplace(Entity mayor)
        {
            if (!IsValidCitizen(mayor) ||
                !TryGetComponentData(mayor, out Worker worker))
            {
                return Entity.Null;
            }

            if (TryGetComponentData(worker.m_Workplace, out PropertyRenter renter) &&
                renter.m_Property != Entity.Null &&
                EntityManager.Exists(renter.m_Property))
            {
                return renter.m_Property;
            }

            return worker.m_Workplace;
        }

        public int GetHomeCapacity(Entity home)
        {
            if (!EntityManager.Exists(home) ||
                !TryGetComponentData(home, out PrefabRef prefabRef) ||
                !TryGetComponentData(prefabRef.m_Prefab, out BuildingPropertyData propertyData))
            {
                return 0;
            }

            return math.max(0, PropertyUtils.GetResidentialProperties(propertyData));
        }

        public int GetHomeOccupantCount(Entity home)
        {
            if (!EntityManager.Exists(home) || !EntityManager.HasBuffer<Renter>(home))
                return 0;

            DynamicBuffer<Renter> renters = EntityManager.GetBuffer<Renter>(home);
            int count = 0;
            for (int i = 0; i < renters.Length; i++)
            {
                if (EntityManager.HasComponent<Household>(renters[i].m_Renter))
                    count++;
            }

            return count;
        }

        public int GetWorkplaceCapacity(Entity workplace)
        {
            return EntityManager.Exists(workplace) && TryGetComponentData(workplace, out WorkProvider provider)
                ? math.max(0, provider.m_MaxWorkers)
                : 0;
        }

        public int GetWorkplaceOccupantCount(Entity workplace)
        {
            return EntityManager.Exists(workplace) && EntityManager.HasBuffer<Employee>(workplace)
                ? EntityManager.GetBuffer<Employee>(workplace).Length
                : 0;
        }

        public bool IsValidMayorHome(Entity home)
        {
            if (home == Entity.Null ||
                !EntityManager.Exists(home) ||
                EntityManager.HasComponent<Abandoned>(home) ||
                EntityManager.HasComponent<Deleted>(home) ||
                EntityManager.HasComponent<Destroyed>(home) ||
                EntityManager.HasComponent<Temp>(home) ||
                !EntityManager.HasComponent<Building>(home) ||
                !EntityManager.HasComponent<ResidentialProperty>(home) ||
                !TryGetComponentData(home, out PrefabRef prefabRef) ||
                !TryGetComponentData(prefabRef.m_Prefab, out BuildingPropertyData propertyData))
            {
                return false;
            }

            if (IsResidentialSignatureHome(home, propertyData))
                return true;

            return IsLowDensityResidentialPrefab(prefabRef.m_Prefab, propertyData);
        }

        public bool IsValidMayorWorkplace(Entity workplace)
        {
            if (workplace == Entity.Null ||
                !EntityManager.Exists(workplace) ||
                EntityManager.HasComponent<Deleted>(workplace) ||
                EntityManager.HasComponent<Destroyed>(workplace) ||
                EntityManager.HasComponent<Temp>(workplace) ||
                !EntityManager.HasComponent<Building>(workplace) ||
                !EntityManager.HasComponent<AdminBuilding>(workplace) ||
                !EntityManager.HasComponent<WorkProvider>(workplace) ||
                !EntityManager.HasBuffer<Employee>(workplace) ||
                !TryGetComponentData(workplace, out PrefabRef prefabRef))
            {
                return false;
            }

            return IsCityHallPrefab(prefabRef.m_Prefab);
        }

        public bool BuildMayorHomeChoices(List<Entity> choices, ElectionState state, Entity currentHome, Entity selectedBuilding)
        {
            choices.Clear();
            AddMayorHomeChoice(choices, GetEffectiveMayorHome(state));
            AddMayorHomeChoice(choices, currentHome);
            AddMayorHomeChoice(choices, selectedBuilding);

            bool limited = AddMayorHomeChoicesFromQuery(choices, m_SignatureResidentialQuery);
            return limited || AddMayorHomeChoicesFromQuery(choices, m_LowDensityResidentialQuery);
        }

        public bool BuildMayorWorkplaceChoices(List<Entity> choices, ElectionState state, Entity currentWorkplace, Entity selectedBuilding)
        {
            choices.Clear();
            AddMayorWorkplaceChoice(choices, GetEffectiveMayorWorkplace(state));
            AddMayorWorkplaceChoice(choices, currentWorkplace);
            AddMayorWorkplaceChoice(choices, selectedBuilding);

            if (m_CityHallQuery.IsEmptyIgnoreFilter)
                return false;

            using (NativeArray<Entity> entities = m_CityHallQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    if (!IsValidMayorWorkplace(entity) || choices.Contains(entity))
                        continue;

                    if (choices.Count >= MaxMayorWorkplaceChoices)
                        return true;

                    choices.Add(entity);
                }
            }

            return false;
        }

        private bool AddMayorHomeChoice(List<Entity> choices, Entity home)
        {
            if (choices.Count >= MaxMayorHomeChoices ||
                choices.Contains(home) ||
                !IsValidMayorHome(home))
            {
                return false;
            }

            choices.Add(home);
            return true;
        }

        private bool AddMayorWorkplaceChoice(List<Entity> choices, Entity workplace)
        {
            if (choices.Count >= MaxMayorWorkplaceChoices ||
                choices.Contains(workplace) ||
                !IsValidMayorWorkplace(workplace))
            {
                return false;
            }

            choices.Add(workplace);
            return true;
        }

        private bool AddMayorHomeChoicesFromQuery(List<Entity> choices, EntityQuery query)
        {
            if (query.IsEmptyIgnoreFilter)
                return false;

            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    if (!IsValidMayorHome(entity) || choices.Contains(entity))
                        continue;

                    if (choices.Count >= MaxMayorHomeChoices)
                        return true;

                    choices.Add(entity);
                }
            }

            return false;
        }

        private bool IsResidentialSignatureHome(Entity home, BuildingPropertyData propertyData)
        {
            return EntityManager.HasComponent<Signature>(home) &&
                   EntityManager.HasComponent<ResidentialProperty>(home) &&
                   PropertyUtils.GetResidentialProperties(propertyData) > 0;
        }

        private bool IsLowDensityResidentialPrefab(Entity prefab, BuildingPropertyData propertyData)
        {
            if (PropertyUtils.GetResidentialProperties(propertyData) <= 0)
                return false;

            if (TryGetComponentData(prefab, out SpawnableBuildingData spawnableBuildingData) &&
                TryGetComponentData(spawnableBuildingData.m_ZonePrefab, out ZoneData zoneData) &&
                TryGetComponentData(spawnableBuildingData.m_ZonePrefab, out ZonePropertiesData zonePropertiesData) &&
                PropertyUtils.GetZoneDensity(zoneData, zonePropertiesData) == ZoneDensity.Low)
            {
                if (zoneData.m_AreaType == AreaType.Residential)
                    return true;

                return EntityManager.Exists(prefab) && EntityManager.HasComponent<SignatureBuildingData>(prefab);
            }

            return IsLowDensityResidentialSignaturePrefab(prefab);
        }

        private bool IsLowDensityResidentialSignaturePrefab(Entity prefab)
        {
            if (prefab == Entity.Null ||
                !EntityManager.Exists(prefab) ||
                !EntityManager.HasComponent<SignatureBuildingData>(prefab))
            {
                return false;
            }

            string prefabName = m_PrefabSystem.GetPrefabName(prefab);
            if (string.IsNullOrWhiteSpace(prefabName))
                return false;

            string normalizedName = prefabName
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty);

            return normalizedName.IndexOf("LowDensityResidential", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalizedName.IndexOf("ResidentialLowDensity", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryGetStateEntity(out Entity stateEntity)
        {
            stateEntity = Entity.Null;
            if (m_StateQuery.IsEmptyIgnoreFilter)
                return false;

            stateEntity = m_StateQuery.GetSingletonEntity();
            return stateEntity != Entity.Null && EntityManager.Exists(stateEntity);
        }

        private bool EnsureMayorTargets(ref ElectionState state)
        {
            bool changed = false;

            if (!IsValidMayorHome(state.mayorHome))
            {
                Entity previousHome = state.mayorHome;
                state.mayorHome = TryFindDefaultMayorHome(out Entity home) ? home : Entity.Null;
                changed |= previousHome != state.mayorHome;
            }

            if (!IsValidMayorWorkplace(state.mayorWorkplace))
            {
                Entity previousWorkplace = state.mayorWorkplace;
                state.mayorWorkplace = TryFindDefaultMayorWorkplace(out Entity workplace) ? workplace : Entity.Null;
                changed |= previousWorkplace != state.mayorWorkplace;
            }

            return changed;
        }

        private bool AssignMayorToHome(Entity mayor, Entity home)
        {
            if (!IsValidCitizen(mayor) ||
                !IsValidMayorHome(home) ||
                !TryGetComponentData(mayor, out HouseholdMember householdMember))
            {
                return false;
            }

            Entity household = householdMember.m_Household;
            if (household == Entity.Null ||
                !EntityManager.Exists(household) ||
                !EntityManager.HasComponent<Household>(household))
            {
                return false;
            }

            Entity oldHome = GetHouseholdHome(household);
            if (oldHome == home)
            {
                EnsureRenter(home, household);
                return true;
            }

            EnsureHomeSpace(home, household);

            if (GetHomeCapacity(home) > 0 && GetHomeOccupantCount(home) >= GetHomeCapacity(home) && !HasRenter(home, household))
            {
                ElectionDebug.Log($"Mayor home assignment blocked: target home {FormatEntity(home)} is still full after eviction attempt.");
                return false;
            }

            if (oldHome != Entity.Null && oldHome != home)
            {
                ElectionDebug.Log($"Mayor home reassignment: removing mayor household {FormatEntity(household)} from old home {FormatEntity(oldHome)}.");
                RemoveRenter(oldHome, household, markAvailable: true, makeHomeless: false);
            }

            int rent = 0;
            if (TryGetComponentData(home, out PropertyOnMarket onMarket))
                rent = onMarket.m_AskingRent;

            PropertyRenter renter = new PropertyRenter
            {
                m_Property = home,
                m_Rent = rent
            };

            if (EntityManager.HasComponent<PropertyRenter>(household))
                EntityManager.SetComponentData(household, renter);
            else
                EntityManager.AddComponentData(household, renter);

            if (EntityManager.HasComponent<HomelessHousehold>(household))
                EntityManager.RemoveComponent<HomelessHousehold>(household);

            EnsureRenter(home, household);
            UpdateHomeMarketState(home);
            AddRentersUpdated(home);
            return true;
        }

        private bool AssignMayorToCityHall(Entity mayor, Entity cityHall)
        {
            if (!IsValidCitizen(mayor) ||
                !IsValidMayorWorkplace(cityHall))
            {
                return false;
            }

            byte workerLevel = GetWorkerLevel(mayor);
            Workshift shift = Workshift.Day;
            float lastCommuteTime = 0f;
            Entity oldWorkplace = Entity.Null;

            if (EntityManager.HasComponent<Worker>(mayor))
            {
                Worker existingWorker = EntityManager.GetComponentData<Worker>(mayor);
                oldWorkplace = existingWorker.m_Workplace;
                workerLevel = existingWorker.m_Level;
                shift = existingWorker.m_Shift;
                lastCommuteTime = existingWorker.m_LastCommuteTime;
            }

            if (oldWorkplace != Entity.Null && oldWorkplace != cityHall)
            {
                ElectionDebug.Log($"Mayor workplace reassignment: removing mayor {FormatEntity(mayor)} from old workplace {FormatEntity(oldWorkplace)}.");
                RemoveEmployee(oldWorkplace, mayor);
            }

            EnsureWorkplaceSpace(cityHall, mayor);

            Worker worker = new Worker
            {
                m_Workplace = cityHall,
                m_LastCommuteTime = lastCommuteTime,
                m_Level = workerLevel,
                m_Shift = shift
            };

            if (EntityManager.HasComponent<Worker>(mayor))
                EntityManager.SetComponentData(mayor, worker);
            else
                EntityManager.AddComponentData(mayor, worker);

            EnsureEmployee(cityHall, mayor, workerLevel);
            return true;
        }

        private void EnsureHomeSpace(Entity home, Entity protectedHousehold)
        {
            int capacity = GetHomeCapacity(home);
            if (capacity <= 0 || !EntityManager.HasBuffer<Renter>(home) || HasRenter(home, protectedHousehold))
                return;

            while (GetHomeOccupantCount(home) >= capacity)
            {
                Entity evictedHousehold = FindEvictableHousehold(home, protectedHousehold);
                if (evictedHousehold == Entity.Null)
                    return;

                ElectionDebug.Log($"Mayor home assignment evicting household {FormatEntity(evictedHousehold)} from home {FormatEntity(home)}.");
                RemoveRenter(home, evictedHousehold, markAvailable: false, makeHomeless: true);
            }
        }

        private Entity FindEvictableHousehold(Entity home, Entity protectedHousehold)
        {
            DynamicBuffer<Renter> renters = EntityManager.GetBuffer<Renter>(home);
            for (int i = renters.Length - 1; i >= 0; i--)
            {
                Entity renter = renters[i].m_Renter;
                if (renter != protectedHousehold && EntityManager.HasComponent<Household>(renter))
                    return renter;
            }

            return Entity.Null;
        }

        private Entity GetHouseholdHome(Entity household)
        {
            if (household == Entity.Null ||
                !EntityManager.Exists(household) ||
                !TryGetComponentData(household, out PropertyRenter propertyRenter))
            {
                return Entity.Null;
            }

            return propertyRenter.m_Property != Entity.Null && EntityManager.Exists(propertyRenter.m_Property)
                ? propertyRenter.m_Property
                : Entity.Null;
        }

        private bool HasRenter(Entity property, Entity renter)
        {
            if (property == Entity.Null ||
                !EntityManager.Exists(property) ||
                !EntityManager.HasBuffer<Renter>(property))
            {
                return false;
            }

            DynamicBuffer<Renter> renters = EntityManager.GetBuffer<Renter>(property);
            for (int i = 0; i < renters.Length; i++)
            {
                if (renters[i].m_Renter == renter)
                    return true;
            }

            return false;
        }

        private void EnsureRenter(Entity property, Entity renter)
        {
            DynamicBuffer<Renter> renters = EntityManager.HasBuffer<Renter>(property)
                ? EntityManager.GetBuffer<Renter>(property)
                : EntityManager.AddBuffer<Renter>(property);
            for (int i = 0; i < renters.Length; i++)
            {
                if (renters[i].m_Renter == renter)
                    return;
            }

            renters.Add(new Renter
            {
                m_Renter = renter
            });
        }

        private void RemoveRenter(Entity property, Entity renter, bool markAvailable, bool makeHomeless)
        {
            if (property != Entity.Null &&
                EntityManager.Exists(property) &&
                EntityManager.HasBuffer<Renter>(property))
            {
                DynamicBuffer<Renter> renters = EntityManager.GetBuffer<Renter>(property);
                for (int i = renters.Length - 1; i >= 0; i--)
                {
                    if (renters[i].m_Renter == renter)
                        renters.RemoveAt(i);
                }

                if (markAvailable)
                    MarkPropertyAvailable(property);

                AddRentersUpdated(property);
            }

            if (EntityManager.Exists(renter) &&
                TryGetComponentData(renter, out PropertyRenter propertyRenter) &&
                propertyRenter.m_Property == property)
            {
                EntityManager.RemoveComponent<PropertyRenter>(renter);
            }

            if (makeHomeless &&
                EntityManager.Exists(renter) &&
                EntityManager.HasComponent<Household>(renter) &&
                !EntityManager.HasComponent<HomelessHousehold>(renter))
            {
                EntityManager.AddComponentData(renter, new HomelessHousehold());
            }
        }

        private void MarkPropertyAvailable(Entity property)
        {
            if (property == Entity.Null ||
                !EntityManager.Exists(property) ||
                !EntityManager.HasComponent<Building>(property) ||
                EntityManager.HasComponent<PropertyOnMarket>(property) ||
                EntityManager.HasComponent<PropertyToBeOnMarket>(property))
            {
                return;
            }

            EntityManager.AddComponentData(property, new PropertyToBeOnMarket());
        }

        private void UpdateHomeMarketState(Entity home)
        {
            int capacity = GetHomeCapacity(home);
            int occupants = GetHomeOccupantCount(home);
            if (capacity <= 0 || occupants >= capacity)
            {
                if (EntityManager.HasComponent<PropertyOnMarket>(home))
                    EntityManager.RemoveComponent<PropertyOnMarket>(home);
                if (EntityManager.HasComponent<PropertyToBeOnMarket>(home))
                    EntityManager.RemoveComponent<PropertyToBeOnMarket>(home);
            }
            else
            {
                MarkPropertyAvailable(home);
            }
        }

        private void AddRentersUpdated(Entity property)
        {
            if (property == Entity.Null || !EntityManager.Exists(property))
                return;

            Entity rentEvent = EntityManager.CreateEntity(m_RentEventArchetype);
            EntityManager.SetComponentData(rentEvent, new RentersUpdated(property));
        }

        private void EnsureWorkplaceSpace(Entity workplace, Entity protectedWorker)
        {
            if (!EntityManager.Exists(workplace) ||
                !EntityManager.HasBuffer<Employee>(workplace) ||
                !TryGetComponentData(workplace, out WorkProvider provider) ||
                provider.m_MaxWorkers <= 0 ||
                HasEmployee(workplace, protectedWorker))
            {
                return;
            }

            DynamicBuffer<Employee> employees = EntityManager.GetBuffer<Employee>(workplace);
            while (employees.Length >= provider.m_MaxWorkers)
            {
                Entity firedWorker = FindFireableEmployee(workplace, protectedWorker);
                if (firedWorker == Entity.Null)
                    return;

                ElectionDebug.Log($"Mayor workplace assignment firing employee {FormatEntity(firedWorker)} from workplace {FormatEntity(workplace)}.");
                RemoveEmployee(workplace, firedWorker);
                if (EntityManager.Exists(firedWorker) &&
                    TryGetComponentData(firedWorker, out Worker worker) &&
                    worker.m_Workplace == workplace)
                {
                    EntityManager.RemoveComponent<Worker>(firedWorker);
                }
            }
        }

        private bool HasEmployee(Entity workplace, Entity worker)
        {
            if (!EntityManager.Exists(workplace) || !EntityManager.HasBuffer<Employee>(workplace))
                return false;

            DynamicBuffer<Employee> employees = EntityManager.GetBuffer<Employee>(workplace);
            for (int i = 0; i < employees.Length; i++)
            {
                if (employees[i].m_Worker == worker)
                    return true;
            }

            return false;
        }

        private Entity FindFireableEmployee(Entity workplace, Entity protectedWorker)
        {
            DynamicBuffer<Employee> employees = EntityManager.GetBuffer<Employee>(workplace);
            for (int i = employees.Length - 1; i >= 0; i--)
            {
                Entity worker = employees[i].m_Worker;
                if (worker != protectedWorker)
                    return worker;
            }

            return Entity.Null;
        }

        private void MakeUnemployed(Entity citizen)
        {
            if (!IsValidCitizen(citizen))
                return;

            if (!EntityManager.HasComponent<Worker>(citizen))
                return;

            Worker worker = EntityManager.GetComponentData<Worker>(citizen);
            ElectionDebug.Log($"Citizen made unemployed by Elections mayor transition: citizen={FormatEntity(citizen)}, oldWorkplace={FormatEntity(worker.m_Workplace)}.");
            RemoveEmployee(worker.m_Workplace, citizen);
            EntityManager.RemoveComponent<Worker>(citizen);
        }

        private void RemoveEmployee(Entity workplace, Entity citizen)
        {
            if (workplace == Entity.Null ||
                !EntityManager.Exists(workplace) ||
                !EntityManager.HasBuffer<Employee>(workplace))
            {
                return;
            }

            DynamicBuffer<Employee> employees = EntityManager.GetBuffer<Employee>(workplace);
            for (int i = employees.Length - 1; i >= 0; i--)
            {
                if (employees[i].m_Worker == citizen)
                    employees.RemoveAt(i);
            }
        }

        private void EnsureEmployee(Entity workplace, Entity citizen, byte level)
        {
            DynamicBuffer<Employee> employees = EntityManager.GetBuffer<Employee>(workplace);
            for (int i = 0; i < employees.Length; i++)
            {
                Employee employee = employees[i];
                if (employee.m_Worker == citizen)
                {
                    if (employee.m_Level != level)
                    {
                        employee.m_Level = level;
                        employees[i] = employee;
                    }

                    return;
                }
            }

            employees.Add(new Employee
            {
                m_Worker = citizen,
                m_Level = level
            });
        }

        private bool TryFindDefaultMayorHome(out Entity home)
        {
            home = Entity.Null;

            return TryFindDefaultMayorHome(m_SignatureResidentialQuery, out home) ||
                   TryFindDefaultMayorHome(m_LowDensityResidentialQuery, out home);
        }

        private bool TryFindDefaultMayorHome(EntityQuery query, out Entity home)
        {
            home = Entity.Null;

            if (query.IsEmptyIgnoreFilter)
                return false;

            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    if (IsValidMayorHome(entity))
                    {
                        home = entity;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryFindDefaultMayorWorkplace(out Entity workplace)
        {
            workplace = Entity.Null;

            if (m_CityHallQuery.IsEmptyIgnoreFilter)
                return false;

            using (NativeArray<Entity> entities = m_CityHallQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    if (IsValidMayorWorkplace(entity))
                    {
                        workplace = entity;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsCityHallPrefab(Entity prefab)
        {
            string prefabName = m_PrefabSystem.GetPrefabName(prefab);
            if (string.IsNullOrWhiteSpace(prefabName))
                return false;

            return prefabName.IndexOf("City Hall", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   prefabName.IndexOf("CityHall", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsValidCitizen(Entity citizen)
        {
            return citizen != Entity.Null &&
                   EntityManager.Exists(citizen) &&
                   EntityManager.HasComponent<Citizen>(citizen) &&
                   !EntityManager.HasComponent<Deleted>(citizen) &&
                   !EntityManager.HasComponent<Temp>(citizen);
        }

        private byte GetWorkerLevel(Entity citizen)
        {
            if (EntityManager.HasComponent<Worker>(citizen))
                return EntityManager.GetComponentData<Worker>(citizen).m_Level;

            if (EntityManager.HasComponent<Citizen>(citizen))
                return (byte)math.clamp(EntityManager.GetComponentData<Citizen>(citizen).GetEducationLevel(), 0, 4);

            return 0;
        }

        private static string FormatEntity(Entity entity)
        {
            if (entity == Entity.Null)
                return "Entity.Null";

            return $"Entity({entity.Index}:{entity.Version})";
        }

        private bool TryGetComponentData<T>(Entity entity, out T component) where T : unmanaged, IComponentData
        {
            if (entity != Entity.Null && EntityManager.Exists(entity) && EntityManager.HasComponent<T>(entity))
            {
                component = EntityManager.GetComponentData<T>(entity);
                return true;
            }

            component = default;
            return false;
        }
    }
}
