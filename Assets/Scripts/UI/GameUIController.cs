using UnityEngine;
using UnityEngine.UIElements;

namespace HiAndSee.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class GameUIController : MonoBehaviour
    {
        public static GameUIController Instance { get; private set; }

        Label _roleLabel;
        Label _objectiveLabel;
        Label _taskLabel;
        Label _promptLabel;
        Label _resultLabel;
        VisualElement _progressFill;

        void OnEnable()
        {
            Instance = this;

            var root = GetComponent<UIDocument>().rootVisualElement;
            _roleLabel = root.Q<Label>("role-label");
            _objectiveLabel = root.Q<Label>("objective-label");
            _taskLabel = root.Q<Label>("task-label");
            _promptLabel = root.Q<Label>("prompt-label");
            _resultLabel = root.Q<Label>("result-label");
            _progressFill = root.Q<VisualElement>("progress-fill");

            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            UnityEngine.Cursor.visible = false;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        public void SetGameplayHud(string role, string objective, string taskText, string prompt, float progress, string result)
        {
            if (_roleLabel != null) _roleLabel.text = role;
            if (_objectiveLabel != null) _objectiveLabel.text = objective;
            if (_taskLabel != null) _taskLabel.text = taskText;
            if (_promptLabel != null) _promptLabel.text = prompt;
            if (_resultLabel != null) _resultLabel.text = result;
            if (_progressFill != null) _progressFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
        }
    }
}
