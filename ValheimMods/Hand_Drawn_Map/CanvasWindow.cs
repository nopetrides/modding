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
        private readonly RectTransform panel;
        private readonly RectTransform viewport;
        private readonly Text status;
        private readonly Text pencilLabel;
        private readonly Text eraserLabel;
        private readonly Text annotateLabel;
        private readonly Text scaleLabel;
        private readonly RectTransform scaleBar;
        private readonly RectTransform scaleEnd;
        private readonly BrushGraphic brush;
        private readonly Image locationMarker;
        private Image nativeMarkerImage;
        private readonly InkHistory history = new InkHistory();
        private readonly List<Annotation> annotations = new List<Annotation>();
        private readonly Vector3[] nativeCorners = new Vector3[4];
        private readonly bool readOnly;
        private readonly List<ChartGraphic> batches = new List<ChartGraphic>();
        internal readonly List<InkSegment> Segments = new List<InkSegment>();
        private bool invalid = true;
        private enum Tool { Pencil, Eraser, Annotate }
        private Tool tool;
        private Annotation activeAnnotation;
        private float nextFadeRefresh;
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
            panel = Rect("MapPanel", root.transform);
            panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one;
            AddImage(panel, new Color(0.12f, 0.10f, 0.08f));

            var header = Rect("Toolbar", panel);
            header.anchorMin = new Vector2(0, 1); header.anchorMax = Vector2.one;
            header.pivot = new Vector2(0.5f, 1); header.sizeDelta = new Vector2(0, 96);
            Text title = Label(header, "HAND DRAWN MAP", new Vector2(20, -26), new Vector2(270, 44), 23);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0, 1);
            pencilLabel = Button(header, "Pencil", 300, () => SelectTool(Tool.Pencil));
            eraserLabel = Button(header, "Eraser", 430, () => SelectTool(Tool.Eraser));
            annotateLabel = Button(header, "Annotate", 560, () => SelectTool(Tool.Annotate));
            Button(header, "Close", 690, plugin.CloseMap);
            Text clearLabel = Label(header, "Clear", new Vector2(850, -27), new Vector2(65, 38), 20);
            clearLabel.rectTransform.anchorMin = clearLabel.rectTransform.anchorMax = new Vector2(0, 1);
            Button(header, "Guides", 920, ClearGuides);
            Button(header, "Annotations", 1050, ClearAnnotations, 150);
            scaleLabel = Label(header, "", new Vector2(300, -59), new Vector2(200, 24), 17);
            scaleLabel.rectTransform.anchorMin = scaleLabel.rectTransform.anchorMax = new Vector2(0, 1);
            scaleBar = ScalePart(header, new Vector2(300, -82), new Vector2(180, 2));
            ScalePart(header, new Vector2(300, -85), new Vector2(2, 8));
            scaleEnd = ScalePart(header, new Vector2(478, -85), new Vector2(2, 8));

            viewport = Rect("Parchment", panel);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(12, 44); viewport.offsetMax = new Vector2(-12, -100);
            AddImage(viewport, new Color(0.86f, 0.81f, 0.68f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var marker = Rect("PlayerLocation", viewport);
            locationMarker = marker.gameObject.AddComponent<Image>();
            locationMarker.raycastTarget = false;
            locationMarker.preserveAspect = true;
            marker.gameObject.SetActive(false);
            var cursor = Rect("BrushFootprint", viewport);
            brush = cursor.gameObject.AddComponent<BrushGraphic>();
            brush.raycastTarget = false;
            brush.gameObject.SetActive(false);
            var footer = Rect("Status", panel);
            footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0);
            footer.pivot = new Vector2(0.5f, 0); footer.sizeDelta = new Vector2(-24, 40);
            status = Label(footer, "", Vector2.zero, Vector2.zero, 18);
            status.rectTransform.anchorMin = Vector2.zero;
            status.rectTransform.anchorMax = Vector2.one;
            status.rectTransform.offsetMin = new Vector2(8, 0);
            status.rectTransform.offsetMax = Vector2.zero;
            UpdateTools();
            root.SetActive(false);
            Canvas.willRenderCanvases += UpdateLocationMarker;
        }

        internal void SetOpen(bool open)
        {
            FinishGesture();
            root.SetActive(open);
            if (open)
            {
                RaiseCanvas();
                Canvas.ForceUpdateCanvases();
                MatchNativeBounds();
                Canvas.ForceUpdateCanvases();
                invalid = true;
            }
            else
            {
                brush.gameObject.SetActive(false);
                locationMarker.gameObject.SetActive(false);
            }
        }

        internal void Tick()
        {
            if (ZInput.GetKeyDown(KeyCode.Escape)) { plugin.CloseMap(); return; }
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
            MatchNativeBounds();
            if (annotations.RemoveAll(annotation => annotation != activeAnnotation && annotation.Expires <= Time.unscaledTime) > 0) invalid = true;
            if (!readOnly && Input.GetKeyDown(KeyCode.Z) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                FinishGesture();
                if (history.Undo(Document, out bool conflict)) { notice = null; Changed(); }
                else notice = conflict ? "Undo skipped: affected ink has changed." : "Nothing to undo.";
            }
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
                if (tool == Tool.Eraser)
                {
                    history.Begin(Document);
                    erasing = true;
                    lastBrush = world;
                    eraseRadius = plugin.EraserPixels.Value * WorldPerPixel;
                    EraseTo(world);
                }
                else
                {
                    if (tool == Tool.Pencil) history.Begin(Document);
                    StartStroke(world);
                }
            }
            if ((annotations.Count > 0 || plugin.Guides.Count > 0) && Time.unscaledTime >= nextFadeRefresh)
            { invalid = true; nextFadeRefresh = Time.unscaledTime + 0.05f; }
            if (activeStroke != null || erasing)
            {
                if (!ZInput.GetMouseButton(0) || !inside) FinishGesture();
                else if (erasing) EraseTo(world);
                else AppendPoint(world);
            }
            if (viewport.rect.size != lastSize || !Mathf.Approximately(lastProjection, WorldPerPixel)) invalid = true;
            if (invalid) Rebuild();
            UpdateScale();
            UpdateBrush(local, inside);
            status.text = readOnly ? "Chart could not be loaded. Saving and editing are disabled; check the log and restore the .bak file."
                : notice ?? $"LMB: {tool}   |   Middle drag: pan   |   Wheel: zoom   |   Ctrl+Z: undo ({history.Count})   |   {plugin.MapKey.Value} / Esc: close   |   {plugin.SurveyKey.Value}: survey in world view";
        }

        private void StartStroke(ChartPoint world)
        {
            if (tool == Tool.Annotate)
            {
                int count = 0;
                foreach (Annotation annotation in annotations) count += annotation.Stroke.Points.Count;
                if (annotations.Count >= 256 || count >= 20000)
                { notice = "Temporary drawing limit reached."; return; }
                activeStroke = new ChartStroke(plugin.PencilPixels.Value * WorldPerPixel);
                notice = null;
                activeStroke.Points.Add(world);
                activeAnnotation = new Annotation(activeStroke, float.PositiveInfinity, Time.unscaledTime);
                annotations.Add(activeAnnotation);
                invalid = true;
                return;
            }
            if (Document.PointCount >= ChartDocument.MaxPoints || Document.Strokes.Count >= ChartDocument.MaxStrokes)
            { notice = "Drawing limit reached. Erase some ink to continue."; return; }
            activeStroke = new ChartStroke(plugin.PencilPixels.Value * WorldPerPixel);
            notice = null;
            activeStroke.Points.Add(world);
            Document.Strokes.Add(activeStroke);
            Document.PointCount++;
            Changed();
        }

        private void AppendPoint(ChartPoint world)
        {
            if ((world - activeStroke.Points[activeStroke.Points.Count - 1]).LengthSquared < WorldPerPixel * WorldPerPixel) return;
            if (activeAnnotation != null)
            {
                int count = 0;
                foreach (Annotation annotation in annotations) count += annotation.Stroke.Points.Count;
                if (count >= 20000) { FinishGesture(); notice = "Temporary drawing limit reached."; return; }
                activeStroke.Points.Add(world);
                invalid = true;
                return;
            }
            if (Document.PointCount >= ChartDocument.MaxPoints) { FinishGesture(); notice = "Drawing limit reached."; return; }
            if (activeStroke.Points.Count >= ChartDocument.MaxStrokePoints)
            {
                ChartPoint previous = activeStroke.Points[activeStroke.Points.Count - 1];
                activeStroke = null;
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
            var temporary = new List<Annotation>();
            bool changed = false;
            foreach (Annotation annotation in annotations)
            {
                var pieces = ChartGeometry.Erase(annotation.Stroke, lastBrush, world, eraseRadius, out bool cut);
                changed |= cut;
                foreach (ChartStroke piece in pieces)
                    temporary.Add(new Annotation(piece, annotation.Expires, annotation.Created));
            }
            if (changed && temporary.Count <= 20000)
            { annotations.Clear(); annotations.AddRange(temporary); invalid = true; }
            lastBrush = world;
        }

        private void FinishGesture()
        {
            if (activeAnnotation != null)
            {
                activeAnnotation.Created = Time.unscaledTime;
                activeAnnotation.Expires = Time.unscaledTime + plugin.AnnotationLifetime.Value;
            }
            activeAnnotation = null;
            activeStroke = null;
            erasing = false;
            panning = false;
            history.Commit(Document);
        }
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
                RenderStroke(stroke, ink);
            foreach (Annotation annotation in annotations)
            {
                float fade = annotation == activeAnnotation ? 1f : Mathf.Clamp01((annotation.Expires - Time.unscaledTime) /
                    Mathf.Min(30f, annotation.Expires - annotation.Created));
                RenderStroke(annotation.Stroke, new Color(0.38f, 0.22f, 0.12f, fade * 0.8f));
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
            locationMarker.transform.SetAsLastSibling();
            brush.transform.SetAsLastSibling();
            invalid = false;
        }

        private void RenderStroke(ChartStroke stroke, Color color)
        {
            if (stroke.Points.Count == 1) AddSegment(stroke.Points[0], stroke.Points[0], stroke.Width / lastProjection, color);
            for (int i = 1; i < stroke.Points.Count; i++)
                AddSegment(stroke.Points[i - 1], stroke.Points[i], stroke.Width / lastProjection, color);
        }

        private void AddSegment(ChartPoint a, ChartPoint b, float width, Color color)
        {
            a = ToScreen(a); b = ToScreen(b);
            if (!ChartGeometry.ClipLine(ref a, ref b, lastSize.x * 0.5f + width, lastSize.y * 0.5f + width)) return;
            Segments.Add(new InkSegment(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z), Mathf.Max(0.5f, width), color));
        }

        private void UpdateTools()
        {
            Color selected = new Color(1f, 0.75f, 0.3f);
            pencilLabel.color = tool == Tool.Pencil ? selected : Color.white;
            eraserLabel.color = tool == Tool.Eraser ? selected : Color.white;
            annotateLabel.color = tool == Tool.Annotate ? selected : Color.white;
        }

        private void SelectTool(Tool selected)
        { FinishGesture(); tool = selected; notice = null; UpdateTools(); }

        private void ClearGuides()
        {
            plugin.Guides.Clear();
            notice = null;
            invalid = true;
        }

        private void ClearAnnotations()
        {
            if (activeAnnotation != null)
            { activeAnnotation = null; activeStroke = null; }
            annotations.Clear();
            notice = null;
            invalid = true;
        }

        private void UpdateScale()
        {
            float meters = MetricScale.Length(WorldPerPixel, 180f);
            float width = meters / WorldPerPixel;
            scaleBar.sizeDelta = new Vector2(width, 2);
            scaleEnd.anchoredPosition = new Vector2(300 + width - 2, -85);
            scaleLabel.text = MetricScale.Label(meters);
        }

        private void UpdateBrush(Vector2 local, bool inside)
        {
            brush.gameObject.SetActive(inside && !panning && !readOnly);
            if (!inside) return;
            float radius = tool == Tool.Eraser
                ? (erasing ? eraseRadius / WorldPerPixel : plugin.EraserPixels.Value)
                : (activeStroke != null ? activeStroke.Width / WorldPerPixel : plugin.PencilPixels.Value) * 0.5f;
            brush.rectTransform.anchoredPosition = local;
            brush.rectTransform.sizeDelta = Vector2.one * Mathf.Max(1f, radius * 2f);
            Color tint = tool == Tool.Eraser ? new Color(0.65f, 0.15f, 0.12f) : new Color(0.12f, 0.22f, 0.3f);
            tint.a = plugin.BrushOpacity.Value;
            brush.color = tint;
        }

        private void UpdateLocationMarker()
        {
            if (!IsOpen) return;
            Player player = Player.m_localPlayer;
            if (!plugin.ShowMyLocation.Value || player == null || Minimap.instance == null)
            { locationMarker.gameObject.SetActive(false); return; }
            if (nativeMarkerImage == null)
                nativeMarkerImage = Minimap.instance.m_largeMarker.GetComponentInChildren<Image>(true);
            if (nativeMarkerImage == null)
            { locationMarker.gameObject.SetActive(false); return; }
            Vector3 position = player.transform.position;
            ChartPoint point = ToScreen(new ChartPoint(position.x, position.z));
            Vector2 local = new Vector2(point.X, point.Z);
            if (!viewport.rect.Contains(local))
            { locationMarker.gameObject.SetActive(false); return; }
            locationMarker.sprite = nativeMarkerImage.overrideSprite;
            locationMarker.color = nativeMarkerImage.color;
            RectTransform native = nativeMarkerImage.rectTransform;
            locationMarker.rectTransform.sizeDelta = new Vector2(
                native.rect.width * Mathf.Abs(native.lossyScale.x),
                native.rect.height * Mathf.Abs(native.lossyScale.y)) / canvas.scaleFactor;
            locationMarker.rectTransform.anchoredPosition = local;
            Camera camera = Utils.GetMainCamera();
            float yaw = camera != null ? camera.transform.eulerAngles.y : player.transform.eulerAngles.y;
            locationMarker.rectTransform.localRotation = Quaternion.Euler(0, 0, -yaw);
            locationMarker.gameObject.SetActive(true);
        }

        private void RaiseCanvas()
        {
            int layer = canvas.sortingLayerID;
            int order = 100;
            int layerValue = SortingLayer.GetLayerValueFromID(layer);
            foreach (Canvas other in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (other == canvas || !other.isActiveAndEnabled || !other.gameObject.scene.IsValid()) continue;
                int value = SortingLayer.GetLayerValueFromID(other.sortingLayerID);
                if (value > layerValue)
                { layer = other.sortingLayerID; layerValue = value; order = other.sortingOrder; }
                else if (value == layerValue) order = Mathf.Max(order, other.sortingOrder);
            }
            canvas.sortingLayerID = layer;
            canvas.overrideSorting = true;
            canvas.sortingOrder = Mathf.Min(32767, order + 1);
        }

        private void MatchNativeBounds()
        {
            RectTransform native = Minimap.instance.m_mapImageLarge.rectTransform;
            native.GetWorldCorners(nativeCorners);
            Canvas nativeCanvas = native.GetComponentInParent<Canvas>();
            Camera camera = nativeCanvas != null && nativeCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? nativeCanvas.worldCamera : null;
            Vector2 bottom = RectTransformUtility.WorldToScreenPoint(camera, nativeCorners[0]);
            Vector2 top = RectTransformUtility.WorldToScreenPoint(camera, nativeCorners[2]);
            RectTransform canvasRect = (RectTransform)root.transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, bottom, null, out Vector2 localBottom);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, top, null, out Vector2 localTop);
            Vector2 minimum = localBottom - canvasRect.rect.min;
            Vector2 maximum = localTop - canvasRect.rect.max;
            if (top.x - bottom.x < Screen.width * 0.25f || top.y - bottom.y < Screen.height * 0.25f)
            { minimum = new Vector2(100, 100); maximum = new Vector2(-100, -100); }
            if (panel.offsetMin != minimum || panel.offsetMax != maximum)
            { panel.offsetMin = minimum; panel.offsetMax = maximum; invalid = true; }
        }

        private static RectTransform ScalePart(RectTransform parent, Vector2 position, Vector2 size)
        {
            RectTransform part = Rect("Scale", parent);
            part.anchorMin = part.anchorMax = new Vector2(0, 1);
            part.pivot = Vector2.zero;
            part.anchoredPosition = position; part.sizeDelta = size;
            AddImage(part, new Color(0.9f, 0.86f, 0.75f));
            part.GetComponent<Image>().raycastTarget = false;
            return part;
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

        private Text Button(RectTransform parent, string caption, float x, Action action, float width = 120)
        {
            RectTransform rect = Rect(caption, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -8); rect.sizeDelta = new Vector2(width, 38);
            AddImage(rect, new Color(0.25f, 0.21f, 0.16f));
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(() => action());
            Text label = Label(rect, caption, new Vector2(12, 0), new Vector2(width - 24, 38), 20);
            return label;
        }

        public void Dispose()
        {
            Canvas.willRenderCanvases -= UpdateLocationMarker;
            if (root != null) Object.Destroy(root);
        }

        private sealed class Annotation
        {
            public readonly ChartStroke Stroke;
            public float Expires;
            public float Created;
            public Annotation(ChartStroke stroke, float expires, float created)
            { Stroke = stroke; Expires = expires; Created = created; }
        }
    }
}
