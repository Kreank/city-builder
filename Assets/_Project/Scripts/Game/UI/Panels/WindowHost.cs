using System;
using System.Collections.Generic;
using CityBuilder.Game.Roads;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Panels
{
    /// <summary>
    /// Fensterverwaltung der Spiel-UI (gehört zur <see cref="Hud.GameHud"/>): höchstens <b>ein</b> großes Panel am rechten
    /// Rand und <b>ein</b> mittiges Fenster gleichzeitig. Je Platz gewinnt das Fenster mit dem höchsten
    /// <see cref="GameWindow.OpenScore"/> (bei Gleichstand das zuletzt geöffnete); im mittigen Platz verdrängt ein neu
    /// geöffnetes Fenster das alte (<see cref="GameWindow.Close"/>). Esc schließt zuerst das mittige Fenster, sonst gibt
    /// es den Tastendruck an das rechte Panel. Inhalte werden 5× je Sekunde und direkt nach Klicks neu beschrieben.
    /// Mausblockade: Die Fenster liegen unter der beim <see cref="UiBlocker"/> angemeldeten HUD-Wurzel.
    /// </summary>
    public sealed class WindowHost
    {
        const float RefreshInterval = 0.2f;

        sealed class Frame
        {
            public GameWindow Window;
            public VisualElement Root, Head;
            public ScrollView Scroll;
            public readonly Ui HeadUi = new Ui();
            public readonly Ui BodyUi = new Ui();
            public float Score;
            public long OpenedAt;
            public bool Visible;
            public int ShownVersion = int.MinValue;
            public float NextErrorLog;
        }

        readonly List<Frame> frames = new List<Frame>();
        readonly VisualElement root, centerLayer;
        long openCounter;
        float nextRefresh;
        bool dirty;
        Frame right, center;
        float rightBottom = 16f;

        public RoadTool Tool { get; }

        /// <summary>Gerade sichtbares rechtes Panel (null = keins).</summary>
        public GameWindow RightWindow => right?.Window;
        public GameWindow CenterWindow => center?.Window;

        /// <summary>Linke Kante des rechten Panels in Panel-Koordinaten (Referenzpixel), NaN ohne Panel.</summary>
        public float RightPanelLeft => right != null ? right.Root.worldBound.xMin : float.NaN;

        public WindowHost(RoadTool tool, VisualElement hudRoot)
        {
            Tool = tool;
            root = hudRoot;
            centerLayer = Ui.Box("cb-center-layer");
            root.Add(centerLayer);
        }

        /// <summary>Fenster anmelden. Darf jederzeit aufgerufen werden (auch von Systemen, die später dazukommen).</summary>
        public T Add<T>(T window) where T : GameWindow
        {
            var f = new Frame { Window = window };
            window.Host = this;
            f.Root = Ui.El("cb-window", window.Slot == WindowSlot.Right ? "cb-window--right" : "cb-window--center", "cb-hidden");
            if (!string.IsNullOrEmpty(window.FrameClass)) f.Root.AddToClassList(window.FrameClass);
            f.Head = Ui.Box("cb-window__head");
            f.Root.Add(f.Head);
            f.Scroll = new ScrollView(ScrollViewMode.Vertical);
            f.Scroll.AddToClassList("cb-window__scroll");
            f.Scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            f.Scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            f.Scroll.contentContainer.AddToClassList("cb-window__content");
            f.Root.Add(f.Scroll);
            f.HeadUi.Clicked = MarkDirty;
            f.BodyUi.Clicked = MarkDirty;
            if (window.Slot == WindowSlot.Center) centerLayer.Add(f.Root);
            else
            {
                f.Root.style.bottom = rightBottom;
                root.Insert(Math.Max(0, root.IndexOf(centerLayer)), f.Root);
            }
            frames.Add(f);
            dirty = true;
            return window;
        }

        /// <summary>Sofort neu zeichnen (nächstes Frame), z. B. nach einer Aktion außerhalb des Fensters.</summary>
        public void MarkDirty() => dirty = true;

        /// <summary>Abstand des rechten Panels vom unteren Rand (Referenzpixel) – die HUD rückt es über die Bauleiste.</summary>
        public void SetRightBottom(float px)
        {
            if (Mathf.Abs(px - rightBottom) < 0.5f) return;
            rightBottom = px;
            foreach (var f in frames)
                if (f.Window.Slot == WindowSlot.Right) f.Root.style.bottom = px;
        }

        public void Update()
        {
            foreach (var f in frames)
            {
                try { f.Window.Poll(); }
                catch (Exception e) { LogOnce(f, e); }
                float s = SafeScore(f);
                if (s <= 0f) f.Window.Dismissed = false;
                float effective = f.Window.Dismissed ? 0f : s;
                if (effective > 0f && f.Score <= 0f) f.OpenedAt = ++openCounter;
                f.Score = effective;
            }

            var newCenter = Pick(WindowSlot.Center);
            if (newCenter != center && newCenter != null)
                foreach (var f in frames)
                    if (f != newCenter && f.Window.Slot == WindowSlot.Center && f.Score > 0f)
                    {
                        f.Window.Close(); // mittig nur eins: das neue verdrängt das alte
                        f.Score = 0f;
                    }
            center = newCenter;
            right = Pick(WindowSlot.Right);

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (center != null) { center.Window.Close(); dirty = true; }
                else if (right != null && right.Window.HandleEscape()) dirty = true;
            }

            bool refresh = dirty || Time.unscaledTime >= nextRefresh;
            if (refresh)
            {
                dirty = false;
                nextRefresh = Time.unscaledTime + RefreshInterval;
            }
            foreach (var f in frames)
            {
                bool show = f == right || f == center;
                if (show != f.Visible)
                {
                    f.Visible = show;
                    Ui.Show(f.Root, show);
                    if (show) Draw(f);
                }
                if (!show) continue;
                try { f.Window.Tick(); }
                catch (Exception e) { LogOnce(f, e); }
                if (refresh) Draw(f);
            }
        }

        Frame Pick(WindowSlot slot)
        {
            Frame best = null;
            foreach (var f in frames)
            {
                if (f.Window.Slot != slot || f.Score <= 0f) continue;
                if (best == null || f.Score > best.Score || (Mathf.Approximately(f.Score, best.Score) && f.OpenedAt > best.OpenedAt)) best = f;
            }
            return best;
        }

        float SafeScore(Frame f)
        {
            try { return f.Window.OpenScore; }
            catch (Exception e) { LogOnce(f, e); return 0f; }
        }

        void Draw(Frame f)
        {
            try
            {
                f.HeadUi.Begin(f.Head);
                f.Window.DrawHead(f.HeadUi);
                f.HeadUi.End();
                f.BodyUi.Begin(f.Scroll.contentContainer);
                f.Window.DrawBody(f.BodyUi);
                f.BodyUi.End();
            }
            catch (Exception e)
            {
                LogOnce(f, e);
            }
            if (f.ShownVersion != f.Window.ContentVersion)
            {
                f.ShownVersion = f.Window.ContentVersion;
                f.Scroll.scrollOffset = Vector2.zero;
            }
        }

        static void LogOnce(Frame f, Exception e)
        {
            if (Time.unscaledTime < f.NextErrorLog) return;
            f.NextErrorLog = Time.unscaledTime + 5f;
            Debug.LogException(e);
        }
    }
}
