using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// The handful of shapes the HUD is built from, drawn with IMGUI.
    ///
    /// IMGUI has no line or circle primitive - everything is a stretched, rotated 1x1 texture - so
    /// each of these existed privately inside whichever HUD piece needed it first. One copy, shared,
    /// keeps the reticle and the enemy markers drawing at the same weight: two implementations of
    /// "a thin line" drift in thickness and antialiasing and start looking like two UIs.
    ///
    /// Deliberately IMGUI rather than a Canvas. The HUD is a crosshair, a few rings and a few
    /// markers that change every frame; a Canvas would mean pooled RectTransforms and a prefab per
    /// shape to draw things that are cheaper to rasterise directly. If the HUD ever grows buttons
    /// or layout, that is the moment to move it, not before.
    /// </summary>
    public static class HudDraw
    {
        private static Texture2D _pixel;

        /// <summary>
        /// The white 1x1 every shape is stretched from. Created once and kept: it survives scene
        /// loads and is cheap enough that per-component copies were pure duplication.
        /// </summary>
        public static Texture2D Pixel
        {
            get
            {
                if (_pixel == null)
                {
                    _pixel = new Texture2D(1, 1);
                    _pixel.SetPixel(0, 0, Color.white);
                    _pixel.Apply();
                }

                return _pixel;
            }
        }

        /// <summary>
        /// A line, as a thin box rotated about its start point. Tinted by the current GUI.color.
        /// </summary>
        public static void Line(Vector2 from, Vector2 to, float thickness)
        {
            var delta = to - from;
            float length = delta.magnitude;
            if (length < 0.01f)
                return;

            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, from);
            GUI.DrawTexture(new Rect(from.x, from.y - thickness * 0.5f, length, thickness), Pixel);
            GUI.matrix = matrix;
        }

        public static void Circle(Vector2 centre, float radius, float thickness, int segments = 48)
        {
            float step = Mathf.PI * 2f / segments;
            var previous = centre + new Vector2(radius, 0f);

            for (int i = 1; i <= segments; i++)
            {
                float angle = step * i;
                var next = centre + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                Line(previous, next, thickness);
                previous = next;
            }
        }

        /// <summary>
        /// A square stood on its corner, sized by the distance from centre to corner.
        ///
        /// Drawn from four explicit corners rather than as a rotated box, because rotating a box
        /// also rotates its stroke: the edges thin out at the corners and the shape reads as a
        /// blurred circle at the small sizes a distant marker uses.
        /// </summary>
        public static void Diamond(Vector2 centre, float radius, float thickness)
        {
            var up = centre + new Vector2(0f, -radius);
            var right = centre + new Vector2(radius, 0f);
            var down = centre + new Vector2(0f, radius);
            var left = centre + new Vector2(-radius, 0f);

            Line(up, right, thickness);
            Line(right, down, thickness);
            Line(down, left, thickness);
            Line(left, up, thickness);
        }
    }
}
