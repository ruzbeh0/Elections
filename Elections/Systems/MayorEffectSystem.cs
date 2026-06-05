using Elections.Bridge;
using Elections.Components;
using Elections.Models;
using Game;
using Game.City;
using Game.Simulation;
using Unity.Entities;

namespace Elections.Systems
{
    public partial class MayorEffectSystem : GameSystemBase
    {
        private const int kUpdateInterval = 4096;

        private EntityQuery m_StateQuery;
        private CitySystem m_CitySystem;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return kUpdateInterval;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_StateQuery = GetEntityQuery(ComponentType.ReadWrite<ElectionState>());
        }

        protected override void OnUpdate()
        {
            if (Mod.m_Setting == null)
                return;

            if (!Mod.m_Setting.EnableElections || m_StateQuery.IsEmptyIgnoreFilter)
            {
                RealisticTripsBridge.ClearMayorResourceConsumptionMultiplier(0);
                return;
            }

            Entity stateEntity = m_StateQuery.GetSingletonEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            bool appliedEffectMatches = state.appliedEffectId == state.mayorEffectId &&
                                        state.appliedNegativeSoftened == state.mayorNegativeSoftened;

            if (appliedEffectMatches && state.mayorMoneyApplied)
            {
                SyncRealisticTripsEffect(state);
                return;
            }

            Entity city = m_CitySystem.City;
            if (city == Entity.Null || !EntityManager.Exists(city))
            {
                ElectionDebug.Log("Mayor effect update skipped: city entity is not available.");
                return;
            }

            if (!appliedEffectMatches)
            {
                ElectionDebug.Log($"Mayor effect changed: appliedEffectId={state.appliedEffectId}, mayorEffectId={state.mayorEffectId}, softened={state.mayorNegativeSoftened}. Removing old modifiers before applying the new effect.");
                RealisticTripsBridge.ClearMayorResourceConsumptionMultiplier(state.appliedEffectId);
                RemoveAppliedModifiers(city, ref state);
            }

            if (state.mayorEffectId > 0)
            {
                ElectionEffectDefinition effect = ElectionEffects.Get(state.mayorEffectId, state.mayorNegativeSoftened);
                SyncRealisticTripsEffect(effect);

                if (!state.mayorMoneyApplied)
                {
                    ElectionDebug.Log($"Applying mayor money effect: effectId={state.mayorEffectId}, effect={effect.Name}, delta={effect.MoneyDelta:n0}.");
                    ApplyMoney(city, effect.MoneyDelta);
                    state.mayorMoneyApplied = true;
                }

                if (!appliedEffectMatches)
                {
                    ElectionDebug.Log($"Applying mayor city modifiers: effectId={state.mayorEffectId}, effect={effect.Name}, modifier1={effect.ModifierType1} add={effect.Add1} mul={effect.Mul1}, modifier2={effect.ModifierType2} add={effect.Add2} mul={effect.Mul2}.");
                    ApplyModifiers(city, effect, ref state);
                    state.appliedEffectId = state.mayorEffectId;
                    state.appliedNegativeSoftened = state.mayorNegativeSoftened;
                }
            }
            else
            {
                ElectionDebug.Log("No mayor policy effect is active; marking mayor money effect as applied.");
                RealisticTripsBridge.ClearMayorResourceConsumptionMultiplier(0);
                state.mayorMoneyApplied = true;
                state.appliedNegativeSoftened = false;
            }

            EntityManager.SetComponentData(stateEntity, state);
        }

        private void ApplyMoney(Entity city, int delta)
        {
            if (delta == 0 || !EntityManager.HasComponent<PlayerMoney>(city))
                return;

            PlayerMoney money = EntityManager.GetComponentData<PlayerMoney>(city);
            if (delta > 0)
                money.Add(delta);
            else
                money.Subtract(-delta);
            EntityManager.SetComponentData(city, money);
            ElectionDebug.Log($"Mayor money effect applied: delta={delta:n0}.");
        }

        private static void SyncRealisticTripsEffect(ElectionState state)
        {
            if (state.mayorEffectId <= 0)
            {
                RealisticTripsBridge.ClearMayorResourceConsumptionMultiplier(0);
                return;
            }

            SyncRealisticTripsEffect(ElectionEffects.Get(state.mayorEffectId, state.mayorNegativeSoftened));
        }

        private static void SyncRealisticTripsEffect(ElectionEffectDefinition effect)
        {
            float multiplier = effect.ResourceConsumptionMultiplier > 0f ? effect.ResourceConsumptionMultiplier : 1f;
            RealisticTripsBridge.SetMayorResourceConsumptionMultiplier(effect.Id, multiplier);
        }

        private void ApplyModifiers(Entity city, ElectionEffectDefinition effect, ref ElectionState state)
        {
            if (!EntityManager.HasBuffer<CityModifier>(city))
            {
                ElectionDebug.Log("Mayor modifier application skipped: city has no CityModifier buffer.");
                return;
            }

            DynamicBuffer<CityModifier> modifiers = EntityManager.GetBuffer<CityModifier>(city);

            state.appliedModifierType1 = (int)effect.ModifierType1;
            state.appliedModifierAdd1 = effect.Add1;
            state.appliedModifierMul1 = effect.Mul1;
            AddModifierDelta(modifiers, state.appliedModifierType1, state.appliedModifierAdd1, state.appliedModifierMul1);

            state.appliedModifierType2 = (int)effect.ModifierType2;
            state.appliedModifierAdd2 = effect.Add2;
            state.appliedModifierMul2 = effect.Mul2;
            AddModifierDelta(modifiers, state.appliedModifierType2, state.appliedModifierAdd2, state.appliedModifierMul2);
        }

        private void RemoveAppliedModifiers(Entity city, ref ElectionState state)
        {
            if (EntityManager.HasBuffer<CityModifier>(city))
            {
                ElectionDebug.Log($"Removing applied mayor modifiers: effectId={state.appliedEffectId}, modifier1={state.appliedModifierType1} add={state.appliedModifierAdd1} mul={state.appliedModifierMul1}, modifier2={state.appliedModifierType2} add={state.appliedModifierAdd2} mul={state.appliedModifierMul2}.");
                DynamicBuffer<CityModifier> modifiers = EntityManager.GetBuffer<CityModifier>(city);
                AddModifierDelta(modifiers, state.appliedModifierType1, -state.appliedModifierAdd1, -state.appliedModifierMul1);
                AddModifierDelta(modifiers, state.appliedModifierType2, -state.appliedModifierAdd2, -state.appliedModifierMul2);
            }

            state.appliedEffectId = 0;
            state.appliedNegativeSoftened = false;
            state.appliedModifierType1 = -1;
            state.appliedModifierAdd1 = 0f;
            state.appliedModifierMul1 = 0f;
            state.appliedModifierType2 = -1;
            state.appliedModifierAdd2 = 0f;
            state.appliedModifierMul2 = 0f;
        }

        private static void AddModifierDelta(DynamicBuffer<CityModifier> modifiers, int modifierType, float add, float mul)
        {
            if (modifierType < 0 || (add == 0f && mul == 0f))
                return;

            while (modifiers.Length <= modifierType)
                modifiers.Add(default);

            CityModifier modifier = modifiers[modifierType];
            modifier.m_Delta.x += add;
            modifier.m_Delta.y += mul;
            modifiers[modifierType] = modifier;
        }
    }
}
