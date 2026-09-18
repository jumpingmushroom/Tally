using System.Collections.Generic;
using System.Text;
using Jotunn.Managers;
using Recount.Core;
using Recount.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Recount.UI
{
    /// <summary>
    /// The meter window: title bar with the mode and segment, buttons, and a list of bars.
    /// Built from flat images and Norse text under Jotunn's CustomGUIFront, so it rides along
    /// with the game's UI scaling and is destroyed and rebuilt with it.
    /// </summary>
    public sealed class MeterWindow
    {
        private const float TitleHeight = 22f;
        private const float Padding = 4f;
        private const float RowGap = 2f;
        private const float ButtonFont = 12f;
        private const float PositionSettleDelay = 0.4f;
        private const float DpsRefresh = 0.5f;

        private sealed class Row
        {
            public GameObject Go;
            public RectTransform Rect;
            public Image Bar;
            public TextMeshProUGUI Left;
            public TextMeshProUGUI Right;
            public RowHandler Handler;
            public RowData Data;
        }

        private readonly Meter _meter;
        private readonly Tooltip _tooltip = new Tooltip();
        private readonly List<Row> _rows = new List<Row>();

        private GameObject _root;
        private RectTransform _rect;
        private Image _background;
        private RectTransform _body;
        private TextMeshProUGUI _title;
        private Button _back;
        private GameObject _resizeGrip;
        private float _titleReserved = 150f;

        private Vector2 _lastSeenPosition;
        private Vector2 _lastSeenSize;
        private float _positionSettleAt;
        private bool _suppressLayoutApply;

        private int _builtRevision = -1;
        private bool _dirty = true;
        private float _nextTimedRebuild;
        private Row _hovered;

        public MeterMode Mode { get; private set; }
        public int SegmentIndex { get; private set; }
        public string Drill { get; private set; }
        public int Scroll { get; private set; }

        public bool IsOpen { get; private set; }
        public bool Created => _root != null;

        public MeterWindow(Meter meter)
        {
            _meter = meter;
        }

        // ---- construction ------------------------------------------------------------------

        public void Create()
        {
            if (_root != null || GUIManager.CustomGUIFront == null)
                return;

            Transform parent = GUIManager.CustomGUIFront.transform;

            _root = Widgets.Panel(parent, "RecountWindow", Widgets.Background, true);
            _rect = Widgets.Rect(_root);
            Widgets.TopLeft(_rect, 0f, 0f, PluginConfig.WindowWidth.Value, PluginConfig.WindowHeight.Value);
            _background = _root.GetComponent<Image>();
            _root.AddComponent<RectMask2D>();
            _root.AddComponent<ScrollRelay>().OnScrolled = OnScrolled;
            Widgets.AddBorder(_root.transform, Widgets.Border);

            // Title bar: drag handle, mode and segment, buttons.
            GameObject bar = Widgets.Panel(_root.transform, "TitleBar", Widgets.TitleBackground, true);
            RectTransform barRect = Widgets.Rect(bar);
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = new Vector2(0f, TitleHeight);
            var drag = bar.AddComponent<WindowDrag>();
            drag.Window = _rect;
            drag.OnMoved = OnMovedByUser;

            TextMeshProUGUI backLabel;
            _back = Widgets.TextButton(bar.transform, "Back", "<", ButtonFont + 2f, Back, out backLabel);
            Widgets.TopLeft(Widgets.Rect(_back.gameObject), 2f, 1f, 18f, TitleHeight - 2f);
            backLabel.color = Widgets.Orange;
            _back.gameObject.SetActive(false);

            _title = Widgets.Label(bar.transform, "Title", ButtonFont + 1f, Widgets.Orange, TextAlignmentOptions.MidlineLeft);
            RectTransform titleRect = Widgets.Rect(_title.gameObject);
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(6f, 0f);
            titleRect.offsetMax = new Vector2(-150f, 0f);

            float x = 2f;
            x = AddTitleButton(bar.transform, "Close", "x", x, () => SetOpen(false));
            x = AddTitleButton(bar.transform, "Report", "Rep", x, Report);
            x = AddTitleButton(bar.transform, "Modes", "Mode", x, CycleMode);
            x = AddTitleButton(bar.transform, "Segments", "Seg", x, CycleSegment);
            x = AddTitleButton(bar.transform, "Reset", "Reset", x, Reset);
            _titleReserved = x + 4f;
            titleRect.offsetMax = new Vector2(-_titleReserved, 0f);

            // Rows live here, clipped by the window.
            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(_root.transform, false);
            _body = Widgets.Rect(bodyGo);
            Widgets.Stretch(_body, Padding, TitleHeight + Padding, Padding, Padding);

            // Resize grip in the corner.
            _resizeGrip = Widgets.Panel(_root.transform, "Resize", new Color(1f, 1f, 1f, 0.22f), true);
            RectTransform grip = Widgets.Rect(_resizeGrip);
            grip.anchorMin = new Vector2(1f, 0f);
            grip.anchorMax = new Vector2(1f, 0f);
            grip.pivot = new Vector2(1f, 0f);
            grip.anchoredPosition = new Vector2(-1f, 1f);
            grip.sizeDelta = new Vector2(12f, 12f);
            var resize = _resizeGrip.AddComponent<ResizeHandle>();
            resize.Window = _rect;
            resize.OnResized = OnResizedByUser;

            _tooltip.Create(parent);

            _rows.Clear();
            _hovered = null;
            _dirty = true;
            _builtRevision = -1;

            _root.SetActive(IsOpen && !_hidden);
            ApplyLayout();
        }

        private float AddTitleButton(Transform bar, string name, string text, float x, System.Action onClick)
        {
            TextMeshProUGUI label;
            Button b = Widgets.TextButton(bar, name, text, ButtonFont, onClick, out label);
            float w = Mathf.Ceil(label.GetPreferredValues(text).x) + 10f;
            Widgets.TopRight(Widgets.Rect(b.gameObject), x, 1f, w, TitleHeight - 2f);
            return x + w + 1f;
        }

        public void Destroy()
        {
            _tooltip.Destroy();
            if (_root != null)
                Object.Destroy(_root);
            _root = null;
            _rect = null;
            _body = null;
            _title = null;
            _rows.Clear();
            _hovered = null;
        }

        // ---- layout ----------------------------------------------------------------------

        /// <summary>Push the config onto the live window: size, position, opacity, fonts.</summary>
        public void ApplyLayout()
        {
            if (_rect == null || _suppressLayoutApply)
                return;

            float w = PluginConfig.WindowWidth.Value;
            float h = PluginConfig.WindowHeight.Value;
            _rect.sizeDelta = new Vector2(w, h);

            Vector2 pos = PluginConfig.WindowPosition.Value;
            if (pos == PluginConfig.DefaultPosition)
            {
                Vector2 canvas = Widgets.CanvasSize(_rect);
                pos = new Vector2(Mathf.Round(canvas.x - w - 24f), -Mathf.Round(canvas.y * 0.33f));
            }
            _rect.anchoredPosition = ClampToCanvas(pos, w);
            _lastSeenPosition = _rect.anchoredPosition;
            _lastSeenSize = _rect.sizeDelta;
            _positionSettleAt = 0f;

            Color bg = Widgets.Background;
            bg.a = PluginConfig.Opacity.Value;
            _background.color = bg;

            // Fonts and row heights: throw the rows away, they are rebuilt with the new metrics.
            foreach (Row r in _rows)
                Object.Destroy(r.Go);
            _rows.Clear();
            _hovered = null;
            _tooltip.Hide();
            _dirty = true;
        }

        /// <summary>Keep a usable amount of the window reachable after a resolution change.</summary>
        private Vector2 ClampToCanvas(Vector2 pos, float width)
        {
            const float margin = 60f;
            Vector2 canvas = Widgets.CanvasSize(_rect);
            return new Vector2(
                Mathf.Clamp(pos.x, margin - width, Mathf.Max(margin - width, canvas.x - margin)),
                Mathf.Clamp(pos.y, -Mathf.Max(0f, canvas.y - margin), 0f));
        }

        private void OnMovedByUser()
        {
            _positionSettleAt = Time.unscaledTime + PositionSettleDelay;
        }

        private void OnResizedByUser()
        {
            _positionSettleAt = Time.unscaledTime + PositionSettleDelay;
            _dirty = true;
        }

        /// <summary>
        /// BepInEx flushes the config file on every SettingChanged, so a drag is written back
        /// once it has settled rather than on every frame.
        /// </summary>
        private void PersistWhenSettled()
        {
            if (_rect == null)
                return;

            Vector2 pos = _rect.anchoredPosition;
            Vector2 size = _rect.sizeDelta;
            if (pos != _lastSeenPosition || size != _lastSeenSize)
            {
                _lastSeenPosition = pos;
                _lastSeenSize = size;
                _positionSettleAt = Time.unscaledTime + PositionSettleDelay;
                if (size != new Vector2(PluginConfig.WindowWidth.Value, PluginConfig.WindowHeight.Value))
                    _dirty = true;
                return;
            }

            if (_positionSettleAt <= 0f || Time.unscaledTime < _positionSettleAt)
                return;
            _positionSettleAt = 0f;

            _suppressLayoutApply = true;
            try
            {
                if (pos != PluginConfig.WindowPosition.Value)
                    PluginConfig.WindowPosition.Value = pos;
                if (size.x != PluginConfig.WindowWidth.Value)
                    PluginConfig.WindowWidth.Value = size.x;
                if (size.y != PluginConfig.WindowHeight.Value)
                    PluginConfig.WindowHeight.Value = size.y;
            }
            finally
            {
                _suppressLayoutApply = false;
            }
        }

        // ---- state -----------------------------------------------------------------------

        public void Toggle()
        {
            SetOpen(!IsOpen);
        }

        private bool _hidden;

        /// <summary>Follow the HUD's own hide toggle (screenshot mode) without changing the open state.</summary>
        public void SetHidden(bool hidden)
        {
            if (hidden == _hidden)
                return;
            _hidden = hidden;
            if (_root != null)
                _root.SetActive(IsOpen && !hidden);
            if (hidden)
                _tooltip.Hide();
        }

        public void SetOpen(bool open)
        {
            IsOpen = open;
            if (_root != null)
                _root.SetActive(open && !_hidden);
            if (!open)
            {
                _tooltip.Hide();
                _hovered = null;
            }
            else
                _dirty = true;
        }

        public void SetMode(MeterMode mode)
        {
            Mode = mode;
            Drill = null;
            Scroll = 0;
            _dirty = true;
        }

        public void CycleMode()
        {
            int i = System.Array.IndexOf(MeterView.Modes, Mode);
            SetMode(MeterView.Modes[(i + 1) % MeterView.Modes.Length]);
        }

        public void CycleSegment()
        {
            int count = _meter.Segments().Count;
            SegmentIndex = (SegmentIndex + 1) % count;
            Drill = null;
            Scroll = 0;
            _dirty = true;
        }

        public void Reset()
        {
            Recorder.Reset();
            SegmentIndex = 0;
            Drill = null;
            Scroll = 0;
            _dirty = true;
        }

        public void Back()
        {
            Drill = null;
            Scroll = 0;
            _dirty = true;
        }

        private void DrillInto(RowData row)
        {
            if (!row.Clickable)
                return;
            Drill = row.Key;
            Scroll = 0;
            _tooltip.Hide();
            _hovered = null;
            _dirty = true;
        }

        private void OnScrolled(float delta)
        {
            if (delta > 0f && Scroll > 0)
                Scroll--;
            else if (delta < 0f)
                Scroll++;
            _dirty = true;
        }

        public Segment CurrentSegment()
        {
            List<Segment> segs = _meter.Segments();
            if (SegmentIndex >= segs.Count)
                SegmentIndex = 0;
            return segs[SegmentIndex];
        }

        /// <summary>Post the current view to chat or the console.</summary>
        public void Report()
        {
            Segment seg = CurrentSegment();
            List<RowData> rows = MeterView.Rows(Mode, seg, Drill, Time.time);
            int n = Mathf.Min(rows.Count, PluginConfig.ReportLines.Value);

            var lines = new List<string>();
            string what = Drill != null ? Drill + " · " + MeterView.ModeName(Mode) : MeterView.ModeName(Mode);
            lines.Add("Recount: " + what + " (" + MeterView.SegmentName(seg, _meter) + ")");
            for (int i = 0; i < n; i++)
            {
                RowData r = rows[i];
                lines.Add((i + 1) + ". " + r.Label + " " + (r.Approximate ? "~" : "") + r.ValueText);
            }
            if (n == 0)
                lines.Add("nothing recorded");

            if (PluginConfig.ReportTo.Value == ReportTarget.Chat && Chat.instance != null)
            {
                foreach (string line in lines)
                    Chat.instance.SendText(Talker.Type.Normal, line);
            }
            else
            {
                foreach (string line in lines)
                {
                    if (Console.instance != null)
                        Console.instance.AddString(line);
                    RecountPlugin.Log.LogInfo(line);
                }
            }
        }

        // ---- per frame -------------------------------------------------------------------

        public void Render()
        {
            if (_root == null || !IsOpen)
                return;

            PersistWhenSettled();

            if (_hovered != null)
                _tooltip.Follow();

            float now = Time.time;
            bool timed = Mode == MeterMode.Dps && _meter.InFight && now >= _nextTimedRebuild;
            if (!_dirty && _meter.Revision == _builtRevision && !timed)
                return;

            _dirty = false;
            _builtRevision = _meter.Revision;
            _nextTimedRebuild = now + DpsRefresh;
            Rebuild();
        }

        private void Rebuild()
        {
            Segment seg = CurrentSegment();
            List<RowData> rows = MeterView.Rows(Mode, seg, Drill, Time.time);

            // Title
            var sb = new StringBuilder(64);
            if (Drill != null)
                sb.Append(Drill).Append("  <color=#BCAF97>·</color>  ").Append(MeterView.ModeName(Mode));
            else
                sb.Append(MeterView.ModeName(Mode)).Append("  <color=#BCAF97>·  ")
                  .Append(Mode == MeterMode.Records ? "all time" : MeterView.SegmentName(seg, _meter)).Append("</color>");
            _title.text = sb.ToString();
            _back.gameObject.SetActive(Drill != null);
            Widgets.Rect(_title.gameObject).offsetMin = new Vector2(Drill != null ? 22f : 6f, 0f);

            // Rows
            float rowHeight = PluginConfig.RowHeight.Value;
            float pitch = rowHeight + RowGap;
            float bodyHeight = _rect.sizeDelta.y - TitleHeight - Padding * 2f;
            float bodyWidth = _rect.sizeDelta.x - Padding * 2f;
            int visible = Mathf.Max(0, Mathf.FloorToInt((bodyHeight + RowGap) / pitch));

            int maxScroll = Mathf.Max(0, rows.Count - visible);
            if (Scroll > maxScroll)
                Scroll = maxScroll;

            float top = rows.Count > 0 ? rows[0].Value : 0f;
            float barAlpha = PluginConfig.BarOpacity.Value;
            float fontSize = PluginConfig.FontSize.Value;

            int shown = 0;
            for (int i = Scroll; i < rows.Count && shown < visible; i++, shown++)
            {
                Row row = shown < _rows.Count ? _rows[shown] : AddRow();
                RowData d = rows[i];
                row.Data = d;
                row.Go.SetActive(true);
                Widgets.TopLeft(row.Rect, 0f, shown * pitch, bodyWidth, rowHeight);

                float frac = top > 0f ? Mathf.Clamp01(d.Value / top) : 0f;
                Color c = d.Color;
                c.a = barAlpha;
                row.Bar.color = c;
                RectTransform barRect = Widgets.Rect(row.Bar.gameObject);
                barRect.anchorMin = Vector2.zero;
                barRect.anchorMax = new Vector2(frac, 1f);
                barRect.offsetMin = Vector2.zero;
                barRect.offsetMax = Vector2.zero;

                row.Left.fontSize = fontSize;
                row.Right.fontSize = fontSize;
                row.Left.text = (i + 1) + ". " + d.Label;
                row.Right.text = (d.Approximate ? "~" : "") + d.ValueText;

                // The value is right-aligned inside the same bar; keep the name from running
                // under it by reserving the value's width.
                float valueWidth = Mathf.Ceil(row.Right.GetPreferredValues(row.Right.text).x) + 6f;
                Widgets.Rect(row.Right.gameObject).sizeDelta = new Vector2(valueWidth, 0f);
                Widgets.Rect(row.Left.gameObject).offsetMax = new Vector2(-valueWidth - 4f, 0f);
            }
            for (int i = shown; i < _rows.Count; i++)
            {
                _rows[i].Go.SetActive(false);
                _rows[i].Data = null;
            }

            if (_hovered != null && (_hovered.Data == null || !_hovered.Go.activeSelf))
            {
                _hovered = null;
                _tooltip.Hide();
            }
            else if (_hovered != null)
                _tooltip.Show(_hovered.Data.Tooltip(), fontSize);
        }

        private Row AddRow()
        {
            var row = new Row();
            row.Go = Widgets.Panel(_body, "Row", Widgets.BarTrack, true);
            row.Rect = Widgets.Rect(row.Go);
            row.Handler = row.Go.AddComponent<RowHandler>();
            row.Handler.OnEnter = () => OnRowEnter(row);
            row.Handler.OnExit = () => OnRowExit(row);
            row.Handler.OnClick = () => { if (row.Data != null) DrillInto(row.Data); };

            row.Bar = Widgets.Panel(row.Go.transform, "Bar", Color.white, false).GetComponent<Image>();

            row.Left = Widgets.Label(row.Go.transform, "Name", PluginConfig.FontSize.Value, Widgets.Text, TextAlignmentOptions.MidlineLeft);
            RectTransform l = Widgets.Rect(row.Left.gameObject);
            l.anchorMin = Vector2.zero;
            l.anchorMax = Vector2.one;
            l.offsetMin = new Vector2(4f, 0f);
            l.offsetMax = new Vector2(-4f, 0f);

            row.Right = Widgets.Label(row.Go.transform, "Value", PluginConfig.FontSize.Value, Widgets.Text, TextAlignmentOptions.MidlineRight);
            RectTransform r = Widgets.Rect(row.Right.gameObject);
            r.anchorMin = new Vector2(1f, 0f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(1f, 0.5f);
            r.anchoredPosition = new Vector2(-4f, 0f);
            r.sizeDelta = new Vector2(80f, 0f);

            _rows.Add(row);
            return row;
        }

        private void OnRowEnter(Row row)
        {
            if (row.Data == null)
                return;
            _hovered = row;
            _tooltip.Show(row.Data.Tooltip(), PluginConfig.FontSize.Value);
        }

        private void OnRowExit(Row row)
        {
            if (ReferenceEquals(_hovered, row))
            {
                _hovered = null;
                _tooltip.Hide();
            }
        }
    }
}
