using System;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Traffic
{
    [Flags]
    public enum VehicleClass
    {
        None = 0,
        Car = 1,
        Truck = 2,
        Bus = 4,
        Tram = 8,
        Road = Car | Truck | Bus,
        All = Car | Truck | Bus | Tram,
    }

    public static class VehicleClasses
    {
        /// <summary>Wer darf diese Spur benutzen?</summary>
        public static VehicleClass AllowedOn(LaneKind kind)
        {
            switch (kind)
            {
                case LaneKind.Car: return VehicleClass.Road;
                case LaneKind.Bus: return VehicleClass.Bus;
                case LaneKind.Tram: return VehicleClass.Tram;
                default: return VehicleClass.None;
            }
        }

        public static float Length(VehicleClass c)
        {
            switch (c)
            {
                case VehicleClass.Truck: return 12f;
                case VehicleClass.Bus: return 12f;
                case VehicleClass.Tram: return 30f;
                default: return 4.5f;
            }
        }

        /// <summary>Höchstgeschwindigkeit in m/s.</summary>
        public static float MaxSpeed(VehicleClass c)
        {
            switch (c)
            {
                case VehicleClass.Truck: return 90f / 3.6f;
                case VehicleClass.Bus: return 80f / 3.6f;
                case VehicleClass.Tram: return 60f / 3.6f;
                default: return 140f / 3.6f;
            }
        }

        public static float Acceleration(VehicleClass c) => c == VehicleClass.Car ? 2.2f : 1.1f;

        public static string DisplayName(VehicleClass c)
        {
            switch (c)
            {
                case VehicleClass.Car: return "Pkw";
                case VehicleClass.Truck: return "Lkw";
                case VehicleClass.Bus: return "Bus";
                case VehicleClass.Tram: return "Tram";
                default: return c.ToString();
            }
        }
    }
}
