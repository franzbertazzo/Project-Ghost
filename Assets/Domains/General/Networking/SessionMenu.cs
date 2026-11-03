using Game.UI;
using UnityEngine;

namespace Game.Networking
{
    /// <summary>
    /// Host / Join, and the code.
    ///
    /// Added automatically by <see cref="SessionController"/> and drawn only when this process is
    /// menu-driven, so an MPPM test instance never sees it. Presentation only: every button calls
    /// into the controller and reads State and Status back, which keeps the connection logic
    /// testable without a UI and the UI free of connection rules.
    ///
    /// Keyboard-first on purpose. The cursor is locked for the whole game and this is the one screen
    /// that unlocks it, so needing a click to focus a field before typing a code was friction on the
    /// only screen where it could be avoided: typing anywhere fills the code, Enter submits, Escape
    /// clears. The mouse still works, it is just never required.
    ///
    /// The code is five character cells rather than a text field, which is not only cosmetic. A
    /// <c>GUI.TextField</c> whose contents are normalised every frame has its caret pushed to the
    /// end on every keystroke, so a character in the middle of the code could not be corrected -
    /// owning the keystrokes fixes that and shows how many characters are expected at the same time.
    ///
    /// See <see cref="MenuSkin"/> for the palette and why this is still IMGUI.
    /// </summary>
    public class SessionMenu : MonoBehaviour
    {
        [SerializeField] private KeyCode leaveKey = KeyCode.Escape;

        private SessionController _session;
        private string _code = string.Empty;
        private string _name;
        private bool _showLeavePrompt;
        private bool _hadCursor;

        private void Awake()
        {
            _session = GetComponent<SessionController>();

            // Seeded from storage, which on a first run is seeded from the MPPM instance name - so
            // the field starts with something rather than empty, and four virtual players sharing
            // one PlayerPrefs store do not all start out called the same thing.
            _name = PlayerNames.Local;
        }

