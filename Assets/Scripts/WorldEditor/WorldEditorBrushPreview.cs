using UnityEngine;

namespace Elysium.WorldEditor
{
    /// Draws the current brush radius as a world-space circle under the cursor.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class WorldEditorBrushPreview : MonoBehaviour
    {
        [SerializeField] private WorldEditorManager manager;
        [SerializeField, Min(16)] private int segmentCount = 48;
        [SerializeField] private Color previewColor = new Color(0.05f, 0.85f, 1f, 0.9f);
        [SerializeField, Min(0.005f)] private float lineWidth = 0.06f;
        [SerializeField, Min(0.005f)] private float yOffset = 0.05f;

        private LineRenderer line;

        private void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.loop = true;
            line.useWorldSpace = true;
            line.positionCount = Mathf.Max(16, segmentCount);
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;
            line.startColor = previewColor;
            line.endColor = previewColor;
            line.enabled = false;

            if (manager == null)
            {
                manager = FindAnyObjectByType<WorldEditorManager>();
            }
        }

        private void LateUpdate()
        {
            if (manager == null || !manager.GodModeEnabled)
            {
                line.enabled = false;
                return;
            }

            if (!manager.TryGetBrushWorldPoint(out var worldPoint))
            {
                line.enabled = false;
                return;
            }

            var radius = manager.ActiveTool == WorldEditorTool.Paint
                ? manager.PaintRadiusMeters
                : manager.SculptRadiusMeters;
            if (radius <= 0.01f)
            {
                line.enabled = false;
                return;
            }

            DrawRing(worldPoint + Vector3.up * yOffset, radius);
        }

        private void DrawRing(Vector3 center, float radius)
        {
            line.enabled = true;
            line.positionCount = Mathf.Max(16, segmentCount);
            for (var index = 0; index < line.positionCount; index++)
            {
                var t = (float)index / line.positionCount;
                var angle = t * Mathf.PI * 2f;
                var offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                line.SetPosition(index, center + offset);
            }
        }
    }
}
