using System;
using Jotunn.Managers;
using Tally.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Tally.UI
{
    /// <summary>
    /// The handful of uGUI pieces the window is built from, in Valheim's palette. Flat images
    /// and Norse text: no panel art, so nothing to harvest from the game's UI that a patch
    /// could rename.
    /// </summary>
    public static class Widgets
    {
        public static readonly Color Text = new Color32(0xF2, 0xE9, 0xD8, 0xFF);
        public static readonly Color Dim = new Color32(0xBC, 0xAF, 0x97, 0xFF);
        public static readonly Color Border = new Color(0.62f, 0.52f, 0.36f, 0.75f);
        public static readonly Color Background = new Color(0.03f, 0.025f, 0.02f, 1f);
        public static readonly Color TitleBackground = new Color(0.12f, 0.09f, 0.06f, 0.55f);
        public static readonly Color BarTrack = new Color(1f, 1f, 1f, 0.05f);

        public static Color Orange
        {
            get { return GUIManager.Instance.ValheimOrange; }
        }

        public static TMP_FontAsset Font
        {
            get { return Fonts.Current; }
        }

        public static RectTransform Rect(GameObject go)
        {
            return (RectTransform)go.transform;
        }

        public static GameObject Panel(Transform parent, string name, Color color, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return go;
        }

        public static void Stretch(RectTransform r, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(left, bottom);
            r.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Anchor to the top-left corner with the pivot there too, so position and size read naturally.</summary>
        public static void TopLeft(RectTransform r, float x, float y, float width, float height)
        {
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(width, height);
        }

        /// <summary>Anchor to the top-right corner, pivot there, growing leftwards.</summary>
        public static void TopRight(RectTransform r, float x, float y, float width, float height)
        {
            r.anchorMin = new Vector2(1f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(1f, 1f);
            r.anchoredPosition = new Vector2(-x, -y);
            r.sizeDelta = new Vector2(width, height);
        }

        /// <summary>One-pixel frame made of four images, so it stays crisp at any size.</summary>
        public static void AddBorder(Transform parent, Color color)
        {
            Edge(parent, color, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), Vector2.zero);
            Edge(parent, color, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 1f));
            Edge(parent, color, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(1f, 0f));
            Edge(parent, color, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-1f, 0f), Vector2.zero);
        }

        private static void Edge(Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject go = Panel(parent, "Edge", color, false);
            RectTransform r = Rect(go);
            r.anchorMin = anchorMin;
            r.anchorMax = anchorMax;
            r.offsetMin = offsetMin;
            r.offsetMax = offsetMax;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
            t.font = Font;
            if (PluginConfig.TextOutline.Value)
            {
                Material outline = Fonts.Outline(t.font);
                if (outline != null)
                    t.fontSharedMaterial = outline;
            }
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Truncate;
            t.text = "";
            return t;
        }

        /// <summary>A flat text button: transparent until hovered, lit while pressed.</summary>
        public static Button TextButton(Transform parent, string name, string text, float fontSize, Action onClick, out TextMeshProUGUI label)
        {
            GameObject go = Panel(parent, name, new Color(1f, 1f, 1f, 0f), true);
            Button b = go.AddComponent<Button>();
            b.targetGraphic = go.GetComponent<Image>();
            b.transition = Selectable.Transition.ColorTint;
            ColorBlock cb = b.colors;
            cb.normalColor = new Color(1f, 1f, 1f, 0f);
            cb.highlightedColor = new Color(1f, 1f, 1f, 0.16f);
            cb.pressedColor = new Color(1f, 1f, 1f, 0.32f);
            cb.selectedColor = new Color(1f, 1f, 1f, 0f);
            cb.disabledColor = new Color(1f, 1f, 1f, 0f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.05f;
            b.colors = cb;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => onClick());

            label = Label(go.transform, "Text", fontSize, Text, TextAlignmentOptions.Center);
            Stretch(Rect(label.gameObject));
            label.text = text;
            return b;
        }

        public static float CanvasScale(Component c)
        {
            Canvas canvas = c.GetComponentInParent<Canvas>();
            if (canvas == null)
                return 1f;
            float s = canvas.rootCanvas.scaleFactor;
            return s > 0.01f ? s : 1f;
        }

        public static Vector2 CanvasSize(RectTransform any)
        {
            Canvas canvas = any.GetComponentInParent<Canvas>();
            RectTransform r = canvas != null ? (RectTransform)canvas.rootCanvas.transform : null;
            if (r != null && r.rect.width > 1f && r.rect.height > 1f)
                return new Vector2(r.rect.width, r.rect.height);
            return new Vector2(Screen.width, Screen.height);
        }

        /// <summary>Mouse position in the canvas's own units, from the top-left, y downwards.</summary>
        public static Vector2 MouseInCanvas(RectTransform any)
        {
            float s = CanvasScale(any);
            Vector2 size = CanvasSize(any);
            Vector3 m = Input.mousePosition;
            return new Vector2(m.x / s, size.y - m.y / s);
        }
    }

    /// <summary>Moves the window it is told about when the title bar is dragged.</summary>
    public sealed class WindowDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectTransform Window;
        public Action OnMoved;

        public void OnBeginDrag(PointerEventData eventData)
        {
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Window == null)
                return;
            float s = Widgets.CanvasScale(Window);
            Window.anchoredPosition += eventData.delta / s;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (OnMoved != null)
                OnMoved();
        }
    }

    /// <summary>Bottom-right corner grip that resizes the window.</summary>
    public sealed class ResizeHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectTransform Window;
        public Vector2 MinSize = new Vector2(180f, 80f);
        public Action OnResized;

        public void OnBeginDrag(PointerEventData eventData)
        {
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Window == null)
                return;
            float s = Widgets.CanvasScale(Window);
            Vector2 size = Window.sizeDelta + new Vector2(eventData.delta.x / s, -eventData.delta.y / s);
            size.x = Mathf.Max(MinSize.x, size.x);
            size.y = Mathf.Max(MinSize.y, size.y);
            Window.sizeDelta = size;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (OnResized != null)
                OnResized();
        }
    }

    /// <summary>Mouse wheel over the window pages the list.</summary>
    public sealed class ScrollRelay : MonoBehaviour, IScrollHandler
    {
        public Action<float> OnScrolled;

        public void OnScroll(PointerEventData eventData)
        {
            if (OnScrolled != null)
                OnScrolled(eventData.scrollDelta.y);
        }
    }

    /// <summary>Hover and click on one bar.</summary>
    public sealed class RowHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public Action OnEnter;
        public Action OnExit;
        public Action OnClick;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (OnEnter != null) OnEnter();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (OnExit != null) OnExit();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && OnClick != null)
                OnClick();
        }
    }
}
