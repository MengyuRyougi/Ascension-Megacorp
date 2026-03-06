using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace USAC
{
    // USAC轨道夹具完整生命周期
    // 下降→着陆抓取→上升离开
    public class Skyfaller_USACGripper : Skyfaller, IActiveTransporter
    {
        private Thing targetThing;

        private bool isLifting;
        private int liftTicks;
        private const int LiftDuration = 150;
        // 夹具垂直向上偏置
        private const float GripperOffsetZ = 0.5f;
        // 上升阶段水平锚点
        private Vector3 landedAnchor;
        // 上一帧目标位置
        private Vector3 lastTargetPos;
        // 是否已正常完成
        private bool completedNormally;
        // 位置跳变距离阈值，将其约束到1格
        private const float WarpThreshold = 1f;

        private Rot4 targetRotation = Rot4.North;
        private float gripperScale = 1.5f;
        private Graphic cachedScaledGraphic;
        private float cachedScaleKey = -1f;

        public void SetTarget(Thing target)
        {
            targetThing = target;
            if (target != null)
            {
                lastTargetPos = target.DrawPos;

                // 根据目标类型计算夹具缩放
                if (target is Pawn pawn)
                {
                    // 基于体型尺寸缩放
                    gripperScale = Mathf.Max(pawn.BodySize * 1.5f, 1.2f);
                }
                else if (target is Building b)
                {
                    gripperScale = Mathf.Max(b.def.size.x, b.def.size.z) * 1.2f;
                }
                else
                {
                    gripperScale = 1.2f; // 普通物品默认缩放
                }

                targetRotation = target.Rotation;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref targetThing, "targetThing");
            Scribe_Values.Look(ref isLifting, "isLifting");
            Scribe_Values.Look(ref liftTicks, "liftTicks");
            Scribe_Values.Look(ref landedAnchor, "landedAnchor");
            Scribe_Values.Look(ref gripperScale, "gripperScale", 1.5f);
            Scribe_Values.Look(ref targetRotation, "targetRotation", Rot4.North);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                // 赋予敌对派系，使其能够被防空设施自动选作目标
                factionInt = Find.FactionManager.RandomEnemyFaction(false, false, true, TechLevel.Undefined);
            }
        }

        protected override void Impact()
        {
            Map map = Map;

            // 追踪目标实际位置落点
            IntVec3 pos = (targetThing is { Spawned: true })
                ? targetThing.Position
                : Position;

            // 破拆屋顶
            if (pos.Roofed(map))
            {
                var roof = pos.GetRoof(map);
                map.roofGrid.SetRoof(pos, null);
                if (roof.isThickRoof)
                {
                    for (int i = 0; i < 3; i++)
                        FleckMaker.ThrowDustPuff(
                            pos.ToVector3Shifted() + Gen.RandomHorizontalVector(0.5f),
                            map, 1.5f);
                }
            }

            FleckMaker.ThrowDustPuff(pos.ToVector3Shifted(), map, 2.5f);

            if (def.skyfaller.impactSound != null)
                def.skyfaller.impactSound.PlayOneShot(
                    SoundInfo.InMap(new TargetInfo(pos, map)));

            // 抓取目标并锁定锚点
            if (targetThing is { Spawned: true })
            {
                landedAnchor = targetThing.DrawPos;
                targetRotation = targetThing.Rotation;
                targetThing.DeSpawn();
                innerContainer.TryAdd(targetThing);
            }
            else
            {
                landedAnchor = pos.ToVector3Shifted();
            }

            // 进入上升阶段
            isLifting = true;
            hasImpacted = true;
        }

        protected override void Tick()
        {
            if (isLifting)
            {
                liftTicks++;
                if (liftTicks >= LiftDuration)
                {
                    completedNormally = true;
                    innerContainer.ClearAndDestroyContents();
                    Destroy();
                }
                return;
            }

            // 下降检测目标跳变
            if (targetThing is { Spawned: true })
            {
                Vector3 curPos = targetThing.DrawPos;
                float d = (curPos - lastTargetPos).sqrMagnitude;
                if (d > WarpThreshold * WarpThreshold)
                {
                    // 目标丢失直接空夹上升
                    Messages.Message("USAC.Debt.Message.GripperTargetLost".Translate(),
                        MessageTypeDefOf.NeutralEvent);
                    landedAnchor = curPos;
                    isLifting = true;
                    hasImpacted = true;
                    return;
                }
                lastTargetPos = curPos;
            }
            else if (targetThing != null && !targetThing.Spawned && !isLifting)
            {
                // 目标意外消失处理
                landedAnchor = Position.ToVector3Shifted();
                isLifting = true;
                hasImpacted = true;
                return;
            }

            // 主动进行护盾拦截检测，使原版高角护盾生效
            if (!hasImpacted)
            {
                CheckForShieldInterception();
            }

            if (!Destroyed)
            {
                base.Tick();
            }
        }

        private void CheckForShieldInterception()
        {
            if (Map == null) return;

            // 假设上一帧位于高空轨道上
            Vector3 lastExactPos = DrawPos + new Vector3(0f, 0f, 10f);
            Vector3 newExactPos = DrawPos;

            List<Thing> interceptors = Map.listerThings.ThingsInGroup(ThingRequestGroup.ProjectileInterceptor);
            for (int i = 0; i < interceptors.Count; i++)
            {
                var comp = interceptors[i].TryGetComp<CompProjectileInterceptor>();
                if (comp != null && comp.Active && comp.Props.interceptAirProjectiles)
                {
                    // 借用Projectlies的逻辑计算
                    Vector3 center = interceptors[i].Position.ToVector3Shifted();
                    float radius = comp.Props.radius;
                    // 由于夹具从天而降，只检查其在二维平面上是否落入护盾圈
                    if ((newExactPos.x - center.x) * (newExactPos.x - center.x) + (newExactPos.z - center.z) * (newExactPos.z - center.z) <= radius * radius)
                    {
                        // 触发护盾特效并阻断夹具
                        FleckMaker.ThrowLightningGlow(newExactPos, Map, 2f);
                        Destroy(DestroyMode.KillFinalize);
                        return;
                    }
                }
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (isLifting)
            {
                DrawLifting(drawLoc);
                return;
            }
            // XZ轴追踪目标实时位置
            if (targetThing is { Spawned: true })
            {
                Vector3 basePos = Position.ToVector3Shifted();
                Vector3 offset = drawLoc - basePos;
                Vector3 targetPos = targetThing.DrawPos;

                // 修正绘制轨迹偏移量
                drawLoc.x = targetPos.x + offset.x;
                drawLoc.z = targetPos.z + offset.z;
                drawLoc.y = targetPos.y;
            }

            // 绘制目标缩放图像
            if (Graphic != null)
            {
                GetScaledGraphic()?.Draw(drawLoc, Rot4.North, this);
            }
        }

        // 缓存缩放后的图形对象
        private Graphic GetScaledGraphic()
        {
            if (Graphic == null) return null;
            if (cachedScaledGraphic == null || cachedScaleKey != gripperScale)
            {
                cachedScaledGraphic = Graphic.GetCopy(new Vector2(gripperScale, gripperScale), null);
                cachedScaleKey = gripperScale;
            }
            return cachedScaledGraphic;
        }

        // 基于锚点绘制上升动效
        private void DrawLifting(Vector3 drawLoc)
        {
            float t = (float)liftTicks / LiftDuration;
            float riseZ = t * t * 30f;

            // 以锁定的落点锚点为水平基准
            Vector3 rootPos = landedAnchor;
            rootPos.z += riseZ;

            // 原地渲染消除跳变感
            // 设置目标为直线升空

            // 被抓取目标处于提拉的中轴基准上
            if (innerContainer.Count > 0)
            {
                Thing carried = innerContainer[0];
                Vector3 carryPos = rootPos;
                carryPos.y = Altitudes.AltitudeFor(AltitudeLayer.Pawn);
                DrawCarried(carried, carryPos);
            }

            // 依据比例向上偏移夹具
            Vector3 gripperPos = rootPos;
            gripperPos.z += GripperOffsetZ * (gripperScale / 1.5f);
            gripperPos.y = Altitudes.AltitudeFor(AltitudeLayer.Skyfaller);

            // 绘制夹具本体 (应用缩放)
            GetScaledGraphic()?.Draw(gripperPos, Rot4.North, this);
        }

        // 补充 IActiveTransporter 必须的装载信息
        private ActiveTransporterInfo dummyInfo;
        public ActiveTransporterInfo Contents
        {
            get
            {
                if (dummyInfo == null)
                    dummyInfo = new ActiveTransporterInfo { parent = this };
                return dummyInfo;
            }
        }

        // 被摧毁或拦截时增加额外债务
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (!completedNormally && !Destroyed)
            {
                // 罚金计入最近合同
                try
                {
                    var debtComp = GameComponent_USACDebt.Instance;
                    var target = debtComp?.NextDueContract;
                    if (debtComp != null && target != null)
                    {
                        float penalty = 3000f;
                        target.Principal += penalty;
                        target.MissedPayments++;
                        debtComp.AddTransaction(
                            USACTransactionType.Penalty, penalty,
                            "USAC.Debt.Transaction.GripperDestroyed".Translate());
                        Messages.Message(
                            "USAC.Debt.Message.GripperDestroyedPenalty"
                                .Translate(penalty.ToString("N0")),
                            MessageTypeDefOf.NegativeEvent);
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[USAC] Failed to apply penalty for gripper destruction: {ex}");
                }
            }
            base.Destroy(mode);
        }

        // 保持原始比例朝向渲染
        private void DrawCarried(Thing carried, Vector3 carryPos)
        {
            if (carried is Pawn pawn)
            {
                try
                {
                    // 执行面向镜头渲染
                    pawn.Drawer.renderer.RenderPawnAt(carryPos, Rot4.South);
                }
                catch
                {
                    // 兼容其他模组渲染拦截
                    pawn.Graphic?.Draw(carryPos, Rot4.South, pawn);
                }
            }
            else
            {
                // 物品和建筑保持被抓取前的朝向
                carried.Graphic?.Draw(carryPos, targetRotation, carried);
            }
        }
    }
}
