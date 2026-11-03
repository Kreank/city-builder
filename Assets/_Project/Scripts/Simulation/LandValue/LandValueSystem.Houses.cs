using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.LandValue
{
    /// <summary>Vorschau an einem Bauplatz: welche Anforderungen dort erfüllt wären und welche Stufe erreichbar ist.</summary>
    public sealed class HousePreview
    {
        /// <summary>Alle Anforderungen aller Stufen (Reihenfolge wie <see cref="HouseRules.AllNeeds"/>).</summary>
        public readonly List<HouseNeedStatus> Needs = new List<HouseNeedStatus>();
        /// <summary>Höchste Stufe, deren Anforderungen hier erfüllt wären (weiche Bedingungen nicht eingerechnet).</summary>
        public int ReachableLevel = 1;
        public string MissingText => HouseRules.MissingText(Needs);
    }

    /// <summary>
    /// Wohnhäuser setzen (Anno-Stil): Auswertung der Anforderungen aus <see cref="HouseRules"/> für gesetzte Häuser –
    /// Versorgung über <c>world.Utilities</c>, Dienste über die verbindliche API <c>world.Services.Coverage</c>, Laden über
    /// die Gebäudeliste + <c>world.Goods.ShopHasStock</c>. Zonengebäude bleiben bei den G6-Regeln.
    /// </summary>
    public sealed partial class LandValueSystem
    {
        /// <summary>
        /// Hook für Tests/Szenarien: erzwingt das Ergebnis einer Anforderung (null = echt auswerten).
        /// </summary>
        public Func<Building, HouseNeed, bool?> HouseNeedOverride { get; set; }

        /// <summary>Stand einer Anforderung an einem bestehenden (gesetzten oder gewachsenen) Haus.</summary>
        public HouseNeedStatus EvaluateNeed(Building b, HouseNeed need) => Evaluate(b, need, false);

        /// <summary>Alle (kumulierten) Anforderungen für Stufe <paramref name="level"/> mit ✓/✗.</summary>
        public List<HouseNeedStatus> EvaluateNeeds(Building b, int level)
        {
            var list = new List<HouseNeedStatus>();
            foreach (var n in HouseRules.NeedsFor(level)) list.Add(Evaluate(b, n, false));
            return list;
        }

        /// <summary>Vorschau an einem geplanten Bauplatz (Werkzeug): alle Anforderungen + erreichbare Stufe.</summary>
        public HousePreview PreviewHouse(HousePlan plan)
        {
            var p = new HousePreview();
            if (plan == null || plan.Type == null) return p;
            var temp = new Building
            {
                Id = -1, Zone = plan.Type.Zone, Origin = BuildingOrigin.Player,
                WidthCells = plan.Type.WidthCells, DepthCells = plan.Type.DepthCells,
                Position = plan.Position, Front = plan.Front, Height = plan.Height,
                FrontSegmentId = plan.AccessSegmentId, FrontT = plan.AccessT, Construction = 1f,
            };
            foreach (var n in HouseRules.AllNeeds()) p.Needs.Add(Evaluate(temp, n, true));
            p.ReachableLevel = HouseRules.ReachableLevel(p.Needs);
            return p;
        }

        static bool AllMet(List<HouseNeedStatus> list)
        {
            foreach (var s in list) if (!s.Met) return false;
            return true;
        }

        static bool SoftMet(HouseLevelRule rule, in BuildingFacts f) =>
            rule == null || (f.Satisfaction >= rule.Satisfaction && f.Occupancy >= rule.Occupancy);

        HouseNeedStatus Evaluate(Building b, HouseNeed need, bool preview)
        {
            var s = new HouseNeedStatus { Need = need };
            var forced = preview ? null : HouseNeedOverride?.Invoke(b, need);
            if (forced.HasValue)
            {
                s.Met = forced.Value;
                s.Value = s.Met ? 1f : 0f;
                s.Detail = s.Met ? "erfüllt" : "fehlt";
                return s;
            }
            switch (need.Kind)
            {
                case HouseNeedKind.Power: return Supply(b, UtilityKind.Power, s, preview);
                case HouseNeedKind.Water: return Supply(b, UtilityKind.Water, s, preview);
                case HouseNeedKind.Sewage: return Supply(b, UtilityKind.Sewage, s, preview);
                case HouseNeedKind.Shop: return ShopNear(b, s);
                default: return ServiceNear(b, need, s, preview);
            }
        }

        HouseNeedStatus Supply(Building b, UtilityKind kind, HouseNeedStatus s, bool preview)
        {
            var u = world.Utilities;
            if (u == null) { s.Detail = "keine Versorgung"; return s; }
            var line = u.ConnectedLine(b, kind);
            if (preview)
            {
                // Bauplatz: zählt, ob eine Leitung in Anschlussweite liegt (ob das Netz reicht, zeigt sich nach dem Bau)
                s.Met = line != null;
                s.Value = s.Met ? 1f : 0f;
                s.Detail = s.Met ? "Leitung in Reichweite" : $"keine Leitung in {ConnectionRules.BuildingReach:0} m";
                return s;
            }
            s.Met = u.IsSupplied(b, kind);
            s.Value = s.Met ? 1f : 0f;
            s.Detail = s.Met ? "versorgt" : line == null ? "kein Anschluss" : "Netz liefert nicht genug";
            return s;
        }

        HouseNeedStatus ShopNear(Building b, HouseNeedStatus s)
        {
            var goods = world.Goods;
            var from = b.Entrance;
            float best = float.MaxValue, bestEmpty = float.MaxValue;
            foreach (var o in world.City.Buildings)
            {
                if (o.Id == b.Id || !o.IsConstructed || (o.Zone != ZoneType.Retail && o.Zone != ZoneType.Mixed)) continue;
                float d = Vector2.Distance(from, o.Entrance);
                if (goods != null && !goods.ShopHasStock(o.Id)) { bestEmpty = MathF.Min(bestEmpty, d); continue; }
                best = MathF.Min(best, d);
            }
            s.Value = best < float.MaxValue ? best : -1f;
            s.Met = best <= HouseRules.ShopWalkRadius;
            if (s.Met) s.Detail = $"Laden in {best:0} m";
            else if (bestEmpty <= HouseRules.ShopWalkRadius) s.Detail = "Laden in der Nähe hat keine Ware";
            else if (best < float.MaxValue) s.Detail = $"nächster Laden {best:0} m (Fußweite {HouseRules.ShopWalkRadius:0} m)";
            else s.Detail = "kein Laden in der Stadt";
            return s;
        }

        HouseNeedStatus ServiceNear(Building b, HouseNeed need, HouseNeedStatus s, bool preview)
        {
            var svc = world.Services;
            float best = 0f;
            if (svc != null)
                foreach (var kind in need.ServiceKinds)
                    best = MathF.Max(best, preview ? svc.CoverageAt(kind, b.Entrance) : svc.Coverage(kind, b));
            s.Value = best;
            s.Met = best >= need.Threshold;
            s.Detail = best <= 0f ? "außer Reichweite" : $"Abdeckung {best * 100f:0} % (nötig {need.Threshold * 100f:0} %)";
            return s;
        }

        /// <summary>Tagesauswertung eines gesetzten Hauses: Aufstieg nach Anforderungen, Abstieg, wenn die eigene Stufe nicht mehr versorgt ist.</summary>
        void EvaluateHouse(Building b, LevelState st, in BuildingFacts f)
        {
            int max = HouseRules.MaxLevel;
            if (b.Level < max)
            {
                var rule = HouseRules.RuleFor(b.Level + 1);
                bool hard = AllMet(EvaluateNeeds(b, b.Level + 1));
                st.Progress = HouseRules.NextProgress(st.Progress, hard, SoftMet(rule, f));
                if (st.Progress >= rule.Days)
                {
                    SetLevel(b, b.Level + 1);
                    b.Condition = 1f; // Ausbau = Modernisierung (wie G6)
                    st.Progress = 0;
                    st.Decline = 0;
                    Upgrades++;
                    return;
                }
            }
            if (b.Level > 1)
            {
                bool bad = !AllMet(EvaluateNeeds(b, b.Level));
                st.Decline = LandValueRules.NextProgress(st.Decline, bad);
                if (st.Decline >= HouseRules.DeclineDays)
                {
                    SetLevel(b, b.Level - 1);
                    st.Decline = 0;
                    st.Progress = 0;
                    Downgrades++;
                }
            }
        }

        BuildingLevelInfo HouseLevelInfo(Building b, BuildingLevelInfo info)
        {
            int max = HouseRules.MaxLevel;
            info.PlayerHouse = true;
            info.MaxLevel = max;
            info.DeclineDaysLimit = HouseRules.DeclineDays;
            info.Level = b.Level;
            info.Condition = b.Condition;
            info.ConditionText = LandValueRules.ConditionText(b.Condition);
            info.Facts = FactsOf(b);
            states.TryGetValue(b.Id, out var st);
            info.ProgressDays = st?.Progress ?? 0;
            info.DeclineDays = st?.Decline ?? 0;
            info.EmptyDays = st?.EmptyDays ?? 0;
            info.Declining = info.DeclineDays > 0;
            bool top = b.Level >= max;
            int target = top ? b.Level : b.Level + 1;
            var rule = HouseRules.RuleFor(target);
            info.Needs = EvaluateNeeds(b, target);
            info.OwnNeedsMissing = b.Level > 1 && !AllMet(EvaluateNeeds(b, b.Level));
            info.NeedSatisfaction = rule?.Satisfaction ?? 0f;
            info.NeedOccupancy = rule?.Occupancy ?? 0f;
            info.DaysNeeded = top ? 0 : rule.Days;
            info.Next = new LevelRequirement
            {
                LandValue = 0f, Satisfaction = info.NeedSatisfaction, Occupancy = info.NeedOccupancy, Condition = 0f, Days = info.DaysNeeded,
            };

            var parts = new List<string>();
            var gap = LevelGap.None;
            foreach (var s in info.Needs) if (!s.Met) { gap |= LevelGap.Needs; parts.Add(s.Need.Label); }
            if (!top && rule != null)
            {
                var f = info.Facts;
                if (f.Satisfaction < rule.Satisfaction)
                {
                    gap |= LevelGap.Satisfaction;
                    parts.Add(f.Satisfaction < 0f ? "Bewohner" : $"Zufriedenheit der Bewohner ({f.Satisfaction * 100f:0} % von {rule.Satisfaction * 100f:0} %)");
                }
                if (f.Occupancy < rule.Occupancy)
                {
                    gap |= LevelGap.Occupancy;
                    parts.Add($"Belegung ({f.Occupancy * 100f:0} % von {rule.Occupancy * 100f:0} %)");
                }
            }
            if (!b.IsConstructed) { info.Missing = LevelGap.UnderConstruction; info.MissingText = "im Bau"; }
            else if (top)
            {
                info.Missing = LevelGap.MaxLevel | (gap & LevelGap.Needs);
                info.MissingText = (gap & LevelGap.Needs) != 0 ? "höchste Stufe · " + string.Join(", ", parts) + " fehlt (Abstieg droht)" : "höchste Stufe";
            }
            else
            {
                info.Missing = gap;
                info.MissingText = parts.Count == 0 ? "alles erfüllt" : "Es fehlt: " + string.Join(", ", parts);
            }
            info.Progress = top ? 1f : Math.Clamp(info.ProgressDays / (float)Math.Max(1, info.DaysNeeded), 0f, 1f);
            return info;
        }
    }
}
