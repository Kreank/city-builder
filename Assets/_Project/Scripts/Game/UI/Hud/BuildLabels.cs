using System.Globalization;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using UnityEngine;

namespace CityBuilder.Game.UI.Hud
{
    /// <summary>Kurznamen, Beschreibungen und Zahlenformat (deutsch) für Bauleiste und HUD.</summary>
    public static class BuildLabels
    {
        /// <summary>Ganzzahl mit Tausenderpunkt (12.480) – ohne Abhängigkeit von installierten Kulturdaten.</summary>
        /// <summary>Geldbetrag kurz: „12.480 €“, ab 1 Mio „1,24 Mio €“.</summary>
        public static string Money(double value) =>
            System.Math.Abs(value) >= 1_000_000
                ? (value / 1_000_000).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',') + " Mio €"
                : Number((float)value) + " €";

        public static string Number(float value) =>
            Mathf.RoundToInt(value).ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.');

        public static string Money(float value) => Number(value) + " €";

        public static string RoadName(RoadType t)
        {
            switch (t.Id)
            {
                case "city_2": return "2-spurig";
                case "city_4": return "4-spurig";
                case "city_oneway_1": return "Einbahn 1";
                case "city_oneway_2": return "Einbahn 2";
                case "city_2_bus": return "Busspuren";
                case "city_oneway_bus": return "Einbahn + Bus";
                case "city_2_tram": return "Tram";
                case "city_oneway_tram": return "Einbahn + Tram";
                case "highway_2": return "Autobahn 2";
                case "highway_3": return "Autobahn 3";
                case "ramp_1": return "Auf-/Abfahrt";
                default: return t.DisplayName;
            }
        }

        public static string DescribeRoad(RoadType t)
        {
            string dir = t.IsOneWay ? "Einbahn" : $"{t.CarLanesBackward}+{t.CarLanesForward} Spuren";
            string extras = (t.HasBusLanes ? " · Busspur" : "") + (t.HasTram ? " · Tram" : "");
            return $"{t.DisplayName} – {dir}{extras} · {t.Width:0.#} m · {t.SpeedLimitKmh:0} km/h · {t.CostPerMeter:0} €/m · max. {t.MaxSlope * 100f:0} % Steigung";
        }

        public static string ZoneName(ZoneType z)
        {
            switch (z)
            {
                case ZoneType.ResidentialLow: return "Wohnen gering";
                case ZoneType.ResidentialMedium: return "Wohnen mittel";
                case ZoneType.ResidentialHigh: return "Wohnen dicht";
                case ZoneType.Mixed: return "Mischgebiet";
                case ZoneType.Retail: return "Einzelhandel";
                case ZoneType.Office: return "Büro";
                case ZoneType.Industry: return "Industrie";
                default: return "Entzonen";
            }
        }

        /// <summary>Zonenfarben laut Stilbogen (Wohnen grün, Gewerbe blau, Industrie gelb, Büro violett).</summary>
        public static Color ZoneColor(ZoneType z)
        {
            switch (z)
            {
                case ZoneType.ResidentialLow:
                case ZoneType.ResidentialMedium:
                case ZoneType.ResidentialHigh: return Hex(0x5DBB63);
                case ZoneType.Mixed: return Hex(0x54A69E); // zwischen Wohnen und Gewerbe
                case ZoneType.Retail: return Hex(0x4A90D9);
                case ZoneType.Office: return Hex(0x8C6BD1);
                case ZoneType.Industry: return Hex(0xE0A030);
                default: return Hex(0x5A5E6B);
            }
        }

        public static string FacilityName(UtilityFacilityType t)
        {
            switch (t.Id)
            {
                case "power_coal": return "Kohlekraftwerk";
                case "power_gas": return "Gaskraftwerk";
                case "power_wind": return "Windrad";
                case "power_solar": return "Solarpark";
                case "water_pump": return "Pumpe (Ufer)";
                case "water_tower": return "Wasserturm";
                default: return t.DisplayName;
            }
        }

        public static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
