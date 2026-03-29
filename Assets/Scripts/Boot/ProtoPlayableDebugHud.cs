using System.Text;
using Elysium.Networking;
using UnityEngine;

namespace Elysium.Boot
{
    /// Lightweight runtime HUD for validating prototype session state without logs.
    [DisallowMultipleComponent]
    public sealed class ProtoPlayableDebugHud : MonoBehaviour
    {
        [SerializeField] private ProtoPlayableBootstrap bootstrap;
        [SerializeField] private bool visible = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.BackQuote;

        private readonly StringBuilder buffer = new StringBuilder(512);

        private GUIStyle panelStyle;
        private GUIStyle labelStyle;

        private void Awake()
        {
            if (bootstrap == null)
            {
                bootstrap = FindFirstObjectByType<ProtoPlayableBootstrap>();
            }

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(10, 10, 10, 10),
            };

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                richText = false,
            };
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                visible = !visible;
            }

            if (bootstrap == null)
            {
                bootstrap = FindFirstObjectByType<ProtoPlayableBootstrap>();
            }
        }

        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }

            if (bootstrap == null)
            {
                GUI.Box(new Rect(12f, 12f, 520f, 80f), "Proto HUD: bootstrap not found", panelStyle);
                return;
            }

            var session = bootstrap.Session;
            var area = bootstrap.AreaLifecycle;
            var activeCharacter = bootstrap.ActiveCharacterInstance;

            buffer.Length = 0;
            buffer.AppendLine("Elysium Prototype HUD");
            buffer.AppendLine($"Session: {session?.SessionId ?? "(none)"}");
            buffer.AppendLine($"State: {session?.State.ToString() ?? "(none)"}");
            buffer.AppendLine($"GM: {session?.GMPlayerId ?? "(none)"}");
            buffer.AppendLine($"Area: {area?.ActiveAreaId ?? "(none)"}");
            buffer.AppendLine($"Character: {bootstrap.AssignedCharacterId}");

            if (activeCharacter != null)
            {
                var p = activeCharacter.transform.position;
                buffer.AppendLine($"Avatar Pos: ({p.x:F1}, {p.y:F1}, {p.z:F1})");
            }
            else
            {
                buffer.AppendLine("Avatar Pos: (not spawned)");
            }

            var exploration = bootstrap.Exploration;
            if (exploration != null)
            {
                buffer.AppendLine($"Exploration Participants: {exploration.CurrentSnapshot?.Participants?.Count ?? 0}");
            }

            buffer.AppendLine($"Move Status: {bootstrap.LastMovementStatus}");
            buffer.AppendLine($"Toggle HUD: {toggleKey}");

            GUI.Box(new Rect(12f, 12f, 520f, 220f), buffer.ToString(), panelStyle);
            GUI.Label(new Rect(24f, 22f, 500f, 200f), buffer.ToString(), labelStyle);
        }
    }
}