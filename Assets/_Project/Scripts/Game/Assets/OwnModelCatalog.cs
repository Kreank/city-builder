using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilder.Game.Assets
{
    /// <summary>
    /// Eigene Modelle (Blender, <c>Assets/_Project/Models</c>, im Git) – getrennt vom Store-Katalog in ThirdParty.
    /// Wird vom Editor automatisch neu gebaut, sobald sich dort ein Modell oder eine Beschreibungsdatei (<c>Name.json</c>)
    /// ändert (<c>CityBuilder.Editor.OwnModelCatalogBuilder</c>), liegt in <c>Assets/_Project/Resources/OwnModels.asset</c>.
    /// Konvention der Modelle: Ursprung = Mitte der Grundfläche auf Bodenhöhe, Vorderseite +Z, 1 Einheit = 1 m,
    /// ein gemeinsames Paletten-Material. Bewegliche Teile sind eigene benannte Kindobjekte (z. B. „Rotor“).
    /// Zuordnung und JSON-Felder: <c>docs/Modell-Konvention.md</c>.
    /// </summary>
    public sealed class OwnModelCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            /// <summary>Id im Spiel, z. B. <c>UtilityFacilityType.Id</c> („power_coal“) bzw. Dateiname bei <see cref="Extras"/>.</summary>
            public string Id;
            public GameObject Model;
            // --- Eigene Assets: Beschreibung (aus Name.json oder den Ordnerregeln)
            /// <summary>Unterordner unter Models (z. B. „Agriculture“).</summary>
            public string Folder;
            /// <summary>Rolle (<see cref="OwnModelRoles"/>).</summary>
            public string Role;
            /// <summary>Verwendung innerhalb der Rolle, z. B. „field“, „bridge_pillar“ (Landwirtschaft/Infrastruktur).</summary>
            public string Use;
            /// <summary>Requisiten: „sidewalk“, „park“ oder „both“.</summary>
            public string Placement;
            /// <summary>Bäume: Klimazone („temperate“, …) – noch ohne Wirkung.</summary>
            public string Biome;
            /// <summary>Freie Schlagwörter (z. B. Feldfrucht „wheat“, Jahreszeit „summer“).</summary>
            public string[] Tags = new string[0];
            /// <summary>Gemessene Größe (m) des Modells.</summary>
            public Vector3 Size;

            public bool HasTag(string tag)
            {
                if (Tags == null) return false;
                foreach (var t in Tags) if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            // --- Ende Eigene Assets
        }

        public Entry[] Facilities = new Entry[0];
        /// <summary>G4 Dienste: Id = <c>ServiceType.Id</c> („police_station“ …), FBX unter Models/Services, Leisure, Landmarks.</summary>
        public Entry[] Services = new Entry[0];
        // --- Fußgänger: Personen-Modelle aus Models/People/*.fbx (Id = Dateiname; Gruppe nach Präfix Child/Kid/Toddler/Teen/Senior,
        // sonst Erwachsene). Ursprung = Mitte zwischen den Füßen, Front +Z, 1 Einheit = 1 m, optionale Kindobjekte LegL/LegR/ArmL/ArmR.
        public Entry[] People = new Entry[0];
        // --- Ende Fußgänger
        /// <summary>ÖPNV: Id „bus_depot“ (Models/Transport/BusDepot.fbx), „bus_stop“ (Models/Props/BusStop.fbx).</summary>
        public Entry[] Transport = new Entry[0];
        /// <summary>
        /// Eigene Assets: alle übrigen verwendeten Modelle (Id = Dateiname): Landwirtschaft, Infrastruktur, Requisiten,
        /// Büsche, Felsen – je mit <see cref="Entry.Role"/>/<see cref="Entry.Use"/>/<see cref="Entry.Placement"/>.
        /// </summary>
        public Entry[] Extras = new Entry[0];
        /// <summary>Gemeinsames Material mit der Paletten-Textur (URP Lit).</summary>
        public Material Palette;
        /// <summary>Stand der Quelldateien beim letzten Bau (Editor baut beim Start neu, wenn er abweicht).</summary>
        [HideInInspector] public string SourceStamp;

        static OwnModelCatalog instance;
        static bool loaded;

        public static OwnModelCatalog Instance
        {
            get
            {
                if (!loaded) { instance = Resources.Load<OwnModelCatalog>("OwnModels"); loaded = true; }
                return instance;
            }
        }

        public GameObject Facility(string id)
        {
            foreach (var e in Facilities) if (e != null && e.Id == id && e.Model != null) return e.Model;
            return null;
        }

        /// <summary>ÖPNV-Modell (Depot, Haltestelle) oder null → Platzhalter.</summary>
        public GameObject TransportModel(string id)
        {
            if (Transport == null) return null;
            foreach (var e in Transport) if (e != null && e.Id == id && e.Model != null) return e.Model;
            return null;
        }

        /// <summary>Modell eines Dienstgebäudes (G4) oder null → Platzhalter.</summary>
        public GameObject Service(string id)
        {
            if (Services == null) return null;
            foreach (var e in Services) if (e != null && e.Id == id && e.Model != null) return e.Model;
            return null;
        }

        // --- Eigene Assets: weitere Modelle nach Name/Rolle
        Dictionary<string, List<Entry>> byRole;

        /// <summary>Weiteres Modell nach Dateiname (z. B. „BridgePillar“) oder null.</summary>
        public Entry Extra(string id)
        {
            if (Extras == null) return null;
            foreach (var e in Extras) if (e != null && e.Id == id && e.Model != null) return e;
            return null;
        }

        /// <summary>Alle weiteren Modelle einer Rolle (<see cref="OwnModelRoles"/>), nach Id sortiert (leer, wenn keine).</summary>
        public IReadOnlyList<Entry> WithRole(string role)
        {
            if (byRole == null)
            {
                byRole = new Dictionary<string, List<Entry>>();
                if (Extras != null)
                    foreach (var e in Extras)
                    {
                        if (e == null || e.Model == null || string.IsNullOrEmpty(e.Role)) continue;
                        if (!byRole.TryGetValue(e.Role, out var list)) byRole[e.Role] = list = new List<Entry>();
                        list.Add(e);
                    }
                foreach (var l in byRole.Values) l.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            }
            return byRole.TryGetValue(role, out var r) ? r : (IReadOnlyList<Entry>)Array.Empty<Entry>();
        }

        /// <summary>Weiteres Modell mit Rolle und Verwendung (erstes nach Id), optional mit Schlagwort.</summary>
        public Entry Find(string role, string use, string tag = null)
        {
            foreach (var e in WithRole(role))
                if (string.Equals(e.Use, use, StringComparison.OrdinalIgnoreCase) && (tag == null || e.HasTag(tag))) return e;
            return null;
        }

        void OnEnable() => byRole = null; // nach Neubau im Editor
        // --- Ende Eigene Assets
    }

    /// <summary>
    /// Rollen eigener Modelle (Feld <c>role</c> der Beschreibungsdatei <c>Name.json</c>, sonst aus den Ordnerregeln).
    /// Neue Rollen hier ergänzen und im <c>OwnModelCatalogBuilder</c> zuordnen.
    /// </summary>
    public static class OwnModelRoles
    {
        public const string Building = "building", Tree = "tree", StreetLamp = "streetlamp", Prop = "prop", Bush = "bush", Rock = "rock",
            Service = "service", Facility = "facility", Transport = "transport", Person = "person", Agriculture = "agriculture",
            Infrastructure = "infrastructure", Waiting = "waiting";
    }
}
