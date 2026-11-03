using System;
using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    [Flags]
    public enum RoadSegmentFlags
    {
        None = 0,
        /// <summary>Teil eines Kreisverkehrs (Ring hat Vorfahrt).</summary>
        Roundabout = 1,
        /// <summary>
        /// Gehört der Welt (vorgebaute Fernstraße/Anschlussstelle, s. <c>Gateways</c>): nicht abreißbar, nicht umbaubar,
        /// Kosten und Unterhalt trägt die Welt. Bleibt beim Zerteilen erhalten (Anschluss neuer Rampen ist erlaubt).
        /// </summary>
        Locked = 2,
    }

    /// <summary>
    /// Ein (gerades oder gekrümmtes) Straßenstück zwischen zwei Knoten.
    /// Die Richtung Start → Ende ist bedeutsam: Spuren mit <see cref="LaneDirection.Forward"/>
    /// fahren in diese Richtung (wichtig für Einbahnstraßen, Autobahnen, Rampen).
    /// Die Höhe verläuft linear zwischen den Knotenhöhen.
    /// </summary>
    public sealed class RoadSegment
    {
        public int Id { get; }
        public RoadNode Start { get; }
        public RoadNode End { get; }
        /// <summary>Bézier-Kontrollpunkt. Bei geraden Straßen die Mitte zwischen Start und Ende.</summary>
        public Vector2 Control { get; }
        public RoadType Type { get; }
        public RoadSegmentFlags Flags { get; }
        public float Length { get; }

        readonly IGround ground;
        /// <summary>Höhenprofil (gleichmäßig über t verteilt) für geländefolgende Straßen; null = linear zwischen den Knoten.</summary>
        readonly float[] profile;

        /// <summary>Folgt dem Gelände (Höhenprofil statt gerader Rampe).</summary>
        public bool FollowsTerrain => profile != null;

        /// <summary>Kopie des Höhenprofils (oder null).</summary>
        public float[] Profile => profile == null ? null : (float[])profile.Clone();

        public bool IsRoundabout => (Flags & RoadSegmentFlags.Roundabout) != 0;
        /// <summary>Gehört der Welt (Gateways): der Spieler darf es nicht abreißen, umbauen oder umdrehen.</summary>
        public bool IsLocked => (Flags & RoadSegmentFlags.Locked) != 0;
        public float Slope => Length > 0f ? MathF.Abs(End.Height - Start.Height) / Length : 0f;

        /// <summary>Höhe der Fahrbahn über dem Gelände an Stelle t (negativ = unter der Erde).</summary>
        public float ElevationAt(float t) => HeightAt(t) - ground.HeightAt(Evaluate(t));

        public float MaxElevation { get { float m = float.MinValue; for (int i = 0; i <= 8; i++) m = MathF.Max(m, ElevationAt(i / 8f)); return m; } }
        public float MinElevation { get { float m = float.MaxValue; for (int i = 0; i <= 8; i++) m = MathF.Min(m, ElevationAt(i / 8f)); return m; } }

        /// <summary>Liegt (teilweise) deutlich über dem Gelände – Brücke.</summary>
        public bool IsElevated => MaxElevation > 0.6f;
        /// <summary>Liegt (teilweise) unter dem Gelände – Trog oder Tunnel.</summary>
        public bool IsUnderground => MinElevation < -0.3f;
        /// <summary>Ist an Stelle t überdeckt (Tunnel)?</summary>
        public bool IsCoveredAt(float t) => ElevationAt(t) <= -RoadBuilder.TunnelDepth + 0.01f;

        internal RoadSegment(int id, RoadNode start, RoadNode end, Vector2 control, RoadType type, RoadSegmentFlags flags, IGround ground,
            float[] profile = null)
        {
            this.ground = ground ?? FlatGround.Instance;
            if (profile != null && profile.Length >= 2)
            {
                this.profile = (float[])profile.Clone();
                // Enden exakt auf die Knotenhöhen legen
                this.profile[0] = start.Height;
                this.profile[this.profile.Length - 1] = end.Height;
            }
            Id = id;
            Start = start;
            End = end;
            Control = control;
            Type = type;
            Flags = flags;
            Length = Bezier.ApproximateLength(start.Position, control, end.Position);
        }

        public Vector2 Evaluate(float t) => Bezier.Evaluate(Start.Position, Control, End.Position, t);

        public float HeightAt(float t) => profile == null ? Start.Height + (End.Height - Start.Height) * t : Profiles.Sample(profile, t);

        /// <summary>Steilster Abschnitt (Anteil).</summary>
        public float MaxSlope => profile == null ? Slope : Profiles.MaxSlope(profile, Length);

        public Vector2 Tangent(float t)
        {
            Vector2 d = Bezier.Derivative(Start.Position, Control, End.Position, t);
            float len = d.Length();
            return len > 1e-6f ? d / len : Vector2.Normalize(End.Position - Start.Position);
        }

        /// <summary>Rechte Seite in Segmentrichtung (Spur-Offsets sind positiv nach rechts).</summary>
        public Vector2 Right(float t)
        {
            var d = Tangent(t);
            return new Vector2(d.Y, -d.X);
        }

        public float ClosestT(Vector2 point) => Bezier.ClosestT(Start.Position, Control, End.Position, point);

        public float DistanceTo(Vector2 point) => Vector2.Distance(Evaluate(ClosestT(point)), point);

        public RoadNode Other(RoadNode node) => node == Start ? End : Start;

        public bool Touches(RoadNode node) => node == Start || node == End;

        public override string ToString() => $"Segment {Id} ({Type.DisplayName}) {Start.Id}→{End.Id}";
    }
}
