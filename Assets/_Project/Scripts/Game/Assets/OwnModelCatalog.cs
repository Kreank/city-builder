using System;
using UnityEngine;

namespace CityBuilder.Game.Assets
{
    /// <summary>
    /// Eigene Modelle (Blender, <c>Assets/_Project/Models</c>, im Git) – getrennt vom Store-Katalog in ThirdParty.
    /// Wird vom Editor automatisch neu gebaut, sobald sich dort ein Modell ändert
    /// (<c>CityBuilder.Editor.OwnModelCatalogBuilder</c>), liegt in <c>Assets/_Project/Resources/OwnModels.asset</c>.
    /// Konvention der Modelle: Ursprung = Mitte der Grundfläche auf Bodenhöhe, Vorderseite +Z, 1 Einheit = 1 m,
    /// ein gemeinsames Paletten-Material. Bewegliche Teile sind eigene benannte Kindobjekte (z. B. „Rotor“).
    /// </summary>
    public sealed class OwnModelCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            /// <summary>Id im Spiel, z. B. <c>UtilityFacilityType.Id</c> („power_coal“).</summary>
            public string Id;
            public GameObject Model;
        }

        public Entry[] Facilities = new Entry[0];
        /// <summary>Gemeinsames Material mit der Paletten-Textur (URP Lit).</summary>
        public Material Palette;

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
    }
}