        private void Update()
        {
            if (_session == null || !_session.MenuDriven)
                return;

            if (_session.State == SessionState.Online && Input.GetKeyDown(leaveKey))
                _showLeavePrompt = !_showLeavePrompt;

            // The cursor belongs to whoever needs to click. CursorLock grabs it for gameplay the
            // moment a player spawns, so it has to be told to stand down while a menu is up -
            // otherwise the buttons are there and unclickable.
            bool wantsCursor = _session.State != SessionState.Online || _showLeavePrompt;
            CursorLock.Suspended = wantsCursor;

            if (wantsCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (_hadCursor)
            {
                // Taken back here rather than left to CursorLock, which only re-locks on Start and
                // on focus changes - neither of which happens when a menu simply closes.
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            _hadCursor = wantsCursor;
        }

        private void OnGUI()
        {
            if (_session == null || !_session.MenuDriven)
                return;

            // In front of everything. OnGUI order between components is not defined, and this
            // panel was landing behind the warmup strip - which for a modal asking whether to close
            // the room is not a cosmetic problem. Lower depth draws nearer the front; MatchHud sits
            // at 1 and the gameplay HUD leaves it at the default.
            GUI.depth = 0;

            var screen = MenuSkin.Begin();

            if (_session.State == SessionState.Online)
                DrawLeavePrompt(screen);
            else
                DrawMenu(screen);

            MenuSkin.End();
        }

        // =====================================================================
        //  The menu
        // =====================================================================

        private void DrawMenu(Rect screen)
        {
            // Key handling splits three ways, and the split is the whole reason the name field and
            // the code cells can coexist: a real TextField owns the keyboard while it is focused,
            // so the code cells must not also be reading keystrokes, or typing a name would fill in
            // a join code at the same time.
            bool nameFocused = GUI.GetNameOfFocusedControl() == NameControl;

            if (!_session.Busy)
            {
                if (nameFocused)
                    HandleNameKeys();
                else
                    HandleTyping();
            }

            const float width = 560f;
            float height = PanelHeight();
            var panel = new Rect((screen.width - width) * 0.5f, (screen.height - height) * 0.5f,
                width, height);

            // The backdrop camera is a lit arena, which a translucent panel cannot be read over.
            // Dimming the whole frame rather than making the panel opaque keeps the arena visible
            // behind it, which is the point of having a backdrop at all.
            MenuSkin.Fill(screen, new Color(0.01f, 0.02f, 0.04f, 0.72f));

            MenuSkin.Panel(panel);

            float x = panel.x + 34f;
            float inner = panel.width - 68f;

            // A running cursor rather than offsets from the panel top. The offsets were a standing
            // invitation to overlap - one of them already did - and a section that changes height
            // (the name field, the local options) means every number below it has to move.
            float y = panel.y + 24f;

            // Tracked caps, which is the whole of the tone: a wordmark rather than a window title.
            // Measured rather than hand-tuned - at eight characters this was overflowing the panel
            // and losing its first and last letter to clipping.
            MenuSkin.Text(new Rect(x, y, inner, 48f),
                MenuSkin.TrackToFit(GameName, MenuSkin.Title, inner), MenuSkin.Title, MenuSkin.Ink);
            y += 48f;

            MenuSkin.Text(new Rect(x, y, inner, 18f), "ZERO-G ROCKET DUELS", MenuSkin.Small,
                MenuSkin.Accent, TextAnchor.MiddleCenter);
            y += 30f;

            if (_session.Busy)
            {
                DrawConnecting(x, y, inner);
                return;
            }

            y = DrawNameField(x, y, inner);

            MenuSkin.SectionRule(new Rect(x, y, inner, 1f), "START A MATCH");
            y += 18f;

            if (MenuSkin.Button(new Rect(x, y, inner, 46f), "Host a match", true,
                    _code.Length == 0))
                _session.HostNew();

            y += 46f + SectionGap;

            MenuSkin.SectionRule(new Rect(x, y, inner, 1f), "OR JOIN ONE");
            y += 16f;

            // The cells draw their own hint line immediately below themselves, so the next thing
            // down has to clear the row plus that line.
            DrawCodeCells(new Rect(x, y, inner, 54f));
            y += 54f + 22f;

            bool complete = JoinCode.IsComplete(_code);

            if (MenuSkin.Button(new Rect(x, y, inner, 38f), "Join match", complete, complete))
                _session.JoinWithCode(_code);

            y += 38f + 8f;

            DrawStatus(new Rect(x, y, inner, 34f));
            y += 34f;

            y = DrawLocalOptions(x, y, inner);

            // Last, narrow and unlit. It is the one button here whose job is to end the session
            // rather than start one, so it should be findable without ever being the thing your
            // eye lands on first.
            if (MenuSkin.Button(new Rect(x + inner * 0.32f, y + 12f, inner * 0.36f, 32f), "Quit"))
                Quit();
        }

        /// <summary>
        /// Application.Quit does nothing in the editor, so play mode is stopped instead - without
        /// this the button would look broken in exactly the place it gets tested.
        /// </summary>
        private static void Quit()
        {
            Debug.Log("[Session] Quit requested.");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// The name every other player sees. Written straight through to
        /// <see cref="PlayerNames.Local"/>, which persists it - so it is remembered between runs and
        /// is already in place by the time a player object spawns and replicates it.
        /// </summary>
        private float DrawNameField(float x, float y, float inner)
        {
            MenuSkin.SectionRule(new Rect(x, y, inner, 1f), "YOUR NAME");
            y += 16f;

            string edited = MenuSkin.Field(new Rect(x, y, inner, 38f), NameControl, _name,
                PlayerNames.MaxLength, "what everyone else will call you");

            if (edited != _name)
            {
                _name = edited;

                // Sanitised on the way into storage rather than on the way into the field: cleaning
                // the string being edited would fight the caret, which is the mistake the join code
                // field used to make.
                PlayerNames.Local = edited;
            }

            return y + 38f + SectionGap;
        }

        /// <summary>
        /// Panel height, as the sum of the sections that will actually be drawn. Kept next to the
        /// cursor-based layout it has to agree with; both change together or the panel clips.
        /// </summary>
        private float PanelHeight()
        {
            if (_session.Busy)
                return 24f + 78f + 196f + 24f;

            float height = 24f + 78f    // header
                           + 54f + SectionGap   // name
                           + 64f + SectionGap   // host
                           + 92f + 38f + 8f     // join: rule, cells, hint, button
                           + 34f                // status
                           + 44f                // quit
                           + 24f;               // bottom margin

            if (ShowLocalOptions)
                height += 70f;

            return height;
        }

        private const float SectionGap = 16f;
        private const string NameControl = "ghost.name";

        /// <summary>
        /// The game's name, as the menu says it. Only the wordmark: the relay room prefix and the
        /// PlayerPrefs keys still read "ghost", deliberately - they are invisible to players, and
        /// changing the room prefix would stop two builds from finding each other's rooms.
        /// </summary>
        private const string GameName = "ZERO GUN";

        /// <summary>
        /// The localhost path, which is how the editor and MPPM actually talk to each other.
        ///
        /// Worth a visible button rather than a hidden default: MPPM's virtual players auto-connect
        /// on the scene transport and never see this menu, so without a way to host on that same
        /// transport the main editor could only open a relay room the clones could not reach. One
        /// click here is the connection they are already retrying for.
        ///
        /// Hidden in release builds - a player has no use for it and "host locally" next to "host a
        /// match" is a question nobody should have to answer.
        /// </summary>
        private float DrawLocalOptions(float x, float y, float inner)
        {
            if (!ShowLocalOptions)
                return y;

            MenuSkin.SectionRule(new Rect(x, y, inner, 1f), "LOCAL TESTING");
            y += 16f;

            float half = (inner - 10f) * 0.5f;

            if (MenuSkin.Button(new Rect(x, y, half, 34f), "Host locally"))
                _session.HostLocal();

            if (MenuSkin.Button(new Rect(x + half + 10f, y, half, 34f), "Join locally"))
                _session.JoinLocal();

            MenuSkin.Text(new Rect(x, y + 36f, inner, 16f),
                "no relay, no code - same machine or LAN", MenuSkin.Small, MenuSkin.Faint,
                TextAnchor.MiddleCenter);

            // 16 rule + 34 buttons + 16 hint, plus a little air before whatever follows.
            return y + 70f;
        }

        /// <summary>Editor and development builds only. See <see cref="DrawLocalOptions"/>.</summary>
        private static bool ShowLocalOptions => Application.isEditor || Debug.isDebugBuild;

        /// <summary>
        /// The five cells, with a blinking underline on the one being typed into.
        ///
        /// Empty cells are drawn as cells rather than as nothing, so the shape of what is wanted is
        /// visible before a single key is pressed - the old field gave no clue that a code was five
        /// characters until you were told.
        /// </summary>
        private void DrawCodeCells(Rect row)
        {
            int count = JoinCode.RequiredLength;
            const float gap = 8f;
            float cell = (row.width - gap * (count - 1)) / count;

            for (int i = 0; i < count; i++)
            {
                var rect = new Rect(row.x + (cell + gap) * i, row.y, cell, row.height);
                bool filled = i < _code.Length;
                bool next = i == _code.Length;

                MenuSkin.Fill(rect, new Color(1f, 1f, 1f, filled ? 0.07f : 0.03f));
                MenuSkin.Frame(rect, filled ? MenuSkin.Accent : MenuSkin.Faint);

                if (filled)
                {
                    MenuSkin.Text(rect, _code[i].ToString(), MenuSkin.Code, MenuSkin.Ink);
                    continue;
                }

                // Blinks on unscaled time so it keeps moving if anything ever pauses the game.
                if (next && Mathf.Repeat(Time.unscaledTime * 2f, 1f) > 0.4f)
                    MenuSkin.Fill(new Rect(rect.x + 8f, rect.yMax - 10f, rect.width - 16f, 2f),
                        MenuSkin.Accent);
            }

            string hint = _code.Length == 0
                ? "type the code your host read out"
                : JoinCode.IsComplete(_code)
                    ? "[ENTER] join"
                    : $"{JoinCode.RequiredLength - _code.Length} to go";

            MenuSkin.Text(new Rect(row.x, row.yMax + 2f, row.width, 16f), hint, MenuSkin.Small,
                _code.Length == 0 ? MenuSkin.Faint : MenuSkin.Key, TextAnchor.MiddleCenter);
        }

        /// <summary>
        /// Mid-attempt: elapsed seconds, a bar running out at the timeout, and a way out.
        /// </summary>
        private void DrawConnecting(float x, float y, float inner)
        {
            bool hosting = _session.State == SessionState.Opening;

            MenuSkin.SectionRule(new Rect(x, y, inner, 1f),
                hosting ? "OPENING A ROOM" : "JOINING");
            y += 18f;

            // Three dots cycling. Not decoration: it is the only thing on this screen that proves
            // the frame is still advancing while the relay is thinking.
            int dots = 1 + (int)(Time.unscaledTime * 2.5f) % 3;

            MenuSkin.Text(new Rect(x, y, inner, 44f),
                hosting
                    ? new string('.', dots)
                    : JoinCode.ForDisplay(_session.Code),
                MenuSkin.Code, MenuSkin.Ink);
            y += 54f;

            MenuSkin.Meter(new Rect(x, y, inner, 3f), _session.AttemptProgress, MenuSkin.Accent);
            y += 10f;

            MenuSkin.Text(new Rect(x, y, inner, 18f), $"{_session.AttemptElapsed:F0}s",
                MenuSkin.Small, MenuSkin.Dim, TextAnchor.MiddleCenter);
            y += 32f;

            if (MenuSkin.Button(new Rect(x + inner * 0.25f, y, inner * 0.5f, 36f), "Cancel"))
                _session.Leave();

            y += 46f;

            DrawStatus(new Rect(x, y, inner, 34f));
        }

        private void DrawStatus(Rect rect)
        {
            if (string.IsNullOrEmpty(_session.Status))
                return;

            bool failed = _session.State == SessionState.Failed;

            if (failed)
                MenuSkin.Fill(rect, new Color(1f, 0.4f, 0.32f, 0.1f));

            MenuSkin.Text(rect, _session.Status, MenuSkin.Body,
                failed ? MenuSkin.Danger : MenuSkin.Dim);
        }

        // =====================================================================
        //  In a match
        // =====================================================================

        /// <summary>
        /// Escape, in a match. Just the question and the two answers.
        ///
        /// No room code here. The code has exactly one home now - the collapsed warmup strip, which
        /// is on screen the whole time you are waiting for somebody - and repeating it in a modal
        /// made this panel a second place to look for the same thing.
        /// </summary>
        private void DrawLeavePrompt(Rect screen)
        {
            if (!_showLeavePrompt)
                return;

            // Dimmed behind, as well as drawn in front: this is a question that stops the game, and
            // the warmup panel showing through it made it read as another layer of HUD rather than
            // as something waiting on an answer.
            MenuSkin.Fill(screen, new Color(0.01f, 0.02f, 0.04f, 0.55f));

            const float width = 380f;
            const float height = 130f;
            var panel = new Rect((screen.width - width) * 0.5f, (screen.height - height) * 0.5f,
                width, height);

            MenuSkin.Panel(panel);

            float x = panel.x + 28f;
            float inner = panel.width - 56f;
            float y = panel.y + 26f;

            MenuSkin.Text(new Rect(x, y, inner, 20f),
                _session.IsHost ? "Close the room?" : "Leave the match?", MenuSkin.Body,
                MenuSkin.Ink);

            // Staying is the safe answer, so it is the lit one: closing a room drops everybody.
            if (MenuSkin.Button(new Rect(x, y + 32f, inner * 0.46f, 36f), "Leave"))
            {
                _showLeavePrompt = false;
                _session.Leave();
            }

            if (MenuSkin.Button(new Rect(x + inner * 0.54f, y + 32f, inner * 0.46f, 36f), "Stay",
                    true, true))
                _showLeavePrompt = false;
        }

        // =====================================================================
        //  Typing
        // =====================================================================

        /// <summary>
        /// The code field's keyboard, owned here instead of by a GUI.TextField.
        ///
        /// Enter means the obvious thing for whatever is on screen: host when no code has been
        /// typed, join once one is complete. Nothing is submitted on a partial code, so leaning on
        /// the key cannot start an attempt that is certain to fail.
        /// </summary>
        /// <summary>
        /// While the name field has the keyboard, Enter and Escape mean "done with this field"
        /// rather than "host" and "clear the code". Leaving them unhandled would make Enter look
        /// broken: a player types a name, presses Enter, and nothing at all happens.
        /// </summary>
        private void HandleNameKeys()
        {
            var e = Event.current;

            if (e.type != EventType.KeyDown)
                return;

            switch (e.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Escape:
                case KeyCode.Tab:
                    GUI.FocusControl(null);
                    e.Use();
                    break;
            }
        }

        private void HandleTyping()
        {
            var e = Event.current;

            if (e.type != EventType.KeyDown)
                return;

            if ((e.control || e.command) && e.keyCode == KeyCode.V)
            {
                // Codes arrive over Discord and Steam chat, so paste is the common case, not typing.
                _code = JoinCode.Normalise(_code + GUIUtility.systemCopyBuffer);
                e.Use();
                return;
            }

            switch (e.keyCode)
            {
                case KeyCode.Backspace when _code.Length > 0:
                    _code = _code.Substring(0, _code.Length - 1);
                    e.Use();
                    return;

                case KeyCode.Escape when _code.Length > 0:
                    _code = string.Empty;
                    e.Use();
                    return;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    // Only ever joins. Enter used to host when no code had been typed, which meant
                    // a stray keypress silently opened a relay room - and then trying to join one
                    // afterwards tore that room down and reconnected, which is a confusing way to
                    // find out you had hosted. Hosting is a button you press on purpose.
                    if (JoinCode.IsComplete(_code))
                        _session.JoinWithCode(_code);

                    e.Use();
                    return;
            }

            if (e.character == '\0' || _code.Length >= JoinCode.RequiredLength)
                return;

            // Normalise decides what is legal, including mapping the shapes that get misread - so
            // typing O puts a Q in, which is what the person reading the code aloud meant.
            var typed = JoinCode.Normalise(e.character.ToString());

            if (typed.Length == 0)
                return;

            _code = JoinCode.Normalise(_code + typed);
            e.Use();
        }
    }
}
