using TMPro;
using UnityEngine;

namespace Recount.UI
{
    /// <summary>"NEW RECORD" across the middle of the screen, held, then faded.</summary>
    public sealed class RecordBanner
    {
        private const float FadeSeconds = 0.8f;

        private GameObject _root;
        private CanvasGroup _group;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _sub;
        private float _holdUntil;
        private float _fadeUntil;

        public bool Created => _root != null;

        public void Create(Transform parent)
        {
            if (_root != null)
                return;

            _root = new GameObject("RecountBanner", typeof(RectTransform), typeof(CanvasGroup));
            _root.transform.SetParent(parent, false);
            RectTransform r = Widgets.Rect(_root);
            r.anchorMin = new Vector2(0.5f, 0.72f);
            r.anchorMax = new Vector2(0.5f, 0.72f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(1200f, 110f);

            _group = _root.GetComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            _title = Widgets.Label(_root.transform, "Title", 36f, Widgets.Orange, TextAlignmentOptions.Center);
            RectTransform tr = Widgets.Rect(_title.gameObject);
            tr.anchorMin = new Vector2(0f, 0.45f);
            tr.anchorMax = new Vector2(1f, 1f);
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;
            _title.fontStyle = FontStyles.Bold;

            _sub = Widgets.Label(_root.transform, "Sub", 20f, Widgets.Text, TextAlignmentOptions.Center);
            RectTransform sr = Widgets.Rect(_sub.gameObject);
            sr.anchorMin = new Vector2(0f, 0f);
            sr.anchorMax = new Vector2(1f, 0.45f);
            sr.offsetMin = Vector2.zero;
            sr.offsetMax = Vector2.zero;

            _root.SetActive(false);
        }

        public void Destroy()
        {
            if (_root != null)
                Object.Destroy(_root);
            _root = null;
            _group = null;
        }

        public void Show(string title, string sub, float seconds)
        {
            if (_root == null)
                return;
            _title.text = title;
            _sub.text = sub;
            _group.alpha = 1f;
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            _holdUntil = Time.unscaledTime + seconds;
            _fadeUntil = _holdUntil + FadeSeconds;
        }

        public void Update()
        {
            if (_root == null || !_root.activeSelf)
                return;
            float now = Time.unscaledTime;
            if (now < _holdUntil)
                return;
            if (now >= _fadeUntil)
            {
                _root.SetActive(false);
                return;
            }
            _group.alpha = 1f - (now - _holdUntil) / FadeSeconds;
        }
    }
}
