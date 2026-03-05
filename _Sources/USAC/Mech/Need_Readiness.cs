using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace USAC
{
    // 定义机兵整备需求逻辑类
    public class Need_Readiness : Need
    {
        public Need_Readiness(Pawn pawn) : base(pawn)
        {
            threshPercents = new System.Collections.Generic.List<float> { 0.01f };
        }

        private CompMechReadiness cachedComp;
        private bool compCacheInit;

        private CompMechReadiness Comp
        {
            get
            {
                if (!compCacheInit)
                {
                    cachedComp = pawn.TryGetComp<CompMechReadiness>();
                    compCacheInit = true;
                }
                return cachedComp;
            }
        }

        public override float MaxLevel => Comp?.Props.capacity ?? 100f;

        public override int GUIChangeArrow => -1;

        public override float CurLevel
        {
            get => curLevelInt;
            set
            {
                curLevelInt = UnityEngine.Mathf.Clamp(value, 0f, MaxLevel);
                UpdateHediff();
            }
        }

        // 判定整备需求列表可见性
        public override bool ShowOnNeedList => Comp != null && pawn.Faction != null && pawn.Faction.IsPlayer;

        // 初始化整备等级
        public override void SetInitialLevel()
        {
            CurLevelPercentage = 1f;
        }

        public override void NeedInterval()
        {
            if (IsFrozen || Comp == null || pawn.Faction == null || !pawn.Faction.IsPlayer) return;
            float consumeAmount = Comp.Props.consumptionPerDay / 400f;
            CurLevel -= consumeAmount;
        }

        public void Resupply(Thing supplyThing)
        {
            var comp = Comp;
            if (comp == null || supplyThing.def != comp.Props.supplyDef) return;

            float needed = MaxLevel - CurLevel;
            float restorePerItem = MaxLevel * 0.25f;
            int toConsume = UnityEngine.Mathf.CeilToInt(needed / restorePerItem);

            toConsume = UnityEngine.Mathf.Min(toConsume, supplyThing.stackCount);

            if (toConsume > 0)
            {
                float amountToRestore = toConsume * restorePerItem;
                supplyThing.SplitOff(toConsume).Destroy();
                CurLevel += amountToRestore;
            }
        }

        private void UpdateHediff()
        {
            var comp = Comp;
            if (comp == null || pawn == null || !pawn.Spawned || pawn.Dead || comp.Props.lowReadinessHediff == null) return;

            bool isLow = CurLevel <= 0f;
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(comp.Props.lowReadinessHediff);

            if (isLow && existing == null)
            {
                pawn.health.AddHediff(comp.Props.lowReadinessHediff);
            }
            else if (!isLow && existing != null)
            {
                pawn.health.RemoveHediff(existing);
            }
        }

        public override string GetTipString()
        {
            StringBuilder sb = new StringBuilder(base.GetTipString());
            var comp = Comp;
            if (comp != null)
            {
                float percent = comp.Props.consumptionPerDay / comp.Props.capacity * 100f;
                sb.AppendInNewLine("USAC_ReadinessConsumption".Translate(percent.ToString("F1")));
            }
            return sb.ToString();
        }
    }
}
