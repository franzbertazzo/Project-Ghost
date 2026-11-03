using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// The look of the menus: one palette, one set of type sizes, one button.
    ///
    /// The session menu and the match banners were both drawn with the default IMGUI skin, which is
    /// why they read as a debug overlay - grey boxes, no hover, no shared spacing, and every size in
    /// raw pixels so the whole thing shrank to a postage stamp on a high-resolution monitor. This
    /// fixes both at once: <see cref="Begin"/> scales the GUI matrix so layout can be written
    /// against a 1080p-tall screen and come out the same physical size anywhere, and everything
    /// drawn through here shares a palette with the gameplay HUD.
    ///
    /// Red is absent on purpose, exactly as in <c>PlayerPalette</c>: it belongs to incoming rockets
    /// and to failure, and a menu that uses it decoratively spends the one colour the player must
    /// never have to think about. <see cref="Danger"/> exists only for a failed connection.
    ///
    /// Still IMGUI. A menu is the one place a Canvas is genuinely better, but it is also the one
    /// place it matters least - this screen exists to get you into a match - so the cost paid here
    /// is a palette and a scale factor rather than a UI rebuild. When there is art direction, this
    /// file is the list of things the replacement has to provide.
    /// </summary>
    public static class MenuSkin
    {
        /// <summary>Layout is written for a screen this tall and scaled to whatever the screen is.</summary>
        public const float ReferenceHeight = 1080f;

        public static readonly Color Ink = new(0.95f, 0.97f, 1f, 1f);
        public static readonly Color Dim = new(0.95f, 0.97f, 1f, 0.62f);
        public static readonly Color Faint = new(0.95f, 0.97f, 1f, 0.26f);

        /// <summary>Steel blue. Deliberately not player 1's cyan, which means "that player".</summary>
        public static readonly Color Accent = new(0.55f, 0.8f, 1f, 1f);

        /// <summary>Keys you can press. Already the HUD's convention for the same thing.</summary>
        public static readonly Color Key = new(1f, 0.9f, 0.4f, 1f);

        public static readonly Color Danger = new(1f, 0.4f, 0.32f, 1f);

        private static readonly Color PanelFill = new(0.02f, 0.03f, 0.05f, 0.92f);
        private static readonly Color PanelEdge = new(0.55f, 0.8f, 1f, 0.28f);
        private static readonly Color SlotFill = new(1f, 1f, 1f, 0.04f);
        private static readonly Color ButtonIdle = new(0.55f, 0.8f, 1f, 0.08f);
        private static readonly Color ButtonHot = new(0.55f, 0.8f, 1f, 0.22f);
        private static readonly Color ButtonDown = new(0.55f, 0.8f, 1f, 0.4f);
        private static readonly Color PrimaryIdle = new(0.55f, 0.8f, 1f, 0.85f);
        private static readonly Color PrimaryHot = new(0.72f, 0.9f, 1f, 1f);

        private static Texture2D _white;
        private static Matrix4x4 _saved;
        private static GUIStyle _title;
        private static GUIStyle _heading;
        private static GUIStyle _body;
        private static GUIStyle _small;
        private static GUIStyle _code;
        private static GUIStyle _score;
        private static GUIStyle _button;
        private static GUIStyle _field;
        private static GUIStyle _placeholder;
        private static GUIStyle _banner;

        /// <summary>Screen pixels per layout unit. 1 at 1080p.</summary>
        public static float Scale => Mathf.Clamp(Screen.height / ReferenceHeight, 0.7f, 3f);

        /// <summary>
        /// Call first in OnGUI. Returns the screen in layout units - use it instead of
        /// Screen.width/height, which are still in real pixels and will be wrong once scaled.
        /// </summary>
        public static Rect Begin()
        {
            EnsureStyles();

            _saved = GUI.matrix;
            float scale = Scale;
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), Vector2.zero);

            return new Rect(0f, 0f, Screen.width / scale, Screen.height / scale);
        }

        /// <summary>Call last in OnGUI, so anything drawn after it is not scaled twice.</summary>
        public static void End() => GUI.matrix = _saved;

        // =====================================================================
        //  Shapes
        // =====================================================================

        public static void Fill(Rect rect, Color colour)
        {
            var previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, White);
            GUI.color = previous;
        }

        /// <summary>A one-unit outline, drawn as four fills rather than a nine-sliced texture.</summary>
        public static void Frame(Rect rect, Color colour, float thickness = 1f)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, thickness), colour);
            Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), colour);
            Fill(new Rect(rect.x, rect.y, thickness, rect.height), colour);
            Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), colour);
        }

        /// <summary>Flat dark panel with a lit edge. The one container shape the menus use.</summary>
        public static void Panel(Rect rect)
        {
            Fill(rect, PanelFill);
            Frame(rect, PanelEdge);

            // A brighter notch in the top-left corner: the panel reads as a piece of equipment
            // rather than a dialog, which is most of the tone for the cost of two fills.
            Fill(new Rect(rect.x, rect.y, 28f, 2f), Accent);
            Fill(new Rect(rect.x, rect.y, 2f, 28f), Accent);
        }

        public static void Rule(Rect rect) => Fill(rect, Faint);

        /// <summary>A labelled section rule: SMALL CAPS on the left, a line filling the rest.</summary>
        public static void SectionRule(Rect rect, string label)
        {
            float textWidth = Small.CalcSize(new GUIContent(label)).x + 10f;

            Text(new Rect(rect.x, rect.y - 7f, textWidth, 16f), label, Small, Accent,
                TextAnchor.MiddleLeft);

            Rule(new Rect(rect.x + textWidth, rect.y, rect.width - textWidth, 1f));
        }

        /// <summary>A horizontal 0-1 meter. Used for the connect timeout and round countdowns.</summary>
        public static void Meter(Rect rect, float fill, Color colour)
        {
            Fill(rect, SlotFill);
            Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height), colour);
        }

        // =====================================================================
        //  Text
        // =====================================================================

        public static void Text(Rect rect, string text, GUIStyle style, Color colour)
        {
            // Styles are shared and mutated per call rather than cloned per colour: IMGUI draws
            // immediately, so the colour only has to be right for the length of this one label.
            style.normal.textColor = colour;
            GUI.color = Color.white;
            GUI.Label(rect, text, style);
        }

        public static void Text(Rect rect, string text, GUIStyle style, Color colour,
            TextAnchor alignment)
        {
            var previous = style.alignment;
            style.alignment = alignment;
            Text(rect, text, style, colour);
            style.alignment = previous;
        }

        /// <summary>
        /// Tracked caps that are guaranteed to fit, by backing the spacing off until they do.
        ///
        /// Tracking is the one piece of styling here that can silently break the layout: it is
        /// spaces in the string, so a wordmark one letter longer can measure wider than its panel,
        /// and GUI.Label clips rather than shrinks - the first and last letters simply vanish, which
        /// looks like the title failing to draw rather than like a width problem. Measuring removes
        /// the guesswork, and means renaming the game is a one-line change again.
        /// </summary>
        public static string TrackToFit(string text, GUIStyle style, float maxWidth, int spacing = 3)
        {
            EnsureStyles();

            if (string.IsNullOrEmpty(text) || style == null)
                return text;

            for (int gap = spacing; gap > 0; gap--)
            {
                var candidate = Track(text, gap);

                if (style.CalcSize(new GUIContent(candidate)).x <= maxWidth)
                    return candidate;
            }

            // Untracked still overflows, so there is nothing left to give up. Returned anyway -
            // a clipped title is worse than a cramped one, but this is the caller's panel to widen.
            return text;
        }

        /// <summary>
        /// Letter-spacing, which IMGUI has no setting for. Only for short display strings - it is
        /// spaces in the string, so it breaks word wrapping and is unreadable on a sentence.
        /// Prefer <see cref="TrackToFit"/> anywhere the width is not obviously generous.
        /// </summary>
        public static string Track(string text, int spacing = 1)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var gap = new string(' ', spacing);
            var built = new System.Text.StringBuilder(text.Length * (spacing + 1));

            for (int i = 0; i < text.Length; i++)
            {
                if (i > 0)
                    built.Append(gap);

                built.Append(text[i]);
            }

            return built.ToString();
        }

        // =====================================================================
        //  Buttons
        // =====================================================================

        /// <summary>
        /// Drawn by hand and clicked through an invisible GUI.Button, so hover, press and disabled
        /// are three distinct looks. The default skin gives all three the same grey, which is most
        /// of why the old menu felt unresponsive.
        /// </summary>
        public static bool Button(Rect rect, string label, bool enabled = true, bool primary = false)
        {
            EnsureStyles();

            label = label.ToUpperInvariant();

            if (!enabled)
            {
                Fill(rect, SlotFill);
                Frame(rect, Faint);
                Text(rect, label, _button, Faint);
                return false;
            }

            bool hot = rect.Contains(Event.current.mousePosition);
            bool down = hot && Input.GetMouseButton(0);

            if (primary)
            {
                Fill(rect, down ? PrimaryHot : hot ? PrimaryHot : PrimaryIdle);
                Frame(rect, Ink);

                // Dark text on a light fill: the one high-contrast element on the screen, which is
                // what makes it read as the thing to press without an explanatory label.
                Text(rect, label, _button, new Color(0.02f, 0.04f, 0.07f));
            }
            else
            {
                Fill(rect, down ? ButtonDown : hot ? ButtonHot : ButtonIdle);
                Frame(rect, hot ? Accent : PanelEdge);
                Text(rect, label, _button, hot ? Ink : Dim);
            }

            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        /// <summary>
        /// A single-line text field, drawn like everything else here.
        ///
        /// A real <c>GUI.TextField</c>, unlike the join code's character cells - a name is free
        /// text, so there is nothing to normalise per keystroke and therefore none of the caret
        /// trouble that made a field the wrong control for the code. Returns the edited string.
        ///
        /// The control name is required: callers need to know whether this field has the keyboard
        /// before deciding what their own key handling does with a keystroke.
        /// </summary>
        public static string Field(Rect rect, string controlName, string value, int maxLength,
            string placeholder = null)
        {
            EnsureStyles();

            value ??= string.Empty;
            bool focused = GUI.GetNameOfFocusedControl() == controlName;

            Fill(rect, new Color(1f, 1f, 1f, focused ? 0.09f : 0.04f));
            Frame(rect, focused ? Accent : PanelEdge);

            GUI.SetNextControlName(controlName);

            _field.normal.textColor = Ink;
            _field.focused.textColor = Ink;
            _field.hover.textColor = Ink;
            GUI.color = Color.white;

            string edited = GUI.TextField(rect, value, maxLength, _field);

            // Drawn over the field rather than substituted into it, so the hint can never be
            // mistaken for a value and submitted as one.
            if (!focused && string.IsNullOrEmpty(edited) && !string.IsNullOrEmpty(placeholder))
                Text(rect, placeholder, _placeholder, Faint, TextAnchor.MiddleLeft);

            return edited;
        }

        // =====================================================================
        //  Styles
        // =====================================================================

        public static GUIStyle Title { get { EnsureStyles(); return _title; } }
        public static GUIStyle Banner { get { EnsureStyles(); return _banner; } }
        public static GUIStyle Heading { get { EnsureStyles(); return _heading; } }
        public static GUIStyle Body { get { EnsureStyles(); return _body; } }
        public static GUIStyle Small { get { EnsureStyles(); return _small; } }
        public static GUIStyle Code { get { EnsureStyles(); return _code; } }

        /// <summary>One or two digits in a cell. Smaller than Title so it cannot clip its box.</summary>
        public static GUIStyle Score { get { EnsureStyles(); return _score; } }

        private static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    _white.SetPixel(0, 0, Color.white);
                    _white.Apply();
                }

                return _white;
            }
        }

        private static void EnsureStyles()
        {
            // Rebuilt if a domain reload cleared them. GUI.skin is only valid inside OnGUI, which
            // is the only place any of this is reachable from.
            if (_title != null && _title.normal != null)
                return;

            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 40,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _banner = new GUIStyle(GUI.skin.label)
            {
                fontSize = 52,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _heading = new GUIStyle(GUI.skin.label)
            {
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _body = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };

            _small = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _code = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _score = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            // Built from the real textField style so it keeps the caret, selection and mouse
            // handling, with the chrome stripped: the frame and fill are drawn by Field().
            _field = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 17,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(12, 12, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(0, 0, 0, 0)
            };

            _field.normal.background = null;
            _field.hover.background = null;
            _field.focused.background = null;
            _field.active.background = null;

            _placeholder = new GUIStyle(_field)
            {
                fontStyle = FontStyle.Italic
            };

            _button = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }
    }
}
