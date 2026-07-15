using Game;
using Game.City;
using Game.Prefabs;
using Unity.Entities;

namespace ServiceSanitationLevy
{
    // Garbage is folded into the custom Public Service Fee (charged by rent + pollution / environmental impact), so
    // the stand-alone vanilla garbage service fee is turned OFF here: the fee parameter is locked (m_Adjustable =
    // false, so it isn't a dead slider) and its live value is forced to 0 — no separate/duplicate garbage bill, and
    // no phantom charge when there's no facility.
    public partial class GarbageFeeOffSystem : GameSystemBase
    {
        private EntityQuery m_ParamQuery;
        private EntityQuery m_FeeQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ParamQuery = GetEntityQuery(ComponentType.ReadWrite<ServiceFeeParameterData>());
            m_FeeQuery = GetEntityQuery(ComponentType.ReadWrite<ServiceFee>());
            RequireForUpdate(m_ParamQuery);
            RequireForUpdate(m_FeeQuery);
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 64;

        protected override void OnUpdate()
        {
            Setting s = Mod.ActiveSetting;
            // Only fold the garbage fee away while the levy is actually on; if the user turns the levy off we leave
            // the native garbage fee alone (pure vanilla).
            if (s == null || !s.LevyEnabled)
                return;

            // Lock the garbage fee parameter so the native Services-tab slider isn't adjustable.
            Entity paramEntity = m_ParamQuery.GetSingletonEntity();
            ServiceFeeParameterData d = EntityManager.GetComponentData<ServiceFeeParameterData>(paramEntity);
            if (d.m_GarbageFee.m_Adjustable)
            {
                d.m_GarbageFee.m_Adjustable = false;
                EntityManager.SetComponentData(paramEntity, d);
            }

            // Force the live garbage fee value to 0 (no separate garbage bill).
            Entity city = m_FeeQuery.GetSingletonEntity();
            DynamicBuffer<ServiceFee> fees = EntityManager.GetBuffer<ServiceFee>(city);
            for (int i = 0; i < fees.Length; i++)
            {
                if (fees[i].m_Resource == PlayerResource.Garbage && fees[i].m_Fee != 0f)
                {
                    ServiceFee f = fees[i];
                    f.m_Fee = 0f;
                    fees[i] = f;
                    break;
                }
            }
        }
    }
}
