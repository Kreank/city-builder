using System;
using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>Geometrie-Helfer fürs Straßenbauen: Winkel, Ausrichtung, Einrasten.</summary>
    public static class RoadGeometry
    {
        const float Rad2Deg = 180f / MathF.PI;
        const float Deg2Rad = MathF.PI / 180f;

        /// <summary>Richtung, in der das Segment den Knoten verlässt (normalisiert).</summary>
        public static Vector2 OutgoingDirection(RoadSegment segment, RoadNode node) =>
            node == segment.Start ? segment.Tangent(0f) : -segment.Tangent(1f);

        /// <summary>Vorzeichenbehafteter Winkel von a nach b in Grad (-180..180, gegen den Uhrzeigersinn positiv).</summary>
        public static float SignedAngle(Vector2 a, Vector2 b)
        {
            float cross = a.X * b.Y - a.Y * b.X;
            float dot = Vector2.Dot(a, b);
            return MathF.Atan2(cross, dot) * Rad2Deg;
        }

        /// <summary>Winkel zwischen zwei Richtungen in Grad (0..180).</summary>
        public static float Angle(Vector2 a, Vector2 b) => MathF.Abs(SignedAngle(a, b));

        public static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Deg2Rad;
            float c = MathF.Cos(r), s = MathF.Sin(r);
            return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }

        /// <summary>Kompassrichtung in Grad (0 = Norden/+Y, 90 = Osten/+X, im Uhrzeigersinn).</summary>
        public static float Bearing(Vector2 direction)
        {
            float deg = MathF.Atan2(direction.X, direction.Y) * Rad2Deg;
            return deg < 0f ? deg + 360f : deg;
        }

        /// <summary>
        /// Sucht unter allen Straßen am Knoten diejenige, deren Abgangsrichtung den kleinsten
        /// Winkel zur gewünschten Richtung hat. Das ist der Winkel, den der Spieler sehen will.
        /// </summary>
        public static bool TryGetReferenceDirection(RoadNetwork network, RoadNode node, Vector2 desired,
            out Vector2 reference, out float angle)
        {
            reference = default;
            angle = 0f;
            if (node == null || node.SegmentIds.Count == 0) return false;

            float best = float.MaxValue;
            foreach (int id in node.SegmentIds)
            {
                var dir = OutgoingDirection(network.GetSegment(id), node);
                float a = Angle(dir, desired);
                if (a < best)
                {
                    best = a;
                    reference = dir;
                }
            }
            angle = best;
            return true;
        }

        /// <summary>
        /// Rastet den Zielpunkt ein: Winkel relativ zu <paramref name="reference"/> in Schritten von
        /// <paramref name="angleStep"/> Grad, Länge in Schritten von <paramref name="lengthStep"/> Metern.
        /// Ein Schritt von 0 schaltet das jeweilige Einrasten ab.
        /// </summary>
        public static Vector2 Snap(Vector2 origin, Vector2 target, Vector2 reference, float angleStep, float lengthStep)
        {
            Vector2 delta = target - origin;
            float length = delta.Length();
            if (length < 1e-4f || reference.LengthSquared() < 1e-8f) return target;

            Vector2 dir = delta / length;
            if (angleStep > 0f)
            {
                float a = SignedAngle(reference, dir);
                float snapped = MathF.Round(a / angleStep) * angleStep;
                dir = Rotate(Vector2.Normalize(reference), snapped);
            }
            if (lengthStep > 0f)
                length = MathF.Max(lengthStep, MathF.Round(length / lengthStep) * lengthStep);

            return origin + dir * length;
        }

        /// <summary>
        /// Ergänzung zu <see cref="Snap"/>: Parallel-Einrasten. Weicht die Richtung origin → target höchstens
        /// <paramref name="maxAngle"/> Grad von ±<paramref name="direction"/> ab, wird target exakt auf die Gerade durch
        /// origin in dieser Richtung gelegt (Länge optional in Schritten von <paramref name="lengthStep"/>).
        /// </summary>
        public static bool SnapParallel(Vector2 origin, Vector2 target, Vector2 direction, float maxAngle, float lengthStep,
            out Vector2 snapped)
        {
            snapped = target;
            Vector2 delta = target - origin;
            if (delta.LengthSquared() < 1e-6f || direction.LengthSquared() < 1e-8f) return false;
            var dir = Vector2.Normalize(direction);
            float along = Vector2.Dot(delta, dir);
            if (along < 0f) { dir = -dir; along = -along; }
            if (Angle(dir, delta) > maxAngle) return false;
            if (lengthStep > 0f) along = MathF.Max(lengthStep, MathF.Round(along / lengthStep) * lengthStep);
            snapped = origin + dir * along;
            return true;
        }
    }
}
