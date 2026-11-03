using System.Collections.Generic;
using System.Linq;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Zoning;
using UnityEngine;

namespace CityBuilder.Game.Inspect
{
    /// <summary>Texte des Inspektors (aus dem früheren IMGUI-Inspektor übernommen). Reine Formatierung, liest nur.</summary>
    public static class InspectorTexts
    {
        public static string BuildingName(Building b) => $"{Zones.DisplayName(b.Zone)} #{b.Id}";

        public static string UseName(ZoneType z)
        {
            switch (z)
            {
                case ZoneType.ResidentialLow:
                case ZoneType.ResidentialMedium:
                case ZoneType.ResidentialHigh: return "Wohngebäude";
                case ZoneType.Mixed: return "Wohnen + Läden";
                case ZoneType.Retail: return "Geschäft";
                case ZoneType.Office: return "Bürogebäude";
                case ZoneType.Industry: return "Industriebetrieb";
                default: return "Gebäude";
            }
        }

        /// <summary>Kurzbeschreibung eines Bürgers für Listen: „34 J. · arbeitet in #12 · zu Hause“.</summary>
        public static string Describe(Citizen c)
        {
            string job = c.WorkplaceId != 0 ? $"arbeitet in #{c.WorkplaceId}" : c.WorksOutside ? "Auspendler" :
                c.IsWorkingAge ? "arbeitslos" : StageName(c);
            return $"{c.Age} J. · {job} · {ActivityName(c)}";
        }

        public static string StageName(Citizen c)
        {
            switch (c.Stage)
            {
                case LifeStage.Child: return "Kind";
                case LifeStage.Student: return "Schüler";
                case LifeStage.Senior: return "Rente";
                default: return "Erwachsen";
            }
        }

        public static string ActivityName(Citizen c)
        {
            switch (c.Activity)
            {
                case Activity.Home: return "zu Hause";
                case Activity.Work: return "bei der Arbeit";
                case Activity.Shopping: return "einkaufen";
                case Activity.Travelling: return c.Mode == TravelMode.Car ? "im Auto unterwegs" : c.Mode == TravelMode.Transit ? "mit dem Bus unterwegs" : "unterwegs";
                case Activity.Leisure: return c.LeisureParkId > 0 ? "im Park" : "Spaziergang im Viertel";
                default: return "außerhalb";
            }
        }

        public static string PurposeName(TripPurpose p)
        {
            switch (p)
            {
                case TripPurpose.ToWork: return "zur Arbeit";
                case TripPurpose.ToShop: return "zum Einkaufen";
                case TripPurpose.ToHome: return "nach Hause";
                default: return "–";
            }
        }

        public static string EducationName(Education e)
        {
            switch (e)
            {
                case Education.None: return "ohne";
                case Education.Basic: return "Grund";
                case Education.Secondary: return "mittel";
                default: return "hoch";
            }
        }

        /// <summary>Dauer in Ticks (1 Tick = 6 Spielminuten) als Text.</summary>
        public static string Duration(long ticks)
        {
            if (ticks < 0) ticks = 0;
            long minutes = ticks * 6;
            if (minutes < 60) return $"{minutes} min";
            if (minutes < 60 * 24) return $"{minutes / 60} h {minutes % 60:00} min";
            return $"{minutes / (60 * 24)} T {minutes / 60 % 24} h";
        }

        /// <summary>Uhrzeit eines Ticks bzw. eines Ticks im Tag.</summary>
        public static string Clock(long tick)
        {
            long tod = ((tick % SimulationClock.TicksPerDay) + SimulationClock.TicksPerDay) % SimulationClock.TicksPerDay;
            long minutes = tod * 6;
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }

        public static string FormatSeconds(float s)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(s));
            return total < 60 ? $"{total} s" : $"{total / 60} min {total % 60:00} s";
        }

        public static string Percent(float v) => $"{v * 100f:0} %";

        /// <summary>Deutlich ausgeprägte Züge (|Wert − 0,5| ≥ 0,2) als Namen; leer = ausgeglichen.</summary>
        public static List<string> Traits(Personality p) =>
            Personality.AllTraits
                .Where(t => Mathf.Abs(p[t] - 0.5f) >= 0.2f)
                .Select(t => p[t] >= 0.5f ? Personality.DisplayName(t) : Personality.OppositeName(t))
                .ToList();

        /// <summary>Zufriedenheit als Text; negativ = niemand da.</summary>
        public static string SatisfactionValue(float s) =>
            s < 0f ? "– (niemand da)" : $"{CitizenSystem.SatisfactionText(s)} ({s * 100f:0} %)";
    }
}
