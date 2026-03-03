using System.Collections.Generic;
using Verse;
using RimWorld;

namespace USAC
{
    // USAC 企业服务管理
    public class GameComponent_USACServices : GameComponent
    {
        #region 字段
        // 自动续费名单
        public HashSet<Pawn> autoRenewPawns = new HashSet<Pawn>();

        // 轨道商船预约到达时刻
        public int traderArrivalTick = -1;
        #endregion

        #region 生命周期
        public GameComponent_USACServices(Game game) { }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 2500 == 0)
            {
                CheckAutoRenewals();
            }

            // 检测商船预约到期
            if (traderArrivalTick > 0 && Find.TickManager.TicksGame >= traderArrivalTick)
            {
                SpawnScheduledTrader();
                traderArrivalTick = -1;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref autoRenewPawns, "autoRenewPawns", LookMode.Reference);
            if (autoRenewPawns == null) autoRenewPawns = new HashSet<Pawn>();
            Scribe_Values.Look(ref traderArrivalTick, "traderArrivalTick", -1);
        }
        #endregion

        #region 逻辑
        private void CheckAutoRenewals()
        {
            List<Pawn> toRemove = new List<Pawn>();
            foreach (var pawn in autoRenewPawns)
            {
                if (pawn == null || pawn.Dead || pawn.Destroyed)
                {
                    toRemove.Add(pawn);
                    continue;
                }

                var trigger = pawn.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("USAC_TempMechlinkTrigger")) as HediffWithComps;
                if (trigger == null)
                {
                    toRemove.Add(pawn);
                    continue;
                }

                var disappearComp = trigger.TryGetComp<HediffComp_Disappears>();
                // 临近过期自动扣费续期
                if (disappearComp != null && disappearComp.ticksToDisappear < 2550)
                {
                    TryRenew(pawn, disappearComp);
                }
            }

            foreach (var p in toRemove) autoRenewPawns.Remove(p);
        }

        public void TryRenew(Pawn pawn, HediffComp_Disappears comp)
        {
            var debtComp = GameComponent_USACDebt.Instance;
            if (debtComp != null && debtComp.GetBondCountNearBeacons(pawn.Map) >= 4)
            {
                debtComp.ConsumeBondsNearBeacons(pawn.Map, 4);
                comp.ticksToDisappear += 1800000;
                Messages.Message("USAC.Message.AutoRenewed".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                // 扣费失败取消续费
                autoRenewPawns.Remove(pawn);
                Messages.Message("USAC.Message.AutoRenewFailed".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.NegativeEvent);
            }
        }

        // 刷新预约的轨道商船
        private void SpawnScheduledTrader()
        {
            Map map = Find.AnyPlayerHomeMap;
            if (map == null) return;

            Faction usacFaction = Find.FactionManager.FirstFactionOfDef(
                DefDatabase<FactionDef>.GetNamed("USAC_Faction"));
            if (usacFaction == null || usacFaction.HostileTo(Faction.OfPlayer)) return;

            if (map.passingShipManager.passingShips.Count >= 5) return;

            TraderKindDef traderKind = DefDatabase<TraderKindDef>.GetNamedSilentFail("USAC_Trader_Orbital");
            if (traderKind == null) return;

            TradeShip ship = new TradeShip(traderKind, usacFaction);
            map.passingShipManager.AddShip(ship);
            ship.GenerateThings();

            Messages.Message("USAC.Message.TraderArrived".Translate(), MessageTypeDefOf.PositiveEvent);
        }
        #endregion
    }
}
