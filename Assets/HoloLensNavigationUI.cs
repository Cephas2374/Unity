using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HoloLens 2 Navigation Controller
/// 
/// Creates a floating button panel in AR for navigating the 3D city.
/// On HoloLens, you can't use a physical keyboard, so this provides
/// tappable buttons for flying through the scene.
/// 
/// Button Layout (mimics keyboard):
///   [Q↰]  [W↑]  [E↗]
///   [A←]  [S↓]  [D→]
///         [X↓↓]
///   [R⤴]  [F⤵]  [Y⟳L] [C⟳R]
///
/// Q = Rotate Left    W = Forward     E = Rotate Right
/// A = Strafe Left    S = Backward    D = Strafe Right
/// R = Move Up        F = Move Down
/// Y = Yaw Left       X = Move Down (alt)  C = Yaw Right
///
/// The panel follows the user and can be toggled with a dedicated button.
/// </summary>
public class HoloLensNavigationUI : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("Movement speed in meters per second")]
    public float moveSpeed = 2.0f;
    
    [Tooltip("Fast movement speed (when boost is active)")]
    public float fastMoveSpeed = 8.0f;
    
    [Tooltip("Rotation speed in degrees per second")]
    public float rotationSpeed = 45f;
    
    [Header("UI Settings")]
    [Tooltip("Distance of navigation panel from camera (meters)")]
    public float panelDistance = 0.6f;
    
    [Tooltip("Vertical offset below eye level (meters)")]
    public float panelVerticalOffset = -0.3f;
    
    [Tooltip("Scale of the navigation panel")]
    public float panelScale = 0.0003f;
    
    [Header("XR Settings")]
    public bool isXRDevice = true;
    
    // Movement state (which directions are currently active)
    private bool moveForward, moveBackward, moveLeft, moveRight;
    private bool moveUp, moveDown;
    private bool rotateLeft, rotateRight;
    private bool isBoosting;
    
    // UI references
    private Canvas navCanvas;
    private GameObject navPanel;
    private bool panelVisible = true;
    private Camera mainCamera;
    
    // Color scheme
    private Color btnNormal = new Color(0.15f, 0.15f, 0.15f, 0.85f);
    private Color btnPressed = new Color(0.0f, 0.47f, 0.84f, 0.95f);
    private Color btnToggle = new Color(0.0f, 0.65f, 0.31f, 0.90f);
    private Color textColor = Color.white;
    
    void Start()
    {
        if (!isXRDevice) 
        {
            this.enabled = false;
            return;
        }
        
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("[HoloLensNav] No Main Camera found!");
            this.enabled = false;
            return;
        }
        
        CreateNavigationPanel();
        Debug.Log("<color=cyan>[HoloLensNav] Navigation panel created. Tap buttons to fly through the city.</color>");
    }
    
    void Update()
    {
        if (mainCamera == null) return;
        
        // Apply continuous movement based on active buttons
        float speed = isBoosting ? fastMoveSpeed : moveSpeed;
        float dt = Time.deltaTime;
        
        Transform camTransform = mainCamera.transform;
        
        // Get horizontal forward (ignore head pitch for movement)
        Vector3 flatForward = camTransform.forward;
        flatForward.y = 0;
        flatForward.Normalize();
        if (flatForward == Vector3.zero) flatForward = Vector3.forward;
        
        Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward) * -1f;
        
        Vector3 movement = Vector3.zero;
        
        if (moveForward)  movement += flatForward * speed * dt;
        if (moveBackward) movement -= flatForward * speed * dt;
        if (moveRight)    movement += flatRight * speed * dt;
        if (moveLeft)     movement -= flatRight * speed * dt;
        if (moveUp)       movement += Vector3.up * speed * dt;
        if (moveDown)     movement -= Vector3.up * speed * dt;
        
        if (movement != Vector3.zero)
        {
            // Move the camera's parent or the CesiumGeoreference origin
            // On HoloLens, we move the world origin (inverse of camera movement)
            var geoRef = FindObjectOfType<CesiumForUnity.CesiumGeoreference>();
            if (geoRef != null)
            {
                // Move the georeference opposite to desired camera movement
                // This effectively "moves" the user through the city
                geoRef.transform.position -= movement;
            }
            else
            {
                // Fallback: move camera directly
                camTransform.position += movement;
            }
        }
        
        // Rotation (yaw only)
        if (rotateLeft || rotateRight)
        {
            float yaw = 0f;
            if (rotateLeft) yaw -= rotationSpeed * dt;
            if (rotateRight) yaw += rotationSpeed * dt;
            
            var geoRef = FindObjectOfType<CesiumForUnity.CesiumGeoreference>();
            if (geoRef != null)
            {
                geoRef.transform.RotateAround(camTransform.position, Vector3.up, -yaw);
            }
        }
    }
    
    void LateUpdate()
    {
        // Keep panel positioned relative to user
        if (navPanel != null && panelVisible && mainCamera != null)
        {
            PositionPanel();
        }
    }
    
    void PositionPanel()
    {
        Vector3 forward = mainCamera.transform.forward;
        forward.y = 0;
        if (forward == Vector3.zero) forward = Vector3.forward;
        forward.Normalize();
        
        // Position: in front of user, slightly below eye level, slightly to the left
        Vector3 pos = mainCamera.transform.position 
            + forward * panelDistance 
            + Vector3.up * panelVerticalOffset
            + Vector3.Cross(Vector3.up, forward) * 0.15f; // Shift left slightly
        
        navPanel.transform.position = pos;
        navPanel.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }
    
    void CreateNavigationPanel()
    {
        // Create canvas
        GameObject canvasObj = new GameObject("HoloLensNavCanvas");
        navCanvas = canvasObj.AddComponent<Canvas>();
        navCanvas.renderMode = RenderMode.WorldSpace;
        
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        canvasObj.AddComponent<GraphicRaycaster>();
        
        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(700, 500);
        canvasRect.localScale = Vector3.one * panelScale;
        
        navPanel = canvasObj;
        
        // Background panel
        GameObject bgObj = CreateUIElement("NavBackground", canvasObj, new Vector2(700, 500));
        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = new Color(0.05f, 0.05f, 0.05f, 0.7f);
        
        // Title
        GameObject titleObj = CreateUIElement("Title", bgObj, new Vector2(700, 50));
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchoredPosition = new Vector2(0, 200);
        Text titleText = titleObj.AddComponent<Text>();
        titleText.text = "Navigation";
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize = 32;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = textColor;
        
        // === ROW 1: Q W E ===
        float row1Y = 120f;
        CreateNavButton(bgObj, "Q  ↰", -200, row1Y, () => rotateLeft = true, () => rotateLeft = false, "Rotate Left");
        CreateNavButton(bgObj, "W  ↑", 0, row1Y, () => moveForward = true, () => moveForward = false, "Forward");
        CreateNavButton(bgObj, "E  ↗", 200, row1Y, () => rotateRight = true, () => rotateRight = false, "Rotate Right");
        
        // === ROW 2: A S D ===
        float row2Y = 10f;
        CreateNavButton(bgObj, "A  ←", -200, row2Y, () => moveLeft = true, () => moveLeft = false, "Strafe Left");
        CreateNavButton(bgObj, "S  ↓", 0, row2Y, () => moveBackward = true, () => moveBackward = false, "Backward");
        CreateNavButton(bgObj, "D  →", 200, row2Y, () => moveRight = true, () => moveRight = false, "Strafe Right");
        
        // === ROW 3: R (up) F (down) + Y/C (yaw) ===
        float row3Y = -100f;
        CreateNavButton(bgObj, "R  ⬆", -200, row3Y, () => moveUp = true, () => moveUp = false, "Move Up");
        CreateNavButton(bgObj, "X  ⬇", 0, row3Y, () => moveDown = true, () => moveDown = false, "Move Down");
        
        // Boost toggle button
        CreateToggleButton(bgObj, "⚡FAST", 200, row3Y, 
            (active) => { isBoosting = active; }, "Toggle Fast Speed");
        
        // === BOTTOM: Hide button ===
        float row4Y = -200f;
        CreateActionButton(bgObj, "HIDE ✕", 0, row4Y, () => {
            panelVisible = false;
            navPanel.SetActive(false);
            // Create a small "show" button that stays in fixed position
            StartCoroutine(ShowMinimizedButton());
        }, "Minimize Panel");
    }
    
    System.Collections.IEnumerator ShowMinimizedButton()
    {
        // Wait a moment, then create a small floating button to reopen
        yield return new WaitForSeconds(0.5f);
        
        GameObject miniCanvas = new GameObject("NavMiniButton");
        Canvas mc = miniCanvas.AddComponent<Canvas>();
        mc.renderMode = RenderMode.WorldSpace;
        miniCanvas.AddComponent<CanvasScaler>();
        miniCanvas.AddComponent<GraphicRaycaster>();
        
        RectTransform mRect = miniCanvas.GetComponent<RectTransform>();
        mRect.sizeDelta = new Vector2(200, 80);
        mRect.localScale = Vector3.one * panelScale;
        
        GameObject btnObj = CreateUIElement("ShowBtn", miniCanvas, new Vector2(200, 80));
        Image btnImg = btnObj.AddComponent<Image>();
        btnImg.color = new Color(0.0f, 0.47f, 0.84f, 0.9f);
        
        Text btnText = CreateTextChild(btnObj, "NAV ☰", 24);
        
        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(() => {
            panelVisible = true;
            navPanel.SetActive(true);
            Destroy(miniCanvas);
        });
        
        // Position it
        while (miniCanvas != null && !panelVisible)
        {
            Vector3 fwd = mainCamera.transform.forward;
            fwd.y = 0;
            if (fwd == Vector3.zero) fwd = Vector3.forward;
            fwd.Normalize();
            
            miniCanvas.transform.position = mainCamera.transform.position 
                + fwd * 0.5f 
                + Vector3.up * panelVerticalOffset
                + Vector3.Cross(Vector3.up, fwd) * 0.25f;
            miniCanvas.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            
            yield return null;
        }
    }
    
    // === UI CREATION HELPERS ===
    
    void CreateNavButton(GameObject parent, string label, float x, float y,
        System.Action onPress, System.Action onRelease, string tooltip)
    {
        GameObject btnObj = CreateUIElement("Btn_" + label.Split(' ')[0], parent, new Vector2(170, 90));
        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(x, y);
        
        Image btnImage = btnObj.AddComponent<Image>();
        btnImage.color = btnNormal;
        
        Text text = CreateTextChild(btnObj, label, 28);
        
        // Use EventTrigger for press-and-hold (not just click)
        UnityEngine.EventSystems.EventTrigger trigger = btnObj.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        
        // PointerDown → start movement
        var pointerDown = new UnityEngine.EventSystems.EventTrigger.Entry();
        pointerDown.eventID = UnityEngine.EventSystems.EventTriggerType.PointerDown;
        pointerDown.callback.AddListener((data) => { 
            onPress?.Invoke();
            btnImage.color = btnPressed;
        });
        trigger.triggers.Add(pointerDown);
        
        // PointerUp → stop movement
        var pointerUp = new UnityEngine.EventSystems.EventTrigger.Entry();
        pointerUp.eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp;
        pointerUp.callback.AddListener((data) => { 
            onRelease?.Invoke();
            btnImage.color = btnNormal;
        });
        trigger.triggers.Add(pointerUp);
        
        // PointerExit → also stop (finger leaves button)
        var pointerExit = new UnityEngine.EventSystems.EventTrigger.Entry();
        pointerExit.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
        pointerExit.callback.AddListener((data) => { 
            onRelease?.Invoke();
            btnImage.color = btnNormal;
        });
        trigger.triggers.Add(pointerExit);
    }
    
    void CreateToggleButton(GameObject parent, string label, float x, float y,
        System.Action<bool> onToggle, string tooltip)
    {
        GameObject btnObj = CreateUIElement("Btn_Toggle", parent, new Vector2(170, 90));
        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(x, y);
        
        Image btnImage = btnObj.AddComponent<Image>();
        btnImage.color = btnNormal;
        
        Text text = CreateTextChild(btnObj, label, 24);
        
        bool isActive = false;
        
        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = btnImage;
        btn.onClick.AddListener(() => {
            isActive = !isActive;
            btnImage.color = isActive ? btnToggle : btnNormal;
            onToggle?.Invoke(isActive);
        });
    }
    
    void CreateActionButton(GameObject parent, string label, float x, float y,
        System.Action onClick, string tooltip)
    {
        GameObject btnObj = CreateUIElement("Btn_Action", parent, new Vector2(170, 70));
        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(x, y);
        
        Image btnImage = btnObj.AddComponent<Image>();
        btnImage.color = new Color(0.6f, 0.1f, 0.1f, 0.85f);
        
        Text text = CreateTextChild(btnObj, label, 22);
        
        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = btnImage;
        btn.onClick.AddListener(() => onClick?.Invoke());
    }
    
    GameObject CreateUIElement(string name, GameObject parent, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return obj;
    }
    
    Text CreateTextChild(GameObject parent, string label, int fontSize)
    {
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(parent.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        
        Text text = textObj.AddComponent<Text>();
        text.text = label;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = textColor;
        text.fontStyle = FontStyle.Bold;
        
        return text;
    }
    
    void OnDestroy()
    {
        if (navPanel != null) Destroy(navPanel);
    }
}
