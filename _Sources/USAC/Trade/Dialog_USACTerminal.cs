using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Verse.Sound;
using static USAC.InternalUI.PortalUIUtility;

namespace USAC
{
    [StaticConstructorOnStartup]
    public class Dialog_USACTerminal : Window
    {
        #region 字段
        private readonly bool giftsOnly;
        private Vector2 scrollPositionLeft;
        private Vector2 scrollPositionRight;
        private List<Tradeable> cachedTradeables;
        private List<Tradeable> filteredTradeables;
        private Tradeable cachedCurrencyTradeable;
        private QuickSearchWidget quickSearchWidget = new();
        private enum SortColumn { Name, Count, Price }
        private SortColumn playerSortColumn = SortColumn.Name;
        private SortColumn traderSortColumn = SortColumn.Name;
        private bool playerSortAscending = true;
        private bool traderSortAscending = true;
        #endregion

        public override Vector2 InitialSize => new(1200f, 800f);
        protected override float Margin => 0f;

        public Dialog_USACTerminal(bool giftsOnly = false)
        {
            this.giftsOnly = giftsOnly;
            doCloseX = false;
            forcePause = true;
            absorbInputAroundWindow = true;
            doWindowBackground = false;
            drawShadow = false;
            filteredTradeables = new List<Tradeable>();
        }

        public override void PreOpen()
        {
            base.PreOpen();
            CacheTradeables();
            ApplySearchFilter();
        }

        public override void PostClose()
        {
            base.PostClose();
            if (TradeSession.trader is Pawn pawn && pawn.mindState.hasQuest)
            {
                TradeUtility.ReceiveQuestFromTrader(pawn, TradeSession.playerNegotiator);
            }
        }

        #region 核心绘制
        public override void DoWindowContents(Rect inRect)
        {
            Rect fullRect = new(0, 0, InitialSize.x, InitialSize.y);
            Widgets.DrawBoxSolid(fullRect, ColWindowBg);
            DrawBackgroundGrid(fullRect);

            GUI.BeginGroup(inRect);
            DrawTerminalHeader(new Rect(0, 0, inRect.width, 70));
            DrawSearchBar(new Rect(0, 80, inRect.width, 35));
            Rect mainRect = new(0, 125, inRect.width, inRect.height - 205);
            BeginTacticalScroll(out var prevBar, out var prevThumb, out var prevColor);
            DrawTradeArea(mainRect);
            EndTacticalScroll(prevBar, prevThumb, prevColor);
            Rect footerRect = new(0, inRect.height - 70, inRect.width, 70);
            DrawFooter(footerRect);

            GUI.EndGroup();
        }

        private void DrawTerminalHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, ColHeaderBg);
            DrawUIGradient(rect, new Color(1, 1, 1, 0.05f), new Color(0, 0, 0, 0.1f));
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = ColAccentCamo1;
            Widgets.Label(new Rect(20, 0, 500, rect.height), "USAC.Trade.Terminal".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = ColTextMuted;
            string traderInfo = TradeSession.trader.TraderName;

            float traderNameMaxWidth = 600f;
            Rect traderRect = new(rect.width - 70 - traderNameMaxWidth, 0, traderNameMaxWidth, rect.height);
            Widgets.Label(traderRect, traderInfo);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            if (DrawTacticalButton(new Rect(rect.width - 50, 18, 34, 34), "X", true, GameFont.Small, "close_terminal"))
            {
                Close();
            }
        }

        private void DrawSearchBar(Rect rect)
        {
            Widgets.DrawBoxSolidWithOutline(rect, ColHeaderBg, ColBorder);
            Rect inner = rect.ContractedBy(5);
            quickSearchWidget.OnGUI(inner, ApplySearchFilter, () => ApplySearchFilter());
            if (quickSearchWidget.filter.Active && filteredTradeables != null)
            {
                string countText = $"{filteredTradeables.Count}/{cachedTradeables?.Count ?? 0}";
                GUI.color = ColAccentCamo3;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(rect.x + 5, rect.yMax - 15, rect.width - 10, 12), countText);
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
        }

        private void DrawTradeArea(Rect rect)
        {
            float totalActive = rect.width;
            float leftWidth = Mathf.Round(totalActive * 0.40f);
            float centerWidth = Mathf.Round(totalActive * 0.20f);
            float rightWidth = totalActive - leftWidth - centerWidth;

            Rect leftColumn = new(rect.x, rect.y, leftWidth, rect.height);
            DrawPlayerColumn(leftColumn);

            Rect centerColumn = new(leftColumn.xMax, rect.y, centerWidth, rect.height);
            DrawSummaryColumn(centerColumn);

            Rect rightColumn = new(centerColumn.xMax, rect.y, rightWidth, rect.height);
            DrawTraderColumn(rightColumn);
        }

