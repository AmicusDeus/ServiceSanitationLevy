using Game;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Economy;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace ServiceSanitationLevy
{
    // Coverage-gated municipal "Public Service Fee" (user-designed) — one fee covering fire, police, disaster and
    // garbage services, charged like a PROPERTY TAX: each occupant (household or company renter) pays a percentage
    // of ITS OWN monthly rent (PropertyRenter.m_Rent, the game's live economy-driven valuation) for every service
    // that actually operates at the building. Debited from occupant balances, credited to the treasury — a real
    // transfer, like the vanilla service fees, not minted income.
    //
    // Per occupant per month:
    //   * each COVERED emergency service (fire / police / disaster shelter road coverage):
    //         rent × PsfEmergencyPercent%
    //   * garbage (gated on a garbage facility existing citywide):
    //         rent × PsfGarbagePercent%  +  pollution surcharge share
    //     where the building-level surcharge = prefab pollution × PsfGarbagePercent × GarbagePollutionWeight
    //     is split across the building's occupants ("polluter pays" — in practice the single factory company).
    // Rent already encodes property value, density and zone type, so a villa household pays more than a studio
    // dweller, offices pay the most per company, and burdens self-adjust with the economy (no magic multipliers).
    //
    // Failsafe: LevyEnabled off => inert. Charged once per in-game day via TimeSystem.GetDay, and the expensive
    // citywide pass only runs on a day boundary (or when first enabled) to avoid per-tick cost.
    public partial class FirePoliceLevySystem : GameSystemBase
    {
        // Per-building fee breakdown (monthly ₡, summed over the building's occupants). A service's part is charged
        // ONLY when that service actually serves the building — no building, no charge.
        public struct PsfBreakdown
        {
            public int ValueTerm;      // what each covered emergency service bills (emergency% × sum of occupant rents)
            public int PollutionTerm;  // the pollution surcharge portion of the garbage part
            public int Fire;           // -1 = not covered (not billed), else the monthly part
            public int Police;
            public int Disaster;
            public int Garbage;        // rent part + pollution surcharge, or -1 when no facility exists
            public int Total;
        }

        private SimulationSystem m_Sim;
        private EntityQuery m_TimeQuery;
        private EntityQuery m_TimeSettingsQuery;
        private EntityQuery m_BuildingQuery;
        private EntityQuery m_GarbageFacilityQuery;
        private EntityQuery m_GarbageScanQuery;
        private bool m_GarbageServiceExists;

        // Citywide: does at least one (non-outside-connection) garbage-processing building exist? Garbage has no
        // road-coverage concept, so its share of the fee is gated on this. Two detection layers: the runtime
        // Game.Buildings.GarbageFacility component (landfills, incinerators), plus a prefab-data scan for buildings
        // whose prefab declares GarbageFacilityData — which catches RECYCLING CENTERS and any other processing
        // building whose runtime archetype differs. Exposed for the daily economy log too.
        public int GarbageFacilityCount { get; private set; }

        private int m_LastDay;
        private bool m_WasActive;
        private float m_LastBaseRate = -1f;
        private float m_LastPollRate = -1f;

        public int LevyIncome { get; private set; } // MONTHLY, for the budget/diagnostics

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Sim = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_TimeQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());
            m_TimeSettingsQuery = GetEntityQuery(ComponentType.ReadOnly<TimeSettingsData>());
            // Occupied buildings (have renters = households/companies).
            m_BuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<Renter>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() },
            });
            m_GarbageFacilityQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.GarbageFacility>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Game.Objects.OutsideConnection>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Game.Tools.Temp>(),
                },
            });
            // All city buildings, for the prefab-data garbage scan (recycling centers etc.).
            m_GarbageScanQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Game.Objects.OutsideConnection>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Game.Tools.Temp>(),
                },
            });
        }

        // Refresh the garbage-service flag + facility count. Runtime-component query first (cheap); if it finds
        // nothing, scan buildings' prefabs for GarbageFacilityData so recycling-only cities still count.
        private void RefreshGarbageService()
        {
            int count = m_GarbageFacilityQuery.CalculateEntityCount();
            if (count == 0 && !m_GarbageScanQuery.IsEmptyIgnoreFilter)
            {
                NativeArray<Entity> blds = m_GarbageScanQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < blds.Length; i++)
                {
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(blds[i]).m_Prefab;
                    if (prefab != Entity.Null && EntityManager.HasComponent<GarbageFacilityData>(prefab))
                        count++;
                }
                blds.Dispose();
            }
            GarbageFacilityCount = count;
            m_GarbageServiceExists = count > 0;
        }

        private int DaysPerMonth()
        {
            if (m_TimeSettingsQuery.IsEmptyIgnoreFilter)
                return 1;
            int daysPerYear = m_TimeSettingsQuery.GetSingleton<TimeSettingsData>().m_DaysPerYear;
            int dpm = daysPerYear / 12;
            return dpm < 1 ? 1 : dpm;
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 64;

        protected override void OnUpdate()
        {
            Setting s = Mod.ActiveSetting;
            if (s == null || m_TimeQuery.IsEmptyIgnoreFilter)
                return;

            RefreshGarbageService();

            if (!s.LevyEnabled)
            {
                m_WasActive = false;
                LevyIncome = 0;
                m_LastDay = TimeSystem.GetDay(m_Sim.frameIndex, m_TimeQuery.GetSingleton<TimeData>());
                return;
            }

            int day = TimeSystem.GetDay(m_Sim.frameIndex, m_TimeQuery.GetSingleton<TimeData>());
            bool firstRun = !m_WasActive;
            float emergencyPct = s.PsfEmergencyPercent;
            float garbagePct = s.PsfGarbagePercent;
            bool ratesChanged = emergencyPct != m_LastBaseRate || garbagePct != m_LastPollRate;
            bool dayBoundary = firstRun || day > m_LastDay;
            if (!dayBoundary && !ratesChanged)
                return; // recompute only on a day boundary, first enable, or when the rates change

            // Charge across elapsed days only; a mere rate change refreshes the income projection without billing.
            int daysElapsed = (dayBoundary && !firstRun) ? (day - m_LastDay) : 0;
            int daysPerMonth = DaysPerMonth();

            int monthlyTotal = 0;
            if (!m_BuildingQuery.IsEmptyIgnoreFilter)
            {
                NativeArray<Entity> blds = m_BuildingQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < blds.Length; i++)
                {
                    PsfBreakdown bd = ComputeBreakdown(blds[i], emergencyPct, garbagePct); // MONTHLY, whole building
                    if (bd.Total <= 0)
                        continue;
                    monthlyTotal += bd.Total;
                    if (daysElapsed > 0)
                        ChargeBuilding(blds[i], bd, emergencyPct, garbagePct, daysElapsed, daysPerMonth);
                }
                blds.Dispose();
            }

            LevyIncome = monthlyTotal; // already a monthly figure

            // Buildings are charged the levy directly (ChargeBuilding — real money leaves the payers, and that debit
            // lands because household/company money isn't touched by BudgetApplySystem). The CITY-side credit is
            // delivered as real budget INCOME by BudgetInjectSystem (LevyIncome folded into IncomeSource.TaxResidential).
            m_WasActive = true;
            m_LastDay = day;
            m_LastBaseRate = emergencyPct;
            m_LastPollRate = garbagePct;
        }

        // Pollution surcharge weight: building surcharge = prefab pollution × PsfGarbagePercent × this. Calibrated so
        // the DEFAULT garbage rate reproduces the previously live-tuned industry surcharge.
        public const float GarbagePollutionWeight = 0.012f;

        // Full per-building breakdown (rent-based economics):
        //   * Fire/police/disaster each bill emergencyPct% of every occupant's rent, gated on that service's road
        //     coverage reaching the building.
        //   * Garbage bills garbagePct% of every occupant's rent plus the building's pollution surcharge, gated on a
        //     garbage facility existing citywide.
        // No building => no charge. Figures are building-level sums (per-occupant billing happens in ChargeBuilding
        // against each renter's own rent; the sum matches because the parts are linear in rent).
        public PsfBreakdown ComputeBreakdown(Entity b, float emergencyPct, float garbagePct)
        {
            PsfBreakdown r = new PsfBreakdown { Fire = -1, Police = -1, Disaster = -1, Garbage = -1 };

            Building bd = EntityManager.GetComponentData<Building>(b);
            bool fireOn = false, policeOn = false, disasterOn = false;
            if (bd.m_RoadEdge != Entity.Null && EntityManager.HasBuffer<Game.Net.ServiceCoverage>(bd.m_RoadEdge))
            {
                DynamicBuffer<Game.Net.ServiceCoverage> cov = EntityManager.GetBuffer<Game.Net.ServiceCoverage>(bd.m_RoadEdge, isReadOnly: true);
                fireOn = NetUtils.GetServiceCoverage(cov, CoverageService.FireRescue, bd.m_CurvePosition) > 0f;
                policeOn = NetUtils.GetServiceCoverage(cov, CoverageService.Police, bd.m_CurvePosition) > 0f;
                disasterOn = NetUtils.GetServiceCoverage(cov, CoverageService.EmergencyShelter, bd.m_CurvePosition) > 0f;
            }
            // Cached flag (refreshed every OnUpdate); the runtime-component OR covers calls before the first tick.
            bool garbageOn = m_GarbageServiceExists || !m_GarbageFacilityQuery.IsEmptyIgnoreFilter;

            if (!fireOn && !policeOn && !disasterOn && !garbageOn)
                return r; // nothing serves this building => no fee at all

            int rentSum = BuildingRentSum(b);

            float pollution = 0f;
            Entity prefab = EntityManager.GetComponentData<PrefabRef>(b).m_Prefab;
            if (EntityManager.HasComponent<PollutionData>(prefab))
            {
                PollutionData pd = EntityManager.GetComponentData<PollutionData>(prefab);
                pollution = pd.m_GroundPollution + pd.m_AirPollution + pd.m_NoisePollution;
                if (pollution < 0f) pollution = 0f;
            }

            r.ValueTerm = (int)System.Math.Round(rentSum * emergencyPct / 100.0);                    // per covered emergency service
            r.PollutionTerm = (int)System.Math.Round(pollution * garbagePct * GarbagePollutionWeight); // garbage pollution surcharge
            int garbageRent = (int)System.Math.Round(rentSum * garbagePct / 100.0);

            if (fireOn) { r.Fire = r.ValueTerm; r.Total += r.ValueTerm; }
            if (policeOn) { r.Police = r.ValueTerm; r.Total += r.ValueTerm; }
            if (disasterOn) { r.Disaster = r.ValueTerm; r.Total += r.ValueTerm; }
            if (garbageOn) { r.Garbage = garbageRent + r.PollutionTerm; r.Total += r.Garbage; }
            return r;
        }

        // Sum of the occupants' monthly rents (PropertyRenter.m_Rent; 1 in-game day = 1 budget month, and the game
        // pays rent in per-day slices of exactly m_Rent — so this IS the monthly figure in budget units). Public so
        // DiagnosticsSystem can log it for rate calibration.
        public int BuildingRentSum(Entity b)
        {
            if (!EntityManager.HasBuffer<Renter>(b))
                return 0;
            DynamicBuffer<Renter> renters = EntityManager.GetBuffer<Renter>(b, isReadOnly: true);
            int sum = 0;
            for (int j = 0; j < renters.Length; j++)
            {
                Entity e = renters[j].m_Renter;
                if (e == Entity.Null || !EntityManager.Exists(e) || !EntityManager.HasComponent<PropertyRenter>(e))
                    continue;
                int rent = EntityManager.GetComponentData<PropertyRenter>(e).m_Rent;
                if (rent > 0)
                    sum += rent;
            }
            return sum;
        }

        // Bill each occupant its OWN share: coveredServices × (rent × emergencyPct%) + garbage (rent × garbagePct% +
        // an equal split of the building's pollution surcharge). Returns the total actually charged.
        private int ChargeBuilding(Entity b, PsfBreakdown bd, float emergencyPct, float garbagePct, int daysElapsed, int daysPerMonth)
        {
            if (!EntityManager.HasBuffer<Renter>(b))
                return 0;
            DynamicBuffer<Renter> renters = EntityManager.GetBuffer<Renter>(b, isReadOnly: true);
            int n = renters.Length;
            if (n <= 0)
                return 0;

            int coveredServices = (bd.Fire >= 0 ? 1 : 0) + (bd.Police >= 0 ? 1 : 0) + (bd.Disaster >= 0 ? 1 : 0);
            bool garbageOn = bd.Garbage >= 0;
            int pollShare = (garbageOn && bd.PollutionTerm > 0) ? bd.PollutionTerm / n : 0;

            int charged = 0;
            for (int j = 0; j < n; j++)
            {
                Entity e = renters[j].m_Renter;
                if (e == Entity.Null || !EntityManager.Exists(e) || !EntityManager.HasBuffer<Resources>(e))
                    continue;
                int rent = 0;
                if (EntityManager.HasComponent<PropertyRenter>(e))
                    rent = EntityManager.GetComponentData<PropertyRenter>(e).m_Rent;
                if (rent < 0) rent = 0;

                int monthly = coveredServices * (int)System.Math.Round(rent * emergencyPct / 100.0);
                if (garbageOn)
                    monthly += (int)System.Math.Round(rent * garbagePct / 100.0) + pollShare;
                if (monthly <= 0)
                    continue;

                // Daily slice (DaysPerMonth is 1 by default, so 1 day == 1 month).
                int charge = (int)System.Math.Round((double)monthly / daysPerMonth) * daysElapsed;
                if (charge <= 0)
                    continue;
                DynamicBuffer<Resources> res = EntityManager.GetBuffer<Resources>(e);
                EconomyUtils.AddResources(Resource.Money, -charge, res);
                charged += charge;
            }
            return charged;
        }
    }
}
