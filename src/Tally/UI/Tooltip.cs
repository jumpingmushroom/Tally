using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tally.UI
{
    /// <summary>A dark box of text that follows the cursor while a row is hovered.</summary>
    public sealed class Tooltip
    {
        private const float Padding = 8f;
        private const float MaxWidth = 340f;

        private GameObject _root;
        private RectTransform _rect;
        private TextMeshProUGUI _text;
        private Image _bg;

        public bool Visible
        {
            get { return _root != null && _root.activeSelf; }
        }

        public void Create(Transform parent)
        {
            if (_root != null)
                return;

            _root = Widgets.Panel(parent, "TallyTooltip", new Color(0.03f, 0.025f, 0.02f, 0.92f), false);
            _rect = Widgets.Rect(_root);
            Widgets.TopLeft(_rect, 0f, 0f, 200f, 50f);
            _bg = _root.GetComponent<Image>();
            Widgets.AddBorder(_root.transform, Widgets.Border);

            _text = Widgets.Label(_root.transform, "Text", 13f, Widgets.Text, TextAlignmentOptions.TopLeft);
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.overflowMode = TextOverflowModes.Overflow;
            RectTransform tr = Widgets.Rect(_text.gameObject);
            Widgets.TopLeft(tr, Padding, Padding, MaxWidth - Padding * 2f, 0f);

            _root.SetActive(false);
        }

        public void Destroy()
        {
            if (_root != null)
                Object.Destroy(_root);
            _root = null;
            _rect = null;
            _text = null;
        }

        public void Show(string text, float fontSize)
        {
            if (_root == null)
                return;
            _text.fontSize = fontSize;
            _text.text = text;
            Vector2 pref = _text.GetPreferredValues(text, MaxWidth - Padding * 2f, 0f);
            float w = Mathf.Min(MaxWidth, pref.x + Padding * 2f + 2f);
            float h = pref.y + Padding * 2f + 2f;
            Widgets.Rect(_text.gameObject).sizeDelta = new Vector2(w - Padding * 2f, h - Padding * 2f);
            _rect.sizeDelta = new Vector2(w, h);
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            Follow();
        }

        public void Hide()
        {
            if (_root != null)
                _root.SetActive(false);
        }

        /// <summary>Sit below-right of the cursor, flipping to the other side at the edges.</summary>
        public void Follow()
        {
            if (!Visible)
                return;
            Vector2 m = Widgets.MouseInCanvas(_rect);
            Vector2 canvas = Widgets.CanvasSize(_rect);
            Vector2 size = _rect.sizeDelta;

            float x = m.x + 16f;
            float y = m.y + 20f;
            if (x + size.x > canvas.x - 4f)
                x = m.x - size.x - 8f;
            if (y + size.y > canvas.y - 4f)
                y = m.y - size.y - 8f;
            x = Mathf.Max(4f, x);
            y = Mathf.Max(4f, y);
            _rect.anchoredPosition = new Vector2(x, -y);
        }
    }
}