        private void DrawPlayerColumn(Rect rect)
        {
            Rect headerRect = new(rect.x, rect.y, rect.width, 30);
            Widgets.DrawBoxSolid(headerRect, ColHeaderBg);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ColAccentCamo1;
            Widgets.Label(headerRect, "USAC.Trade.PlayerItems".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            Rect listRect = new(rect.x, rect.y + 35, rect.width, rect.height - 35);
            Widgets.DrawBoxSolidWithOutline(listRect, new Color(0, 0, 0, 0.2f), ColBorder);
            Rect innerRect = listRect.ContractedBy(5);
            Rect columnHeaderRect = new(innerRect.x, innerRect.y, innerRect.width, 20);
            DrawPlayerColumnHeaders(columnHeaderRect);

            var playerItems = filteredTradeables.Where(t =>
                t.CountHeldBy(Transactor.Colony) > 0 || t.CountToTransfer < 0).ToList();
            ApplySort(playerItems, playerSortColumn, playerSortAscending, true);

            float viewHeight = playerItems.Count * 50f + 25f;
            Rect viewRect = new(0, 0, innerRect.width - 16f, viewHeight);

            Rect scrollRect = new(innerRect.x, innerRect.y + 25, innerRect.width, innerRect.height - 25);
            Widgets.BeginScrollView(scrollRect, ref scrollPositionLeft, viewRect);

            float y = 5f;
            for (int i = 0; i < playerItems.Count; i++)
            {
                Rect rowRect = new(0, y, viewRect.width, 45);
                USACTradePanel.DrawPlayerItemRow(rowRect, playerItems[i], i, CountToTransferChanged);
                y += 50f;
            }

            Widgets.EndScrollView();
        }

        private void DrawSummaryColumn(Rect rect)
        {
            USACTradeSummary.Draw(rect, cachedCurrencyTradeable, CountToTransferChanged);
        }

        private void DrawTraderColumn(Rect rect)
        {
            Rect headerRect = new(rect.x, rect.y, rect.width, 30);
            Widgets.DrawBoxSolid(headerRect, ColHeaderBg);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ColAccentCamo1;
            Widgets.Label(headerRect, "USAC.Trade.TraderItems".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            Rect listRect = new(rect.x, rect.y + 35, rect.width, rect.height - 35);
            Widgets.DrawBoxSolidWithOutline(listRect, new Color(0, 0, 0, 0.2f), ColBorder);
            Rect innerRect = listRect.ContractedBy(5);
            Rect columnHeaderRect = new(innerRect.x, innerRect.y, innerRect.width, 20);
            DrawTraderColumnHeaders(columnHeaderRect);

            var traderItems = filteredTradeables.Where(t =>
                t.CountHeldBy(Transactor.Trader) > 0 || t.CountToTransfer > 0).ToList();
            ApplySort(traderItems, traderSortColumn, traderSortAscending, false);

            float viewHeight = traderItems.Count * 50f + 25f;
            Rect viewRect = new(0, 0, innerRect.width - 16f, viewHeight);

            Rect scrollRect = new(innerRect.x, innerRect.y + 25, innerRect.width, innerRect.height - 25);
            Widgets.BeginScrollView(scrollRect, ref scrollPositionRight, viewRect);

            float y = 5f;
            for (int i = 0; i < traderItems.Count; i++)
            {
                Rect rowRect = new(0, y, viewRect.width, 45);
                USACTradePanel.DrawTraderItemRow(rowRect, traderItems[i], i, CountToTransferChanged);
                y += 50f;
            }

            Widgets.EndScrollView();
        }

        private void DrawFooter(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, ColHeaderBg);
            GUI.color = ColAccentCamo3;
            Widgets.DrawLineHorizontal(0, rect.y, rect.width);
            GUI.color = Color.white;

            float buttonWidth = 150f;
            float buttonHeight = 40f;
            float centerY = rect.y + (rect.height - buttonHeight) / 2f;
            float centerX = rect.width / 2f;
            float optionsX = 30f;
            float rowHeight = 24f;
            Text.Font = GameFont.Small;
            float enableBondsWidth = Text.CalcSize("USAC.Trade.EnableBonds".Translate()).x + 40f;
            float priorityBondsWidth = Text.CalcSize("USAC.Trade.PriorityBonds".Translate()).x + 40f;
            float optionWidth = Mathf.Max(200f, Mathf.Max(enableBondsWidth, priorityBondsWidth));
            Rect useBondsRect = new(optionsX, rect.y + 10, optionWidth, rowHeight);
            bool enableBonds = Tradeable_USACCurrency.EnableBondsForPayment;
            DrawTacticalCheckbox(useBondsRect, "USAC.Trade.EnableBonds".Translate(), ref enableBonds, false, "enable_bonds");
            if (enableBonds != Tradeable_USACCurrency.EnableBondsForPayment)
            {
                Tradeable_USACCurrency.EnableBondsForPayment = enableBonds;
                CountToTransferChanged();
            }
            Rect priorityBondsRect = new(optionsX, rect.y + 36, optionWidth, rowHeight);
            bool priorityBonds = Tradeable_USACCurrency.UseBondsForPayment;
            DrawTacticalCheckbox(priorityBondsRect, "USAC.Trade.PriorityBonds".Translate(), ref priorityBonds, !enableBonds, "priority_bonds");
            if (priorityBonds != Tradeable_USACCurrency.UseBondsForPayment)
            {
                Tradeable_USACCurrency.UseBondsForPayment = priorityBonds;
                CountToTransferChanged();
            }
            if (DrawTacticalButton(
                new Rect(centerX - buttonWidth - 10, centerY, buttonWidth, buttonHeight),
                "ResetButton".Translate(), true, GameFont.Small, "reset_trade"))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                foreach (var tradeable in cachedTradeables)
                {
                    tradeable.AdjustTo(0);
                }
                CountToTransferChanged();
            }
            string acceptLabel = giftsOnly
                ? "OfferGifts".Translate()
                : "AcceptButton".Translate();

            if (DrawTacticalButton(
                new Rect(centerX + 10, centerY, buttonWidth, buttonHeight),
                acceptLabel, true, GameFont.Small, "accept_trade"))
            {
                if (IsPlayerMoneyEnough())
                {
                    ExecuteTrade();
                }
                else
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    Messages.Message("USAC.Trade.NotEnoughCurrency".Translate(), MessageTypeDefOf.RejectInput);
                }
            }
        }

        private bool IsPlayerMoneyEnough()
        {
            if (giftsOnly) return true;
            float totalBuy = 0f;
            float totalSell = 0f;

            foreach (var trad in TradeSession.deal.AllTradeables)
            {
                if (trad.IsCurrency) continue;

                if (trad.CountToTransfer > 0)
                    totalBuy += trad.CurTotalCurrencyCostForSource;
                else if (trad.CountToTransfer < 0)
                    totalSell += trad.CurTotalCurrencyCostForDestination;
            }

            float netCost = totalBuy - totalSell;
            if (netCost <= 0) return true;
            if (cachedCurrencyTradeable != null)
            {
                int available = cachedCurrencyTradeable.CountHeldBy(Transactor.Colony);
                return (available >= netCost);
            }

            return false;
        }
        #endregion

        #region 辅助方法
        private bool DrawSortableHeader(Rect rect, string label, bool isActive, bool ascending)
        {
            bool clicked = false;

            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
                if (Widgets.ButtonInvisible(rect))
                {
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    clicked = true;
                }
            }
            Text.Font = GameFont.Tiny;
            float labelWidth = Text.CalcSize(label).x;
            if (labelWidth > rect.width - (isActive ? 12 : 0))
            {
                Text.Font = GameFont.Tiny;
            }
            GUI.color = isActive ? ColAccentCamo1 : ColAccentCamo3;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.WordWrap = false;
            Rect labelRect = rect;
            if (isActive)
            {
                labelRect.xMax -= 8;
            }
            Widgets.Label(labelRect, label);
            if (isActive)
            {
                Text.Anchor = TextAnchor.MiddleRight;
                string arrow = ascending ? "▲" : "▼";
                Rect arrowRect = new(rect.xMax - 14, rect.y, 12, rect.height);
                Widgets.Label(arrowRect, arrow);
            }

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;

            return clicked;
        }

        private void ApplySort(List<Tradeable> list, SortColumn column, bool ascending, bool isPlayerSide)
        {
            if (list == null || list.Count == 0)
                return;

            switch (column)
            {
                case SortColumn.Name:
                    list.Sort((a, b) =>
                    {
                        int result = string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase);
                        return ascending ? result : -result;
                    });
                    break;

                case SortColumn.Count:
                    list.Sort((a, b) =>
                    {
                        Transactor trans = isPlayerSide ? Transactor.Colony : Transactor.Trader;
                        int countA = a.CountHeldBy(trans);
                        int countB = b.CountHeldBy(trans);
                        int result = countA.CompareTo(countB);
                        return ascending ? result : -result;
                    });
                    break;

                case SortColumn.Price:
                    list.Sort((a, b) =>
                    {
                        TradeAction action = isPlayerSide ? TradeAction.PlayerSells : TradeAction.PlayerBuys;
                        float priceA = a.TraderWillTrade ? a.GetPriceFor(action) : 0f;
                        float priceB = b.TraderWillTrade ? b.GetPriceFor(action) : 0f;
                        int result = priceA.CompareTo(priceB);
                        return ascending ? result : -result;
                    });
                    break;
            }
        }
        private void DrawPlayerColumnHeaders(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = ColAccentCamo3;

            float x = rect.x;
            float rowInnerWidth = rect.width - 16f;
            float nameWidth = USACTradePanel.CalcNameWidth(rowInnerWidth);
            x += USACTradePanel.COL_ICON;
            Rect nameRect = new(x, rect.y, nameWidth - USACTradePanel.COL_SPACING, rect.height);
            if (DrawSortableHeader(nameRect, "USAC.Trade.Header.Item".Translate(), playerSortColumn == SortColumn.Name, playerSortAscending))
            {
                if (playerSortColumn == SortColumn.Name)
                    playerSortAscending = !playerSortAscending;
                else
                {
                    playerSortColumn = SortColumn.Name;
                    playerSortAscending = true;
                }
                ApplySearchFilter();
            }
            x += nameWidth;
            Rect countRect = new(x, rect.y, USACTradePanel.COL_COUNT - USACTradePanel.COL_SPACING, rect.height);
            if (DrawSortableHeader(countRect, "USAC.Trade.Header.Held".Translate(), playerSortColumn == SortColumn.Count, playerSortAscending))
            {
                playerSortColumn = SortColumn.Count;
                playerSortAscending = !playerSortAscending;
                ApplySearchFilter();
            }
            x += USACTradePanel.COL_COUNT;
            Rect priceRect = new(x, rect.y, USACTradePanel.COL_PRICE - USACTradePanel.COL_SPACING, rect.height);
            if (DrawSortableHeader(priceRect, "USAC.Trade.Header.Price".Translate(), playerSortColumn == SortColumn.Price, playerSortAscending))
            {
                playerSortColumn = SortColumn.Price;
                playerSortAscending = !playerSortAscending;
                ApplySearchFilter();
            }

            GUI.color = Color.white;
        }

        private void DrawTraderColumnHeaders(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = ColAccentCamo3;

            float x = rect.x;
            float rowInnerWidth = rect.width - 16f;
            float nameWidth = USACTradePanel.CalcNameWidth(rowInnerWidth);
            x += USACTradePanel.COL_ADJUSTER;
            Rect priceRect = new(x, rect.y, USACTradePanel.COL_PRICE - USACTradePanel.COL_SPACING, rect.height);
            if (DrawSortableHeader(priceRect, "USAC.Trade.Header.Price".Translate(), traderSortColumn == SortColumn.Price, traderSortAscending))
            {
                traderSortColumn = SortColumn.Price;
                traderSortAscending = !traderSortAscending;
                ApplySearchFilter();
            }
            x += USACTradePanel.COL_PRICE;
            Rect countRect = new(x, rect.y, USACTradePanel.COL_COUNT - USACTradePanel.COL_SPACING, rect.height);
            if (DrawSortableHeader(countRect, "USAC.Trade.Header.Stock".Translate(), traderSortColumn == SortColumn.Count, traderSortAscending))
            {
                traderSortColumn = SortColumn.Count;
                traderSortAscending = !traderSortAscending;
                ApplySearchFilter();
            }
            x += USACTradePanel.COL_COUNT;
            Rect nameRect = new(x, rect.y, nameWidth - USACTradePanel.COL_SPACING, rect.height);
            if (DrawSortableHeader(nameRect, "USAC.Trade.Header.Item".Translate(), traderSortColumn == SortColumn.Name, traderSortAscending))
            {
                if (traderSortColumn == SortColumn.Name)
                    traderSortAscending = !traderSortAscending;
                else
                {
                    traderSortColumn = SortColumn.Name;
                    traderSortAscending = true;
                }
                ApplySearchFilter();
            }

            GUI.color = Color.white;
        }

        private void CountToTransferChanged()
        {
            TradeSession.deal.UpdateCurrencyCount();
            USACTradeSummary.Refresh(cachedTradeables, cachedCurrencyTradeable);

            if (Prefs.DevMode)
            {
                var activeTradeables = cachedTradeables?.Where(t => t.CountToTransfer != 0).ToList();
                if (activeTradeables?.Any() == true)
                {
                    // Log.Message($"[USAC] 当前交易状态: {activeTradeables.Count} 个物品有交易数量");
                }
            }
        }
        private void ExecuteTrade()
        {
            System.Action action = delegate
            {
                if (TradeSession.deal.TryExecute(out var actuallyTraded))
                {
                    if (actuallyTraded)
                    {
                        SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();
                        TradeSession.playerNegotiator.GetCaravan()?.RecacheInventory();
                        Close(doCloseSound: false);

                        // 启动交付流程
                        USAC_MechTradeUtility.StartDeliveryProcess();
                    }
                    else
                    {
                        Close();
                    }
                }
            };

            if (TradeSession.deal.DoesTraderHaveEnoughSilver())
            {
                action();
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "ConfirmTraderShortFunds".Translate(), action));
            }
        }

        private void CacheTradeables()
        {
            cachedCurrencyTradeable = TradeSession.deal.AllTradeables
                .FirstOrDefault(x => x.IsCurrency);

            cachedTradeables = (from tr in TradeSession.deal.AllTradeables
                                where !tr.IsCurrency
                                where tr.TraderWillTrade || !TradeSession.trader.TraderKind.hideThingsNotWillingToTrade
                                orderby (!tr.TraderWillTrade) ? (-1) : 0 descending
                                select tr).ToList();

            USACTradeSummary.Refresh(cachedTradeables, cachedCurrencyTradeable);
        }

        private void ApplySearchFilter()
        {
            if (cachedTradeables == null)
            {
                filteredTradeables = new List<Tradeable>();
                return;
            }

            if (!quickSearchWidget.filter.Active)
            {
                filteredTradeables = cachedTradeables.ToList();
            }
            else
            {
                filteredTradeables = new List<Tradeable>();

                foreach (var tradeable in cachedTradeables)
                {
                    if (MatchesSearch(tradeable))
                        filteredTradeables.Add(tradeable);
                }

                // 更新搜索结果状态
                quickSearchWidget.noResultsMatched = filteredTradeables.Count == 0;
            }
        }

        private bool MatchesSearch(Tradeable tradeable)
        {
            // 搜索物品名称
            if (quickSearchWidget.filter.Matches(tradeable.Label.ToString()))
                return true;

            // 搜索物品描述
            if (!string.IsNullOrEmpty(tradeable.ThingDef?.description) &&
                quickSearchWidget.filter.Matches(tradeable.ThingDef.description))
                return true;

            // 搜索分类
            if (tradeable.ThingDef?.thingCategories != null)
            {
                foreach (var category in tradeable.ThingDef.thingCategories)
                {
                    if (quickSearchWidget.filter.Matches(category.LabelCap.ToString()))
                        return true;
                }
            }

            return false;
        }

        private static Texture2D cachedGridTex;
        private static int cachedGridW, cachedGridH;

        // 获取或创建网格纹理
        private static Texture2D GetOrCreateGridTex(int w, int h)
        {
            if (cachedGridTex != null && cachedGridW == w && cachedGridH == h) return cachedGridTex;
            if (cachedGridTex != null) UnityEngine.Object.Destroy(cachedGridTex);
            cachedGridTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color gridCol = new(1f, 1f, 1f, 0.03f);
            Color[] pixels = new Color[w * h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    pixels[y * w + x] = (x % 50 == 0 || y % 50 == 0) ? gridCol : Color.clear;
            cachedGridTex.SetPixels(pixels);
            cachedGridTex.Apply();
            cachedGridW = w; cachedGridH = h;
            return cachedGridTex;
        }

        private void DrawBackgroundGrid(Rect rect)
        {
            GUI.DrawTexture(rect, GetOrCreateGridTex((int)rect.width, (int)rect.height));

            var logo = ContentFinder<Texture2D>.Get("UI/StyleCategories/USACIcon", false);
            if (logo != null)
            {
                float logoSize = 460f;
                Vector2 center = new(rect.width / 2f, rect.height / 2f);
                GUI.color = new Color(1, 1, 1, 0.15f);
                GUI.DrawTexture(new Rect(center.x - logoSize / 2f, center.y - logoSize / 2f, logoSize, logoSize), logo);
                GUI.color = Color.white;
            }
        }

        public override bool CausesMessageBackground()
        {
            return true;
        }
        #endregion
    }
}
