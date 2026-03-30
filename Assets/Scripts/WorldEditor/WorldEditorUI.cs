using UnityEngine;
using UnityEngine.UIElements;

namespace Elysium.WorldEditor
{
    /// Minimal UI Toolkit toolbar for runtime GM world editing.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class WorldEditorUI : MonoBehaviour
    {
        [SerializeField] private WorldEditorManager manager;

        private UIDocument document;
        private Button sculptButton;
        private Button paintButton;
        private Button placeButton;
        private Button waterButton;
        private Button raiseButton;
        private Button lowerButton;
        private Button smoothButton;
        private Button flattenButton;
        private Button undoButton;
        private Slider radiusSlider;
        private Slider strengthSlider;
        private SliderInt paintLayerSlider;
        private Slider paintOpacitySlider;

        private EventCallback<ChangeEvent<float>> radiusChanged;
        private EventCallback<ChangeEvent<float>> strengthChanged;
        private EventCallback<ChangeEvent<int>> paintLayerChanged;
        private EventCallback<ChangeEvent<float>> paintOpacityChanged;

        private void Awake()
        {
            if (manager == null)
            {
                manager = FindAnyObjectByType<WorldEditorManager>();
            }

            document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            var root = document == null ? null : document.rootVisualElement;
            if (root == null || manager == null)
            {
                return;
            }

            sculptButton = root.Q<Button>("tool-sculpt");
            paintButton = root.Q<Button>("tool-paint");
            placeButton = root.Q<Button>("tool-place");
            waterButton = root.Q<Button>("tool-water");
            raiseButton = root.Q<Button>("sculpt-raise");
            lowerButton = root.Q<Button>("sculpt-lower");
            smoothButton = root.Q<Button>("sculpt-smooth");
            flattenButton = root.Q<Button>("sculpt-flatten");
            undoButton = root.Q<Button>("undo");

            radiusSlider = root.Q<Slider>("brush-radius");
            strengthSlider = root.Q<Slider>("brush-strength");
            paintLayerSlider = root.Q<SliderInt>("paint-layer");
            paintOpacitySlider = root.Q<Slider>("paint-opacity");

            if (sculptButton != null) sculptButton.clicked += () => manager.SetTool(WorldEditorTool.Sculpt);
            if (paintButton != null) paintButton.clicked += () => manager.SetTool(WorldEditorTool.Paint);
            if (placeButton != null) placeButton.clicked += () => manager.SetTool(WorldEditorTool.Place);
            if (waterButton != null) waterButton.clicked += () => manager.SetTool(WorldEditorTool.Water);

            if (raiseButton != null) raiseButton.clicked += () => manager.SetSculptOperation(SculptOperation.Raise);
            if (lowerButton != null) lowerButton.clicked += () => manager.SetSculptOperation(SculptOperation.Lower);
            if (smoothButton != null) smoothButton.clicked += () => manager.SetSculptOperation(SculptOperation.Smooth);
            if (flattenButton != null) flattenButton.clicked += () => manager.SetSculptOperation(SculptOperation.Flatten);
            if (undoButton != null) undoButton.clicked += () => manager.RequestUndo();

            if (radiusSlider != null)
            {
                radiusSlider.value = manager.SculptRadiusMeters;
                radiusChanged = evt => manager.SetSculptRadius(evt.newValue);
                radiusSlider.RegisterValueChangedCallback(radiusChanged);
            }

            if (strengthSlider != null)
            {
                strengthSlider.value = manager.SculptStrength;
                strengthChanged = evt => manager.SetSculptStrength(evt.newValue);
                strengthSlider.RegisterValueChangedCallback(strengthChanged);
            }

            if (paintLayerSlider != null)
            {
                paintLayerSlider.value = manager.PaintLayerIndex;
                paintLayerChanged = evt => manager.SetPaintLayer(evt.newValue);
                paintLayerSlider.RegisterValueChangedCallback(paintLayerChanged);
            }

            if (paintOpacitySlider != null)
            {
                paintOpacitySlider.value = manager.PaintOpacity;
                paintOpacityChanged = evt => manager.SetPaintOpacity(evt.newValue);
                paintOpacitySlider.RegisterValueChangedCallback(paintOpacityChanged);
            }
        }

        private void OnDisable()
        {
            if (radiusSlider != null && radiusChanged != null)
            {
                radiusSlider.UnregisterValueChangedCallback(radiusChanged);
            }

            if (strengthSlider != null && strengthChanged != null)
            {
                strengthSlider.UnregisterValueChangedCallback(strengthChanged);
            }

            if (paintLayerSlider != null && paintLayerChanged != null)
            {
                paintLayerSlider.UnregisterValueChangedCallback(paintLayerChanged);
            }

            if (paintOpacitySlider != null && paintOpacityChanged != null)
            {
                paintOpacitySlider.UnregisterValueChangedCallback(paintOpacityChanged);
            }
        }
    }
}
