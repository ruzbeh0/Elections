using Elections.Components;
using Game;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.Buildings;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Elections.Systems
{
    public partial class MayorWorkplaceSystem : GameSystemBase
    {
        private EntityQuery m_StateQuery;
        private EntityQuery m_CityHallQuery;
        private PrefabSystem m_PrefabSystem;
        private Entity m_AssignedMayor;
        private Entity m_AssignedWorkplace;
        private bool m_LoggedNoCityHall;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 512;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_StateQuery = GetEntityQuery(ComponentType.ReadOnly<ElectionState>());
            m_CityHallQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<AdminBuilding>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<WorkProvider>(),
                    ComponentType.ReadWrite<Employee>()
                },
                None = new[]
                {
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>()
                }
            });
        }

        protected override void OnUpdate()
        {
            if (Mod.m_Setting == null || !Mod.m_Setting.EnableElections || m_StateQuery.IsEmptyIgnoreFilter)
                return;

            ElectionState state = m_StateQuery.GetSingleton<ElectionState>();
            Entity mayor = state.mayor;

            if (!IsValidCitizen(mayor))
            {
                if (m_AssignedMayor != Entity.Null)
                {
                    ElectionDebug.Log($"Mayor workplace cleared: assigned mayor {FormatEntity(m_AssignedMayor)} is no longer the current valid mayor.");
                    MakeUnemployed(m_AssignedMayor);
                    m_AssignedMayor = Entity.Null;
                    m_AssignedWorkplace = Entity.Null;
                }

                return;
            }

            if (m_AssignedMayor != Entity.Null && m_AssignedMayor != mayor)
            {
                ElectionDebug.Log($"Mayor changed: previous mayor {FormatEntity(m_AssignedMayor)} is being made unemployed before assigning {FormatEntity(mayor)}.");
                MakeUnemployed(m_AssignedMayor);
            }

            if (!TryFindCityHall(out Entity cityHall))
            {
                if (!m_LoggedNoCityHall)
                {
                    m_LoggedNoCityHall = true;
                    ElectionDebug.Log($"Mayor workplace assignment skipped: no City Hall admin building was found for mayor {FormatEntity(mayor)}.");
                }

                m_AssignedMayor = Entity.Null;
                m_AssignedWorkplace = Entity.Null;
                return;
            }

            m_LoggedNoCityHall = false;
            Entity previousMayor = m_AssignedMayor;
            Entity previousWorkplace = m_AssignedWorkplace;
            if (AssignMayorToCityHall(mayor, cityHall))
            {
                m_AssignedMayor = mayor;
                m_AssignedWorkplace = cityHall;

                if (previousMayor != mayor || previousWorkplace != cityHall)
                    ElectionDebug.Log($"Mayor workplace assigned: mayor={FormatEntity(mayor)}, cityHall={FormatEntity(cityHall)}.");
            }
        }

        private bool AssignMayorToCityHall(Entity mayor, Entity cityHall)
        {
            if (!IsValidCitizen(mayor) ||
                cityHall == Entity.Null ||
                !EntityManager.Exists(cityHall) ||
                !EntityManager.HasBuffer<Employee>(cityHall))
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

        private bool TryFindCityHall(out Entity cityHall)
        {
            cityHall = Entity.Null;

            if (m_CityHallQuery.IsEmptyIgnoreFilter)
                return false;

            using (NativeArray<Entity> entities = m_CityHallQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    if (!EntityManager.HasComponent<PrefabRef>(entity))
                        continue;

                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                    string prefabName = m_PrefabSystem.GetPrefabName(prefab);
                    if (!string.IsNullOrWhiteSpace(prefabName) &&
                        prefabName.IndexOf("City Hall", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        cityHall = entity;
                        return true;
                    }
                }
            }

            return false;
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
    }
}
