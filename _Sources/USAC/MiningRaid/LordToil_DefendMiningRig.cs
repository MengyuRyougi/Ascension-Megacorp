using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace USAC
{
    // 定义机兵采矿点防守任务
    // 记录攻击者为临时敌对派系
    public class LordToil_DefendMiningRig : LordToil
    {
        #region 字段

        private IntVec3 defendPoint;
        private float defendRadius;
        private float wanderRadius;

        #endregion

        #region 属性

        public override IntVec3 FlagLoc => defendPoint;
        public override bool AllowSatisfyLongNeeds => false;

        #endregion

        #region 构造函数

        public LordToil_DefendMiningRig(IntVec3 point, float defendRadius = 10f, float wanderRadius = 5f)
        {
            this.defendPoint = point;
            this.defendRadius = defendRadius;
            this.wanderRadius = wanderRadius;
        }

        #endregion

        #region 公共方法

        public override void UpdateAllDuties()
        {
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn?.mindState == null) continue;

                pawn.mindState.duty = new PawnDuty(DutyDefOf.Defend, defendPoint);
                pawn.mindState.duty.focusSecond = defendPoint;
                pawn.mindState.duty.radius = defendRadius;
                pawn.mindState.duty.wanderRadius = wanderRadius;
            }
        }

        public override void Notify_PawnDamaged(Pawn victim, DamageInfo dinfo)
        {
            base.Notify_PawnDamaged(victim, dinfo);

            Thing instigator = dinfo.Instigator;
            if (instigator == null) return;

            Faction attackerFaction = instigator.Faction;
            if (attackerFaction == null || attackerFaction == lord.faction) return;

            // 切换至清理威胁状态
            lord.ReceiveMemo("StartKillThreats");
        }

        #endregion
    }
}
