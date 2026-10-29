using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Hand_Drawn_Map
{
    internal sealed class CanvasWindow : IDisposable
    {
        private readonly MapPlugin plugin;
        private readonly GameObject root;
        private readonly Canvas canvas;
        private readonly RectTransform viewport;
        private readonly Text status;
        private readonly Text pencilLabel;
        private readonly Text eraserLabel;
        private readonly bool readOnly;
        private readonly List<ChartGraphic> batches = new List<ChartGraphic>();
        internal readonly List<InkSegment> Segments = new List<InkSegment>();
        private bool invalid = true;
        private bool erase;
        private bool panning;
        private bool erasing;
        private ChartStroke activeStroke;
        private ChartPoint lastBrush;
        private Vector2 lastPointer;
        private float eraseRadius;
        private Vector2 lastSize;
        private float lastProjection;
        private readonly Font font;
        private string notice;
        internal bool IsOpen => root != null && root.activeSelf;
        internal bool InGesture => activeStroke != null || erasing || panning;
        private ChartDocument Document => plugin.Document;
        private float Extent => Minimap.instance.m_textureSize * Minimap.instance.m_pixelSize;
        private float WorldPerPixel
        {
            get
            {
                var nativeRect = Minimap.instance.m_mapImageLarge.rectTransform;
                float height = nativeRect.rect.height * Mathf.Abs(nativeRect.lossyScale.y);
                if (height < 1f) height = Screen.height;
                return Extent * Document.Zoom * canvas.scaleFactor / height;
            }
        }

        internal CanvasWindow(MapPlugin plugin, bool readOnly)
        {
            this.plugin = plugin;
            this.readOnly = readOnly;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            root = new GameObject("HandDrawnMap", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Object.DontDestroyOnLoad(root);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            AddImage(root.GetComponent<RectTransform>(), new Color(0.12f, 0.10f, 0.08f));

            var header = Rect("Toolbar", root.transform);
            header.anchorMin = new Vector2(0, 1); header.anchorMax = Vector2.one;
            header.pivot = new Vector2(0.5f, 1); header.sizeDelta = new Vector2(0, 54);
            Label(header, "HAND DRAWN MAP", new Vector2(20, 0), new Vector2(270, 54), 23);
            pencilLabel = Button(header, "Pencil", 300, () => { FinishGesture(); erase = false; UpdateTools(); });
            eraserLabel = Button(header, "Eraser", 430, () => { FinishGesture(); erase = true; UpdateTools(); });
            Button(header, "Close", 560, plugin.CloseMap);

            viewport = Rect("Parchment", root.transform);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(16, 54); viewport.offsetMax = new Vector2(-16, -58);
            AddImage(viewport, new Color(0.86f, 0.81f, 0.68f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var footer = Rect("Status", root.transform);
            footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0);
            footer.pivot = new Vector2(0.5f, 0); footer.sizeDelta = new Vector2(-32, 48);
            status = Label(footer, "", Vector2.zero, Vector2.zero, 18);
            status.rectTransform.anchorMax = Vector2.one;
            status.rectTransform.offsetMin = new Vector2(8, 0);
            status.rectTransform.offsetMax = Vector2.zero;
            UpdateTools();
            root.SetActive(false);
        }

        internal void SetOpen(bool open)
        {
            FinishGesture();
            root.SetActive(open);
            if (open) { Canvas.ForceUpdateCanvases(); invalid = true; }
        }

        internal void Tick()
        {
            if (ZInput.GetKeyDown(KeyCode.Escape)) { plugin.CloseMap(); return; }
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
            Vector2 pointer = ZInput.pointerPosition;
            bool inside = RectTransformUtility.RectangleContainsScreenPoint(viewport, pointer, null);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, pointer, null, out Vector2 local);
            ChartPoint world = ToWorld(local);
            if (inside && !InGesture)
            {
                float wheel = ZInput.GetMouseScrollWheel();
                if (wheel != 0f) Zoom(Mathf.Clamp(wheel, -0.05f, 0.05f) * 2f, local);
            }
            if (inside && !InGesture && ZInput.GetMouseButtonDown(2)) { panning = true; lastPointer = local; }
            if (panning)
            {
                if (!ZInput.GetMouseButton(2)) panning = false;
                else
                {
                    Vector2 delta = local - lastPointer;
                    Document.Center -= new ChartPoint(delta.x, delta.y) * WorldPerPixel;
                    ClampCenter();
                    lastPointer = local;
                    Changed();
                }
            }
            if (inside && !InGesture && !readOnly && ZInput.GetMouseButtonDown(0))
            {
                if (erase)
                {
                    erasing = true;
                    lastBrush = world;
                    eraseRadius = plugin.EraserPixels.Value * WorldPerPixel;
                    EraseTo(world);
                }
                else StartStroke(world);
            }
            if (activeStroke != null || erasing)
            {
                if (!ZInput.GetMouseButton(0) || !inside) FinishGesture();
                else if (erasing) EraseTo(world);
                else AppendPoint(world);
            }
            if (viewport.rect.size != lastSize || !Mathf.Approximately(lastProjection, WorldPerPixel)) invalid = true;
            if (invalid) Rebuild();
            status.text = readOnly ? "Chart could not be loaded. Saving and editing are disabled; check the log and restore the .bak file."
                : notice ?? $"LMB: {(erase ? "erase" : "draw")}   |   Middle drag: pan   |   Wheel: zoom   |   {plugin.MapKey.Value} / Esc: close   |   {plugin.SurveyKey.Value}: survey in world view   |   Guides: {plugin.Guides.Count}";
        }

        private void StartStroke(ChartPoint world)
        {
            if (Document.PointCount >= ChartDocument.MaxPoints || Document.Strokes.Count >= ChartDocument.MaxStrokes)
            { notice = "Drawing limit reached. Erase some ink to continue."; return; }
            activeStroke = new ChartStroke(plugin.PencilPixels.Value * WorldPerPixel);
            activeStroke.Points.Add(world);
            Document.Strokes.Add(activeStroke);
            Document.PointCount++;
            Changed();
        }

        private void AppendPoint(ChartPoint world)
        {
            if ((world - activeStroke.Points[activeStroke.Points.Count - 1]).LengthSquared < WorldPerPixel * WorldPerPixel) return;
            if (Document.PointCount >= ChartDocument.MaxPoints) { FinishGesture(); notice = "Drawing limit reached."; return; }
            if (activeStroke.Points.Count >= ChartDocument.MaxStrokePoints)
            {
                ChartPoint previous = activeStroke.Points[activeStroke.Points.Count - 1];
                FinishGesture();
                StartStroke(previous);
                if (activeStroke == null) return;
                if (Document.PointCount >= ChartDocument.MaxPoints)
                { FinishGesture(); notice = "Drawing limit reached."; return; }
            }
            activeStroke.Points.Add(world);
            Document.PointCount++;
            Changed();
        }

        private void EraseTo(ChartPoint world)
        {
            if (Document.Erase(lastBrush, world, eraseRadius)) { notice = null; Changed(); }
            lastBrush = world;
        }

        private void FinishGesture() { activeStroke = null; erasing = false; panning = false; }
        internal void Invalidate() => invalid = true;
        private void Changed() { plugin.Dirty = true; invalid = true; }

        private void Zoom(float step, Vector2 local)
        {
            ChartPoint before = ToWorld(local);
            Document.Zoom = Mathf.Clamp(Document.Zoom * (1f - step), Minimap.instance.m_minZoom, Minimap.instance.m_maxZoom);
            Document.Center += before - ToWorld(local);
            ClampCenter();
            Changed();
        }

        private void ClampCenter()
        {
            float half = Extent * 0.5f;
            Document.Center = new ChartPoint(Mathf.Clamp(Document.Center.X, -half, half), Mathf.Clamp(Document.Center.Z, -half, half));
        }

        private ChartPoint ToWorld(Vector2 point) => ChartGeometry.ToWorld(new ChartPoint(point.x, point.y), Document.Center, WorldPerPixel);
        private ChartPoint ToScreen(ChartPoint point) => ChartGeometry.ToScreen(point, Document.Center, WorldPerPixel);

        private void Rebuild()
        {
            Segments.Clear();
            lastSize = viewport.rect.size;
            lastProjection = WorldPerPixel;
            Color ink = new Color(0.18f, 0.15f, 0.12f);
            foreach (ChartStroke stroke in Document.Strokes)
            {
                if (stroke.Points.Count == 1) AddSegment(stroke.Points[0], stroke.Points[0], stroke.Width / lastProjection, ink);
                for (int i = 1; i < stroke.Points.Count; i++)
                    AddSegment(stroke.Points[i - 1], stroke.Points[i], stroke.Width / lastProjection, ink);
            }
            foreach (SurveyGuide guide in plugin.Guides)
            {
                float fade = Mathf.Clamp01((guide.Expires - Time.unscaledTime) / Mathf.Min(30f, guide.Expires - guide.Created));
                ChartPoint closest = guide.Origin + guide.Direction * ChartPoint.Dot(Document.Center - guide.Origin, guide.Direction);
                float reach = lastSize.magnitude * lastProjection;
                AddSegment(closest - guide.Direction * reach, closest + guide.Direction * reach, 1.5f, new Color(0.16f, 0.35f, 0.48f, fade * 0.65f));
            }
            int count = Mathf.Max(1, (Segments.Count + ChartGraphic.BatchSize - 1) / ChartGraphic.BatchSize);
            while (batches.Count < count)
            {
                RectTransform transform = Rect("Ink", viewport);
                transform.anchorMin = Vector2.zero; transform.anchorMax = Vector2.one;
                transform.offsetMin = transform.offsetMax = Vector2.zero;
                var graphic = transform.gameObject.AddComponent<ChartGraphic>();
                graphic.raycastTarget = false;
                graphic.Window = this;
                graphic.Batch = batches.Count;
                batches.Add(graphic);
            }
            for (int i = 0; i < batches.Count; i++)
            {
                batches[i].gameObject.SetActive(i < count);
                if (i < count) batches[i].SetVerticesDirty();
            }
            invalid = false;
        }

        private void AddSegment(ChartPoint a, ChartPoint b, float width, Color color)
        {
            a = ToScreen(a); b = ToScreen(b);
            if (!ChartGeometry.ClipLine(ref a, ref b, lastSize.x * 0.5f + width, lastSize.y * 0.5f + width)) return;
            Segments.Add(new InkSegment(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z), Mathf.Max(0.5f, width), color));
        }

        private void UpdateTools()
        {
            pencilLabel.color = erase ? Color.white : new Color(1f, 0.75f, 0.3f);
            eraserLabel.color = erase ? new Color(1f, 0.75f, 0.3f) : Color.white;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void AddImage(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
        }

        private Text Label(RectTransform parent, string text, Vector2 position, Vector2 size, int fontSize)
        {
            RectTransform rect = Rect("Label", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 0.5f);
            rect.pivot = new Vector2(0, 0.5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            Text label = rect.gameObject.AddComponent<Text>();
            label.font = font; label.fontSize = fontSize;
            label.color = Color.white; label.text = text;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            return label;
        }

        private Text Button(RectTransform parent, string caption, float x, Action action)
        {
            RectTransform rect = Rect(caption, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 0.5f);
            rect.pivot = new Vector2(0, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0); rect.sizeDelta = new Vector2(120, 38);
            AddImage(rect, new Color(0.25f, 0.21f, 0.16f));
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(() => action());
            Text label = Label(rect, caption, new Vector2(12, 0), new Vector2(100, 38), 20);
            return label;
        }

        public void Dispose() { if (root != null) Object.Destroy(root); }
    }
}
