using UnityEngine;

namespace Game.Networking
{
    /// <summary>
    /// A camera for the menu to sit in front of, and the only camera in the scene until somebody
    /// spawns.
    ///
    /// Every camera in this game lives on the player prefab, which only exists once a connection
    /// has produced a player. That was invisible while the game auto-hosted on play - a player
    /// appeared immediately - and became Unity's "No cameras rendering" the moment the session menu
    /// started deferring the connection. The menu was being drawn over nothing.
    ///
    /// Stands down the instant anything else renders, so it never fights the player's rig, and
    /// comes back if you leave the match. Deliberately left Untagged: tagging it MainCamera would
    /// make Camera.main point at the menu for every piece of gameplay code that falls back to it.
    /// </summary>
    public class MenuBackdropCamera : MonoBehaviour
    {
        [Tooltip("Where to watch the arena from while the menu is up. Far enough out to see the " +
                 "whole island, which is about 60m across including its debris.")]
        [SerializeField] private Vector3 viewpoint = new(18f, 28f, -92f);

        [SerializeField] private Vector3 lookAt = Vector3.zero;

        [Tooltip("Degrees per second of drift, so the menu is not a still frame.")]
        [SerializeField] private float orbitSpeed = 1.5f;

        private Camera _camera;
        private AudioListener _listener;

        private void Awake()
        {
            _camera = gameObject.GetComponent<Camera>();
            if (_camera == null)
                _camera = gameObject.AddComponent<Camera>();

            // Behind everything else, in case both are ever briefly enabled.
            _camera.depth = -100f;

            // Carried here so the scene is not without one while the menu is up; it goes away with
            // this object, which is before the player's own listener arrives. Two enabled
            // listeners is a warning and unpredictable audio.
            _listener = gameObject.GetComponent<AudioListener>();
            if (_listener == null)
                _listener = gameObject.AddComponent<AudioListener>();

            transform.position = viewpoint;
            transform.LookAt(lookAt);
        }

        private void Update()
        {
            // "Is anything else drawing?" - allCamerasCount counts only enabled cameras, so the
            // test differs by whether this one is currently in that count.
            bool othersRendering = _camera.enabled
                ? Camera.allCamerasCount > 1
                : Camera.allCamerasCount > 0;

            if (othersRendering)
            {
                SetRendering(false);
                return;
            }

            SetRendering(true);

            if (orbitSpeed != 0f)
            {
                transform.RotateAround(lookAt, Vector3.up, orbitSpeed * Time.unscaledDeltaTime);
                transform.LookAt(lookAt);
            }
        }

        private void SetRendering(bool on)
        {
            if (_camera.enabled == on)
                return;

            _camera.enabled = on;
            _listener.enabled = on;
        }
    }
}
