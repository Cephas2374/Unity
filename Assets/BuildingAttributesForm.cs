using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>
/// Building Attributes Form - Clean architecture
/// Shows modal form for editing building properties
/// Fetches from API, displays in scrollable form, saves back to API
/// </summary>
public class BuildingAttributesForm : MonoBehaviour
{
    [Header("API Configuration")]
    public BuildingEnergyManager energyManager;
    public string communityId = "08417008";

    // UI References
    private GameObject modalBlocker;
    private GameObject formPanel;
    private ScrollRect scrollRect;
    private Canvas mainCanvas; // Store root canvas for dropdown template placement
    
    // Current building data
    private string currentGmlId;
    private string currentGmlIdBasic;
    private BuildingData currentBuildingData;
    
    // Store original API values to send back
    private JObject originalApiData;

    // UI Field References
    private Text titleText;
    private Dropdown constructionYearDropdown;
    private InputField numberOfStoreysInput;
    private Dropdown roofStoryDropdown;
    
    // Before Renovation
    private Dropdown heatingSystemBeforeDropdown;
    private Dropdown windowYearBeforeDropdown;
    private Dropdown wallYearBeforeDropdown;
    private Dropdown roofYearBeforeDropdown;
    private Dropdown ceilingYearBeforeDropdown;
    
    // After Renovation
    private Dropdown heatingSystemAfterDropdown;
    private Dropdown windowYearAfterDropdown;
    private Dropdown wallYearAfterDropdown;
    private Dropdown roofYearAfterDropdown;
    private Dropdown ceilingYearAfterDropdown;
    
    // Mapping dictionaries: Display Label -> API Code (like UE version)
    private Dictionary<string, string> constructionYearChoiceMap = new Dictionary<string, string>();
    private Dictionary<string, string> roofStoreyChoiceMap = new Dictionary<string, string>();
    private Dictionary<string, string> heatingSystemChoiceMap = new Dictionary<string, string>();
    
    // Reverse mappings: API Code -> Display Label
    private Dictionary<string, string> constructionYearReverseMap = new Dictionary<string, string>();
    private Dictionary<string, string> roofStoreyReverseMap = new Dictionary<string, string>();
    private Dictionary<string, string> heatingSystemReverseMap = new Dictionary<string, string>();

    void Start()
    {
        if (energyManager == null)
        {
            energyManager = FindObjectOfType<BuildingEnergyManager>();
            if (energyManager == null)
            {
                Debug.LogError("❌ BuildingEnergyManager not found!");
                return;
            }
        }
        
        Debug.Log("✅ BuildingAttributesForm ready - form will be created on first Ctrl+Click");
    }

    void Update()
    {
        // F5 to force refresh form UI
        if (Input.GetKeyDown(KeyCode.F5))
        {
            Debug.Log("🔄 F5 - Destroying form for refresh...");
            DestroyForm();
        }
        
        // Handle click outside form to close it
        if (modalBlocker != null && modalBlocker.activeInHierarchy && Input.GetMouseButtonDown(0))
        {
            if (!EventSystem.current.IsPointerOverGameObject())
            {
                // Click outside all UI  - ignore
                return;
            }
            
            // Check if click was on the form panel (not outside it)
            RectTransform formRect = formPanel != null ? formPanel.GetComponent<RectTransform>() : null;
            if (formRect != null && RectTransformUtility.RectangleContainsScreenPoint(formRect, Input.mousePosition))
            {
                // Click was ON the form, don't close
                return;
            }
            
            // Click was on modal blocker background (outside form), close it
            Debug.Log("🚪 Click outside form - closing");
            CloseForm();
        }
    }

    /// <summary>
    /// Show form for a specific building - called from CesiumMetadataReader on Ctrl+Click
    /// </summary>
    public void ShowBuildingForm(string gmlId, BuildingData buildingData)
    {
        Debug.Log($"📋 ShowBuildingForm called for: {gmlId}");
        
        currentGmlId = gmlId;
        // Use gmlIdBasic from buildingData (already in correct format from cache)
        currentGmlIdBasic = buildingData?.gmlIdBasic ?? gmlId?.Replace("_", "");
        currentBuildingData = buildingData;

        Debug.Log($"📋 currentGmlId = '{currentGmlId}'");
        Debug.Log($"📋 currentGmlIdBasic = '{currentGmlIdBasic}'");

        // Create form if it doesn't exist
        if (formPanel == null)
        {
            CreateFormUI();
        }

        // Populate with data and show
        PopulateFormFromAPI(buildingData);
        
        if (modalBlocker != null)
            modalBlocker.SetActive(true);
        if (formPanel != null)
            formPanel.SetActive(true);
            
        Debug.Log($"✅ Form displayed for: {gmlId}");
    }

    /// <summary>
    /// Create the entire form UI structure
    /// </summary>
    void CreateFormUI()
    {
        Debug.Log("🏗️ Creating form UI...");
        
        // Ensure EventSystem exists for UI interactions
        if (EventSystem.current == null)
        {
            GameObject eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<EventSystem>();
            
            // Check if using New Input System
            var inputSystemUIInputModuleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemUIInputModuleType != null)
            {
                // New Input System is available
                eventSystemObj.AddComponent(inputSystemUIInputModuleType);
                Debug.Log("✅ Created EventSystem with InputSystemUIInputModule (New Input System)");
            }
            else
            {
                // Fallback to old input system
                eventSystemObj.AddComponent<StandaloneInputModule>();
                Debug.Log("✅ Created EventSystem with StandaloneInputModule (Old Input System)");
            }
        }
        
        mainCanvas = FindObjectOfType<Canvas>();
        if (mainCanvas == null)
        {
            Debug.LogError("❌ No Canvas found in scene!");
            return;
        }
        
        // Ensure the canvas has a GraphicRaycaster for UI clicks to work
        if (mainCanvas.GetComponent<GraphicRaycaster>() == null)
        {
            mainCanvas.gameObject.AddComponent<GraphicRaycaster>();
            Debug.Log("✅ Added GraphicRaycaster to main canvas");
        }

        // === MODAL BLOCKER (Background) ===
        modalBlocker = new GameObject("BuildingForm_ModalBlocker");
        modalBlocker.transform.SetParent(mainCanvas.transform, false);
        
        RectTransform blockerRect = modalBlocker.AddComponent<RectTransform>();
        blockerRect.anchorMin = Vector2.zero;
        blockerRect.anchorMax = Vector2.one;
        blockerRect.sizeDelta = Vector2.zero;
        
        Image blockerImage = modalBlocker.AddComponent<Image>();
        blockerImage.color = new Color(0, 0, 0, 0.7f); // Semi-transparent black
        blockerImage.raycastTarget = false;  // CRITICAL: False so clicks pass through to form elements!
        
        // Use GraphicRaycaster to detect outside clicks without blocking form interaction
        GraphicRaycaster blockerRaycaster = modalBlocker.AddComponent<GraphicRaycaster>();
        
        // No Button here - it was blocking dropdown clicks!
        // Instead, we'll handle outside clicks via Update() checking
        
        Debug.Log("✅ Modal blocker created");

        // === FORM PANEL (Centered box) ===
        formPanel = new GameObject("BuildingAttributesFormPanel");
        formPanel.transform.SetParent(modalBlocker.transform, false);
        
        RectTransform formRect = formPanel.AddComponent<RectTransform>();
        formRect.sizeDelta = new Vector2(520, 1100); // width and height of the form panel
        formRect.anchoredPosition = Vector2.zero;
        formRect.anchorMin = new Vector2(0.5f, 0.5f);
        formRect.anchorMax = new Vector2(0.5f, 0.5f);
        formRect.pivot = new Vector2(0.5f, 0.5f);
        
        Image formBg = formPanel.AddComponent<Image>();
        formBg.color = new Color(0.95f, 0.95f, 0.95f, 1f);
        formBg.raycastTarget = true;
        
        Outline formOutline = formPanel.AddComponent<Outline>();
        formOutline.effectColor = Color.black;
        formOutline.effectDistance = new Vector2(3, -3);
        
        // Add ScrollBlocker to form panel to prevent map zoom
        formPanel.AddComponent<ScrollBlocker>();

        // === TITLE BAR ===
        GameObject titleBar = new GameObject("TitleBar");
        titleBar.transform.SetParent(formPanel.transform, false);
        
        RectTransform titleBarRect = titleBar.AddComponent<RectTransform>();
        titleBarRect.anchorMin = new Vector2(0, 1);
        titleBarRect.anchorMax = new Vector2(1, 1);
        titleBarRect.pivot = new Vector2(0.5f, 1);
        titleBarRect.sizeDelta = new Vector2(0, 60);
        titleBarRect.anchoredPosition = Vector2.zero;
        
        Image titleBarBg = titleBar.AddComponent<Image>();
        titleBarBg.color = new Color(0.12f, 0.53f, 0.70f, 1f);
        
        // Title Text
        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(titleBar.transform, false);
        
        RectTransform titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 0);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.sizeDelta = Vector2.zero;
        
        titleText = titleObj.AddComponent<Text>();
        titleText.text = "Building Attributes";
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize = 20;
        titleText.fontStyle = FontStyle.Bold;
        titleText.color = Color.white;
        titleText.alignment = TextAnchor.MiddleCenter;
        
        // Close Button
        GameObject closeBtn = CreateButton(titleBar.transform, "X", new Vector2(40, 40), 
            new Vector2(-10, -10), new Vector2(1, 1), new Vector2(1, 1));
        Button closeBtnComponent = closeBtn.GetComponent<Button>();
        closeBtnComponent.onClick.AddListener(() => {
            Debug.Log("🚪 Close button clicked!");
            CloseForm();
        });
        closeBtn.GetComponent<Image>().color = new Color(0.8f, 0.2f, 0.2f, 1f);
        
        Debug.Log($"✅ Close button created and listener added");

        // === SCROLL VIEW ===
        GameObject scrollView = new GameObject("ScrollView");
        scrollView.transform.SetParent(formPanel.transform, false);
        
        RectTransform scrollViewRect = scrollView.AddComponent<RectTransform>();
        scrollViewRect.anchorMin = new Vector2(0, 0);
        scrollViewRect.anchorMax = new Vector2(1, 1);
        scrollViewRect.offsetMin = new Vector2(15, 70); // Bottom margin for save button
        scrollViewRect.offsetMax = new Vector2(-15, -75); // Top margin for title bar
        
        scrollRect = scrollView.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 20f;
        
        // NOTE: NO ScrollBlocker here - this would prevent scrolling!

        // Viewport
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollView.transform, false);
        
        RectTransform viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.sizeDelta = Vector2.zero;
        
        Image viewportMask = viewport.AddComponent<Image>();
        viewportMask.color = Color.white;
        // Use RectMask2D instead of Mask so dropdown templates with Canvas components aren't clipped
        viewport.AddComponent<RectMask2D>();
        
        scrollRect.viewport = viewportRect;

        // Content Container
        GameObject content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        
        RectTransform contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.sizeDelta = new Vector2(0, 0);
        
        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 5;
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childScaleWidth = false;
        layout.childScaleHeight = false;
        
        // IMPORTANT: Add ContentSizeFitter for proper layout sizing
        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        scrollRect.content = contentRect;

        // === ADD FORM FIELDS ===
        
        // General Information
        AddSectionHeader(content, "General Information");
        constructionYearDropdown = AddDropdownField(content, "Construction year class", new List<string>());
        numberOfStoreysInput = AddInputField(content, "Number of Storeys");
        roofStoryDropdown = AddDropdownField(content, "Number/Type of Roof Storey", new List<string>());
        
        // Before Renovation
        AddSectionHeader(content, "Before Renovation");
        heatingSystemBeforeDropdown = AddDropdownField(content, "Heating system type 1", new List<string>());
        windowYearBeforeDropdown = AddDropdownField(content, "Construction year class of renovated window", new List<string>());
        wallYearBeforeDropdown = AddDropdownField(content, "Construction year class of renovated wall", new List<string>());
        roofYearBeforeDropdown = AddDropdownField(content, "Construction year class of renovated roof", new List<string>());
        ceilingYearBeforeDropdown = AddDropdownField(content, "Construction year class of renovated ceiling", new List<string>());
        
        // After Renovation
        AddSectionHeader(content, "After Renovation");
        heatingSystemAfterDropdown = AddDropdownField(content, "Heating system type 1", new List<string>());
        windowYearAfterDropdown = AddDropdownField(content, "Construction year class of renovated window", new List<string>());
        wallYearAfterDropdown = AddDropdownField(content, "Construction year class of renovated wall", new List<string>());
        roofYearAfterDropdown = AddDropdownField(content, "Construction year class of renovated roof", new List<string>());
        ceilingYearAfterDropdown = AddDropdownField(content, "Construction year class of renovated ceiling", new List<string>());

        // === SAVE BUTTON ===
        GameObject saveBtn = CreateButton(formPanel.transform, "Save Changes", new Vector2(200, 50),
            new Vector2(0, 10), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
        Button saveBtnComponent = saveBtn.GetComponent<Button>();
        saveBtnComponent.onClick.AddListener(() => {
            Debug.Log("💾 Save button clicked!");
            SaveBuildingInformation();
        });
        saveBtn.GetComponent<Image>().color = new Color(0.2f, 0.7f, 0.3f, 1f);
        
        Debug.Log($"✅ Save button created and listener added");
        
        // Force layout rebuild
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        
        // Start hidden
        modalBlocker.SetActive(false);
        
        Debug.Log("✅ Form UI created successfully");
    }

    // === UI HELPER METHODS ===

    Text AddSectionHeader(GameObject parent, string text)
    {
        GameObject headerObj = new GameObject($"Header_{text.Replace(" ", "")}");
        headerObj.transform.SetParent(parent.transform, false);
        
        LayoutElement layout = headerObj.AddComponent<LayoutElement>();
        layout.preferredHeight = 35;
        layout.minHeight = 35;
        
        Image bg = headerObj.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.53f, 0.70f, 1f);
        
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(headerObj.transform, false);
        
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.offsetMin = new Vector2(10, 0);
        
        Text headerText = textObj.AddComponent<Text>();
        headerText.text = text;
        headerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        headerText.fontSize = 16;
        headerText.fontStyle = FontStyle.Bold;
        headerText.color = Color.white;
        headerText.alignment = TextAnchor.MiddleLeft;
        
        return headerText;
    }

    Dropdown AddDropdownField(GameObject parent, string label, List<string> options)
    {
        GameObject fieldObj = new GameObject($"Field_{label.Replace(" ", "")}");
        fieldObj.transform.SetParent(parent.transform, false);
        
        LayoutElement fieldLayout = fieldObj.AddComponent<LayoutElement>();
        fieldLayout.preferredHeight = 55;
        fieldLayout.minHeight = 55;
        
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        
        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(fieldObj.transform, false);
        
        RectTransform labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 0.5f);
        labelRect.anchorMax = new Vector2(1, 1);
        labelRect.offsetMin = new Vector2(10, 0);
        labelRect.offsetMax = new Vector2(-10, 0);
        
        Text labelText = labelObj.AddComponent<Text>();
        labelText.text = label;
        labelText.font = font;
        labelText.fontSize = 13;
        labelText.fontStyle = FontStyle.Bold;
        labelText.color = new Color(0.2f, 0.2f, 0.2f);
        labelText.alignment = TextAnchor.MiddleLeft;
        
        // Dropdown
        GameObject dropdownObj = new GameObject("Dropdown");
        dropdownObj.transform.SetParent(fieldObj.transform, false);
        
        RectTransform dropdownRect = dropdownObj.AddComponent<RectTransform>();
        dropdownRect.anchorMin = new Vector2(0, 0);
        dropdownRect.anchorMax = new Vector2(1, 0.5f);
        dropdownRect.offsetMin = new Vector2(8, 4);
        dropdownRect.offsetMax = new Vector2(-8, -2);
        
        Image dropdownBg = dropdownObj.AddComponent<Image>();
        dropdownBg.type = Image.Type.Simple;
        dropdownBg.color = Color.white;
        dropdownBg.raycastTarget = true;  // Button itself needs raycast for clicking
        
        Outline border = dropdownObj.AddComponent<Outline>();
        border.effectColor = new Color(0.5f, 0.5f, 0.5f);
        border.effectDistance = new Vector2(1, -1);
        
        Dropdown dropdown = dropdownObj.AddComponent<Dropdown>();
        dropdown.targetGraphic = dropdownBg;
        dropdown.interactable = true;
        
        // Add color blocks for visual feedback
        ColorBlock dropdownColors = dropdown.colors;
        dropdownColors.normalColor = Color.white;
        dropdownColors.highlightedColor = new Color(0.95f, 0.95f, 0.95f);
        dropdownColors.pressedColor = new Color(0.90f, 0.90f, 0.90f);
        dropdownColors.selectedColor = new Color(0.95f, 0.95f, 0.95f);
        dropdown.colors = dropdownColors;
        
        // Dropdown label
        GameObject dropdownLabelObj = new GameObject("Label");
        dropdownLabelObj.transform.SetParent(dropdownObj.transform, false);
        
        RectTransform dropdownLabelRect = dropdownLabelObj.AddComponent<RectTransform>();
        dropdownLabelRect.anchorMin = Vector2.zero;
        dropdownLabelRect.anchorMax = Vector2.one;
        dropdownLabelRect.offsetMin = new Vector2(10, 0);
        dropdownLabelRect.offsetMax = new Vector2(-25, 0);
        
        Text dropdownLabelText = dropdownLabelObj.AddComponent<Text>();
        dropdownLabelText.font = font;
        dropdownLabelText.fontSize = 14;
        dropdownLabelText.color = Color.black;
        dropdownLabelText.alignment = TextAnchor.MiddleLeft;
        
        dropdown.captionText = dropdownLabelText;
        
        // Arrow
        GameObject arrowObj = new GameObject("Arrow");
        arrowObj.transform.SetParent(dropdownObj.transform, false);
        
        RectTransform arrowRect = arrowObj.AddComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(1, 0.5f);
        arrowRect.anchorMax = new Vector2(1, 0.5f);
        arrowRect.pivot = new Vector2(1, 0.5f);
        arrowRect.sizeDelta = new Vector2(20, 20);
        arrowRect.anchoredPosition = new Vector2(-5, 0);
        
        Text arrowText = arrowObj.AddComponent<Text>();
        arrowText.text = "▼";
        arrowText.font = font;
        arrowText.fontSize = 12;
        arrowText.color = Color.black;
        arrowText.alignment = TextAnchor.MiddleCenter;
        
        // Template must be a CHILD of the dropdown for Unity to position it correctly
        // Unity's Dropdown.Show() adds Canvas(sortingOrder=30000) which escapes RectMask2D clipping
        GameObject template = CreateDropdownTemplate(dropdownObj.transform, mainCanvas, out Text templateItemText, out Image templateItemImage);
        
        dropdown.template = template.GetComponent<RectTransform>();
        dropdown.itemText = templateItemText;
        dropdown.itemImage = templateItemImage;  // FIX: Set itemImage reference
        
        Debug.Log($"✅ Dropdown '{label}' created:");
        Debug.Log($"   - Template: {(template != null ? "YES" : "NO")}");
        Debug.Log($"   - Template parent: {(template.transform.parent != null ? template.transform.parent.name : "root")}");
        Debug.Log($"   - Template.RectTransform: {(dropdown.template != null ? "YES" : "NO")}");
        Debug.Log($"   - ItemText: {(templateItemText != null ? "YES" : "NO")}");
        Debug.Log($"   - Interactable: {dropdown.interactable}");
        
        // Add options
        dropdown.options.Clear();
        foreach (string opt in options)
        {
            dropdown.options.Add(new Dropdown.OptionData(opt));
        }
        Debug.Log($"   - Options: {dropdown.options.Count}");
        Debug.Log($"   - Dropdown list should appear below button when clicked");
        
        return dropdown;
    }

    GameObject CreateDropdownTemplate(Transform parent, Canvas rootCanvas, out Text itemTextOut, out Image itemImageOut)
    {
        // Template is a child of the dropdown - Unity positions it automatically
        // Unity's Dropdown.Show() adds Canvas(overrideSorting, sortingOrder=30000)
        // and GraphicRaycaster, which escapes RectMask2D clipping
        GameObject template = new GameObject("Template");
        template.transform.SetParent(parent, false);  // parent is now the dropdown itself
        template.SetActive(false);
        
        RectTransform templateRect = template.AddComponent<RectTransform>();
        // Standard Unity Dropdown template anchors: stretch horizontally, anchor at bottom
        templateRect.anchorMin = new Vector2(0, 0);
        templateRect.anchorMax = new Vector2(1, 0);
        templateRect.pivot = new Vector2(0.5f, 1);  // Top-center pivot so it drops DOWN
        templateRect.sizeDelta = new Vector2(0, 200);  // Width stretches to match button, height = 200
        templateRect.anchoredPosition = new Vector2(0, 2);  // Slight gap below button
        
        // DO NOT add Canvas or GraphicRaycaster here!
        // Unity's Dropdown.Show() adds them automatically with sortingOrder=30000
        // which properly escapes RectMask2D clipping
        
        Image templateBg = template.AddComponent<Image>();
        templateBg.type = Image.Type.Simple;
        templateBg.color = new Color(1, 1, 1, 1);
        templateBg.raycastTarget = true;
        
        Outline templateBorder = template.AddComponent<Outline>();
        templateBorder.effectColor = Color.black;
        templateBorder.effectDistance = new Vector2(2, -2);
        
        ScrollRect templateScroll = template.AddComponent<ScrollRect>();
        templateScroll.horizontal = false;
        templateScroll.vertical = true;
        templateScroll.scrollSensitivity = 1;
        templateScroll.elasticity = 0.1f;
        templateScroll.decelerationRate = 0.95f;
        templateScroll.movementType = ScrollRect.MovementType.Elastic;
        
        // Viewport
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(template.transform, false);
        
        RectTransform viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.sizeDelta = Vector2.zero;
        
        Image viewportImage = viewport.AddComponent<Image>();
        viewportImage.type = Image.Type.Simple;
        viewportImage.color = new Color(1, 1, 1, 1);
        viewportImage.raycastTarget = false;
        
        CanvasGroup viewportCanvasGroup = viewport.AddComponent<CanvasGroup>();
        viewportCanvasGroup.alpha = 1;
        
        // Mask for clipping content to viewport bounds
        Mask mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = true;
        
        templateScroll.viewport = viewportRect;
        
        // Content
        GameObject content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        
        RectTransform contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.sizeDelta = new Vector2(0, 28);
        
        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.childForceExpandHeight = false;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childControlWidth = true;
        contentLayout.spacing = 2;
        contentLayout.padding = new RectOffset(0, 0, 0, 0);
        
        // FIX: Add ContentSizeFitter for proper template content sizing
        ContentSizeFitter contentFitter = content.AddComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        
        LayoutElement contentLayoutElem = content.AddComponent<LayoutElement>();
        // Let ContentSizeFitter drive height - don't override it
        // contentLayoutElem.preferredHeight = 200;  // REMOVED - let fitter handle it
        contentLayoutElem.flexibleHeight = 0;
        
        templateScroll.content = contentRect;
        
        // Item
        GameObject item = new GameObject("Item");
        item.transform.SetParent(content.transform, false);
        
        RectTransform itemRect = item.AddComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0, 0.5f);
        itemRect.anchorMax = new Vector2(1, 0.5f);
        itemRect.sizeDelta = new Vector2(0, 28);
        
        LayoutElement itemLayout = item.AddComponent<LayoutElement>();
        itemLayout.preferredHeight = 28;
        itemLayout.minHeight = 28;
        
        Toggle itemToggle = item.AddComponent<Toggle>();
        itemToggle.interactable = true;
        itemToggle.transition = Selectable.Transition.ColorTint;
        
        GameObject itemBg = new GameObject("ItemBackground");
        itemBg.transform.SetParent(item.transform, false);
        
        RectTransform itemBgRect = itemBg.AddComponent<RectTransform>();
        itemBgRect.anchorMin = Vector2.zero;
        itemBgRect.anchorMax = Vector2.one;
        itemBgRect.sizeDelta = Vector2.zero;
        
        Image itemBgImg = itemBg.AddComponent<Image>();
        itemBgImg.type = Image.Type.Simple;
        itemBgImg.color = Color.white;
        itemBgImg.raycastTarget = true;
        
        itemToggle.targetGraphic = itemBgImg;
        
        ColorBlock itemColors = itemToggle.colors;
        itemColors.normalColor = Color.white;
        itemColors.highlightedColor = new Color(0.90f, 0.95f, 1.0f);
        itemColors.pressedColor = new Color(0.80f, 0.90f, 1.0f);
        itemColors.selectedColor = new Color(0.85f, 0.93f, 1.0f);
        itemToggle.colors = itemColors;
        
        GameObject itemLabel = new GameObject("ItemLabel");
        itemLabel.transform.SetParent(item.transform, false);
        
        RectTransform itemLabelRect = itemLabel.AddComponent<RectTransform>();
        itemLabelRect.anchorMin = Vector2.zero;
        itemLabelRect.anchorMax = Vector2.one;
        itemLabelRect.offsetMin = new Vector2(20, 0);
        itemLabelRect.offsetMax = new Vector2(-5, 0);
        
        Text itemText = itemLabel.AddComponent<Text>();
        itemText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        itemText.fontSize = 14;
        itemText.color = Color.black;
        itemText.alignment = TextAnchor.MiddleLeft;
        
        // Add checkmark for selected item
        GameObject checkmark = new GameObject("ItemCheckmark");
        checkmark.transform.SetParent(item.transform, false);
        
        RectTransform checkmarkRect = checkmark.AddComponent<RectTransform>();
        checkmarkRect.anchorMin = new Vector2(0, 0.5f);
        checkmarkRect.anchorMax = new Vector2(0, 0.5f);
        checkmarkRect.pivot = new Vector2(0.5f, 0.5f);
        checkmarkRect.sizeDelta = new Vector2(20, 20);
        checkmarkRect.anchoredPosition = new Vector2(10, 0);
        
        Text checkmarkText = checkmark.AddComponent<Text>();
        checkmarkText.text = "✓";
        checkmarkText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        checkmarkText.fontSize = 16;
        checkmarkText.fontStyle = FontStyle.Bold;
        checkmarkText.color = new Color(0.2f, 0.6f, 0.9f);
        checkmarkText.alignment = TextAnchor.MiddleCenter;
        
        itemToggle.graphic = checkmarkText;
        
        itemTextOut = itemText;
        itemImageOut = itemBgImg;  // FIX: Return the background image for itemImage reference
        return template;
    }

    InputField AddInputField(GameObject parent, string label)
    {
        GameObject fieldObj = new GameObject($"Field_{label.Replace(" ", "")}");
        fieldObj.transform.SetParent(parent.transform, false);
        
        LayoutElement fieldLayout = fieldObj.AddComponent<LayoutElement>();
        fieldLayout.preferredHeight = 55;
        fieldLayout.minHeight = 55;
        
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        
        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(fieldObj.transform, false);
        
        RectTransform labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 0.5f);
        labelRect.anchorMax = new Vector2(1, 1);
        labelRect.offsetMin = new Vector2(10, 0);
        labelRect.offsetMax = new Vector2(-10, 0);
        
        Text labelText = labelObj.AddComponent<Text>();
        labelText.text = label;
        labelText.font = font;
        labelText.fontSize = 13;
        labelText.fontStyle = FontStyle.Bold;
        labelText.color = new Color(0.2f, 0.2f, 0.2f);
        labelText.alignment = TextAnchor.MiddleLeft;
        
        // Input Field
        GameObject inputObj = new GameObject("InputField");
        inputObj.transform.SetParent(fieldObj.transform, false);
        
        RectTransform inputRect = inputObj.AddComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0, 0);
        inputRect.anchorMax = new Vector2(1, 0.5f);
        inputRect.offsetMin = new Vector2(8, 4);
        inputRect.offsetMax = new Vector2(-8, -2);
        
        Image inputBg = inputObj.AddComponent<Image>();
        inputBg.color = Color.white;
        inputBg.raycastTarget = true;
        
        Outline border = inputObj.AddComponent<Outline>();
        border.effectColor = new Color(0.5f, 0.5f, 0.5f);
        border.effectDistance = new Vector2(1, -1);
        
        InputField inputField = inputObj.AddComponent<InputField>();
        inputField.targetGraphic = inputBg;
        
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(inputObj.transform, false);
        
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 0);
        textRect.offsetMax = new Vector2(-10, 0);
        
        Text textComponent = textObj.AddComponent<Text>();
        textComponent.font = font;
        textComponent.fontSize = 14;
        textComponent.color = Color.black;
        textComponent.alignment = TextAnchor.MiddleLeft;
        textComponent.supportRichText = false;
        
        inputField.textComponent = textComponent;
        
        return inputField;
    }

    GameObject CreateButton(Transform parent, string text, Vector2 size, Vector2 position, 
        Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject btnObj = new GameObject($"Button_{text.Replace(" ", "")}");
        btnObj.transform.SetParent(parent, false);
        
        RectTransform btnRect = btnObj.AddComponent<RectTransform>();
        btnRect.sizeDelta = size;
        btnRect.anchoredPosition = position;
        btnRect.anchorMin = anchorMin;
        btnRect.anchorMax = anchorMax;
        btnRect.pivot = new Vector2(0.5f, 0.5f);
        
        Image btnImg = btnObj.AddComponent<Image>();
        btnImg.color = new Color(0.8f, 0.8f, 0.8f);
        
        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        
        GameObject btnTextObj = new GameObject("Text");
        btnTextObj.transform.SetParent(btnObj.transform, false);
        
        RectTransform btnTextRect = btnTextObj.AddComponent<RectTransform>();
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.sizeDelta = Vector2.zero;
        
        Text btnText = btnTextObj.AddComponent<Text>();
        btnText.text = text;
        btnText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        btnText.fontSize = 16;
        btnText.fontStyle = FontStyle.Bold;
        btnText.color = Color.white;
        btnText.alignment = TextAnchor.MiddleCenter;
        
        return btnObj;
    }

    // === DATA METHODS ===

    void PopulateFormFromAPI(BuildingData buildingData)
    {
        if (buildingData == null)
        {
            Debug.LogError("❌ No building data provided");
            return;
        }

        Debug.Log($"📋 PopulateFormFromAPI called for: {currentGmlId}");
        Debug.Log($"📋 energyManager is {(energyManager != null ? "VALID" : "NULL")}");
        
        // IMPORTANT: Always use THIS monobehaviour to start coroutines
        // This ensures proper context and lifecycle
        StartCoroutine(FetchAndPopulateForm());
    }
    
    IEnumerator FetchAndPopulateForm()
    {  
        Debug.Log($"📡 === FetchAndPopulateForm Started ===");
        Debug.Log($"📡 currentGmlIdBasic = '{currentGmlIdBasic}'");
        Debug.Log($"📡 energyManager = {energyManager}");
        
        if (energyManager == null)
        {
            Debug.LogError("❌ energyManager is NULL! Cannot fetch attributes.");
            yield break;
        }
        
        if (string.IsNullOrEmpty(currentGmlIdBasic))
        {
            Debug.LogError("❌ currentGmlIdBasic is empty! Cannot fetch attributes.");
            yield break;
        }
        
        bool dataReceived = false;
        JObject formData = null;
        string errorMessage = null;
        
        Debug.Log($"📡 Calling energyManager.FetchBasicAttributes('{currentGmlIdBasic}')...");
        
        yield return energyManager.FetchBasicAttributes(
            currentGmlIdBasic,
            (data) => {
                Debug.Log($"✅ SUCCESS callback received! Data keys: {string.Join(", ", data.Properties().Select(p => p.Name))}");
                formData = data;
                dataReceived = true;
            },
            (error) => {
                Debug.LogError($"❌ ERROR callback received: {error}");
                errorMessage = error;
                dataReceived = true;
            }
        );
        
        Debug.Log($"📡 After yield - dataReceived={dataReceived}, formData={(formData != null ? "NOT NULL" : "NULL")}");
        
        if (!dataReceived)
        {
            Debug.LogError("❌ Failed to fetch form data - no callback received");
            yield break;
        }
        
        if (formData == null)
        {
            Debug.LogError($"❌ API Error: {errorMessage}");
            yield break;
        }
        
        Debug.Log($"✅ Calling PopulateFormFromJsonData...");
        // Now populate the form with the fetched data
        PopulateFormFromJsonData(formData);
        Debug.Log($"✅ === FetchAndPopulateForm Completed ===");
    }
    
    void PopulateFormFromJsonData(JObject jsonData)
    {
        if (jsonData == null)
        {
            Debug.LogError("❌ No JSON data provided");
            return;
        }

        try
        {
            Debug.Log($"📝 === PopulateFormFromJsonData START ===");
            Debug.Log($"📝 JSON keys: {string.Join(", ", jsonData.Properties().Select(p => p.Name))}");
            
            titleText.text = $"Building: {currentGmlId}";
            
            originalApiData = jsonData; // Store for sending back
            Debug.Log($"📝 originalApiData stored: {(originalApiData != null ? "YES" : "NO")}");
            
            // Process field definitions with choices (like UE version)
            Debug.Log($"📝 Calling ProcessFieldDefinitions...");
            ProcessFieldDefinitions(jsonData);
            Debug.Log($"✅ ProcessFieldDefinitions completed");
            
            // Populate form values from API data
            Debug.Log($"📝 Calling PopulateFormValues...");
            PopulateFormValues(jsonData);
            Debug.Log($"✅ PopulateFormValues completed");
            
            Debug.Log("✅ Form populated with API data");
            Debug.Log($"📝 === PopulateFormFromJsonData COMPLETE ===");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error parsing API data: {e.Message}\n{e.StackTrace}");
        }
    }
    
    /// <summary>
    /// Process field definitions from API and build choice mappings
    /// This follows the UE BuildingAttributesWidget.cpp pattern
    /// </summary>
    void ProcessFieldDefinitions(JObject jsonData)
    {
        Debug.Log("📋 Processing field definitions and building choice mappings...");
        
        // Process general_info section
        if (jsonData["general_info"] != null)
        {
            Debug.Log("📋 Processing general_info section...");
            JObject generalInfo = jsonData["general_info"] as JObject;
            if (generalInfo["fields"] != null)
            {
                JObject fields = generalInfo["fields"] as JObject;
                foreach (var fieldPair in fields)
                {
                    JObject fieldDef = fieldPair.Value as JObject;
                    if (fieldDef != null && fieldDef["choices"] != null)
                    {
                        JArray choices = fieldDef["choices"] as JArray;
                        ProcessChoicesForField("general_info", fieldPair.Key, choices);
                    }
                }
            }
        }
        
        // Process begin_of_project section (Before Renovation)
        if (jsonData["begin_of_project"] != null)
        {
            Debug.Log("📋 Processing begin_of_project section...");
            JObject beginProject = jsonData["begin_of_project"] as JObject;
            if (beginProject["fields"] != null)
            {
                JObject fields = beginProject["fields"] as JObject;
                foreach (var fieldPair in fields)
                {
                    JObject fieldDef = fieldPair.Value as JObject;
                    if (fieldDef != null && fieldDef["choices"] != null)
                    {
                        JArray choices = fieldDef["choices"] as JArray;
                        ProcessChoicesForField("begin_of_project", fieldPair.Key, choices);
                    }
                }
            }
        }
        
        // Process end_of_project section (After Renovation)
        if (jsonData["end_of_project"] != null)
        {
            Debug.Log("📋 Processing end_of_project section...");
            JObject endProject = jsonData["end_of_project"] as JObject;
            if (endProject["fields"] != null)
            {
                JObject fields = endProject["fields"] as JObject;
                foreach (var fieldPair in fields)
                {
                    JObject fieldDef = fieldPair.Value as JObject;
                    if (fieldDef != null && fieldDef["choices"] != null)
                    {
                        JArray choices = fieldDef["choices"] as JArray;
                        ProcessChoicesForField("end_of_project", fieldPair.Key, choices);
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Process choices array for a specific field
    /// Choices format: [[code, label], [code, label], ...]
    /// </summary>
    void ProcessChoicesForField(string section, string fieldName, JArray choices)
    {
        Debug.Log($"📋 Processing {choices.Count} choices for [{section}].{fieldName}");
        
        // Determine which dropdown and mapping to use
        Dropdown targetDropdown = null;
        Dictionary<string, string> choiceMap = null;
        Dictionary<string, string> reverseMap = null;
        
        // === GENERAL INFO FIELDS ===
        if (section == "general_info")
        {
            if (fieldName == "construction_year_class")
            {
                targetDropdown = constructionYearDropdown;
                choiceMap = constructionYearChoiceMap;
                reverseMap = constructionYearReverseMap;
            }
            else if (fieldName == "roof_storey")
            {
                targetDropdown = roofStoryDropdown;
                choiceMap = roofStoreyChoiceMap;
                reverseMap = roofStoreyReverseMap;
            }
        }
        // === BEGIN OF PROJECT (BEFORE RENOVATION) ===
        else if (section == "begin_of_project")
        {
            // Fields with begin_ prefix
            if (fieldName == "begin_heating_system_type_1")
            {
                targetDropdown = heatingSystemBeforeDropdown;
                choiceMap = heatingSystemChoiceMap;
                reverseMap = heatingSystemReverseMap;
            }
            else if (fieldName == "begin_renovated_window_class_year" || fieldName == "window_type_1")
            {
                targetDropdown = windowYearBeforeDropdown;
                choiceMap = constructionYearChoiceMap;
                reverseMap = constructionYearReverseMap;
            }
            else if (fieldName == "begin_renovated_wall_class_year" || fieldName == "wall_type_1")
            {
                targetDropdown = wallYearBeforeDropdown;
                choiceMap = constructionYearChoiceMap;
                reverseMap = constructionYearReverseMap;
            }
            else if (fieldName == "begin_renovated_roof_class_year" || fieldName == "roof_type_1")
            {
                targetDropdown = roofYearBeforeDropdown;
                choiceMap = constructionYearChoiceMap;
                reverseMap = constructionYearReverseMap;
            }
            else if (fieldName == "begin_renovated_ceiling_class_year" || fieldName == "ceiling_type_1")
            {
                targetDropdown = ceilingYearBeforeDropdown;
                choiceMap = constructionYearChoiceMap;
                reverseMap = constructionYearReverseMap;
            }
        }
        // === END OF PROJECT (AFTER RENOVATION) ===
        else if (section == "end_of_project")
        {
            // Fields with end_ prefix
            if (fieldName == "end_heating_system_type_1")
            {
                targetDropdown = heatingSystemAfterDropdown;
                choiceMap = heatingSystemChoiceMap;
                reverseMap = heatingSystemReverseMap;
            }
            else if (fieldName == "end_renovated_window_class_year" || fieldName == "window_type_1")
            {
                targetDropdown = windowYearAfterDropdown;
                choiceMap = constructionYearChoiceMap;
                reverseMap = constructionYearReverseMap;
            }
            else if (fieldName == "end_renovated_wall_class_year" || fieldName == "wall_type_1")
            {
                targetDropdown = wallYearAfterDropdown;
                choiceMap = constructionYearChoiceMap;
                reverseMap = constructionYearReverseMap;
            }
            else if (fieldName == "end_renovated_roof_class_year" || fieldName == "roof_type_1")
            {
                targetDropdown = roofYearAfterDropdown;
                choiceMap = constructionYearChoiceMap;
                reverseMap = constructionYearReverseMap;
            }
            else if (fieldName == "end_renovated_ceiling_class_year" || fieldName == "ceiling_type_1")
            {
                targetDropdown = ceilingYearAfterDropdown;
                choiceMap = constructionYearChoiceMap;
                reverseMap = constructionYearReverseMap;
            }
        }
        
        if (targetDropdown == null || choiceMap == null)
        {
            Debug.LogWarning($"⚠️ No dropdown mapping for [{section}].{fieldName}");
            return;
        }
        
        // Clear existing options and mappings
        targetDropdown.ClearOptions();
        choiceMap.Clear();
        reverseMap.Clear();
        
        List<string> displayLabels = new List<string>();
        
        // Process each choice: [code, label]
        foreach (JToken choiceToken in choices)
        {
            JArray choiceArray = choiceToken as JArray;
            if (choiceArray != null && choiceArray.Count >= 2)
            {
                string code = choiceArray[0].ToString();
                string label = choiceArray[1].ToString();
                
                // Clean the label - remove code prefix (e.g., "A - Until 1859" -> "Until 1859")
                // Handles: "A - text", "B- text", "AB - text", "1 - text"
                string displayLabel = label;
                
                // Strip any short alphanumeric prefix before first "-" separator
                int dashIndex = label.IndexOf('-');
                if (dashIndex > 0 && dashIndex <= 3)
                {
                    string prefix = label.Substring(0, dashIndex).Trim();
                    bool isCode = prefix.Length <= 3 && System.Text.RegularExpressions.Regex.IsMatch(prefix, @"^[A-Za-z0-9]+$");
                    if (isCode)
                    {
                        displayLabel = label.Substring(dashIndex + 1).Trim();
                    }
                }
                
                displayLabels.Add(displayLabel);
                choiceMap[displayLabel] = code;  // Display -> API code
                reverseMap[code] = displayLabel;  // API code -> Display
                
                Debug.Log($"   ✓ Choice: '{displayLabel}' -> API code: '{code}'");
            }
        }
        
        // Populate dropdown with display labels
        targetDropdown.AddOptions(displayLabels);
        Debug.Log($"✅ Populated {targetDropdown.name} with {displayLabels.Count} options from [{section}].{fieldName}");
        
        // Force refresh the dropdown
        targetDropdown.RefreshShownValue();
    }
    
    /// <summary>
    /// Extract field value from field definition (extract the API code, not display label)
    /// </summary>
    string ExtractFieldValue(JObject fieldDef)
    {
        if (fieldDef == null) return "";
        
        // Get the API code value (NOT the display label!)
        // The "value" field is the unique API code we need to look up in reverseMap
        if (fieldDef["value"] != null && !string.IsNullOrEmpty(fieldDef["value"].ToString()))
        {
            return fieldDef["value"].ToString();
        }
        
        // Display is just the human-readable label, not useful for lookup
        if (fieldDef["display"] != null)
        {
            Debug.LogWarning("⚠️ Using display value as fallback (not ideal)");
            return fieldDef["display"].ToString();
        }
        
        return "";
    }
    
    /// <summary>
    /// Populate form values from API data after choices are loaded
    /// </summary>
    void PopulateFormValues(JObject jsonData)
    {
        Debug.Log("📝 === PopulateFormValues START ===");
        
        // Process general_info section
        if (jsonData["general_info"] != null)
        {
            Debug.Log("📝 Processing general_info section...");
            JObject generalInfo = jsonData["general_info"] as JObject;
            if (generalInfo["fields"] != null)
            {
                JObject fields = generalInfo["fields"] as JObject;
                Debug.Log($"   general_info has {fields.Count} fields");
                
                // Construction year
                if (fields["construction_year_class"] != null)
                {
                    string value = ExtractFieldValue(fields["construction_year_class"] as JObject);
                    Debug.Log($"   construction_year_class value: '{value}'");
                    SetDropdownValue(constructionYearDropdown, value, constructionYearReverseMap);
                }
                
                // Number of storeys
                if (fields["storey"] != null)
                {
                    string value = ExtractFieldValue(fields["storey"] as JObject);
                    Debug.Log($"   storey value: '{value}'");
                    numberOfStoreysInput.text = value;
                }
                
                // Roof storey
                if (fields["roof_storey"] != null)
                {
                    string value = ExtractFieldValue(fields["roof_storey"] as JObject);
                    Debug.Log($"   roof_storey value: '{value}'");
                    SetDropdownValue(roofStoryDropdown, value, roofStoreyReverseMap);
                }
            }
        }
        
        // Process begin_of_project section
        if (jsonData["begin_of_project"] != null)
        {
            Debug.Log("📝 Processing begin_of_project section...");
            JObject beginProject = jsonData["begin_of_project"] as JObject;
            if (beginProject["fields"] != null)
            {
                JObject fields = beginProject["fields"] as JObject;
                Debug.Log($"   begin_of_project has {fields.Count} fields");
                
                // Heating system - try both old and new field names
                string value = null;
                if (fields["begin_heating_system_type_1"] != null)
                {
                    value = ExtractFieldValue(fields["begin_heating_system_type_1"] as JObject);
                    Debug.Log($"   begin_heating_system_type_1 value: '{value}'");
                }
                else if (fields["heating_system_type_1"] != null)
                {
                    value = ExtractFieldValue(fields["heating_system_type_1"] as JObject);
                    Debug.Log($"   heating_system_type_1 value: '{value}'");
                }
                if (!string.IsNullOrEmpty(value)) SetDropdownValue(heatingSystemBeforeDropdown, value, heatingSystemReverseMap);
                
                // Window type
                value = null;
                if (fields["begin_renovated_window_class_year"] != null)
                {
                    value = ExtractFieldValue(fields["begin_renovated_window_class_year"] as JObject);
                    Debug.Log($"   begin_renovated_window_class_year value: '{value}'");
                }
                else if (fields["window_type_1"] != null)
                {
                    value = ExtractFieldValue(fields["window_type_1"] as JObject);
                    Debug.Log($"   window_type_1 value: '{value}'");
                }
                if (!string.IsNullOrEmpty(value)) SetDropdownValue(windowYearBeforeDropdown, value, constructionYearReverseMap);
                
                // Wall type
                value = null;
                if (fields["begin_renovated_wall_class_year"] != null)
                {
                    value = ExtractFieldValue(fields["begin_renovated_wall_class_year"] as JObject);
                    Debug.Log($"   begin_renovated_wall_class_year value: '{value}'");
                }
                else if (fields["wall_type_1"] != null)
                {
                    value = ExtractFieldValue(fields["wall_type_1"] as JObject);
                    Debug.Log($"   wall_type_1 value: '{value}'");
                }
                if (!string.IsNullOrEmpty(value)) SetDropdownValue(wallYearBeforeDropdown, value, constructionYearReverseMap);
                
                // Roof type
                value = null;
                if (fields["begin_renovated_roof_class_year"] != null)
                {
                    value = ExtractFieldValue(fields["begin_renovated_roof_class_year"] as JObject);
                    Debug.Log($"   begin_renovated_roof_class_year value: '{value}'");
                }
                else if (fields["roof_type_1"] != null)
                {
                    value = ExtractFieldValue(fields["roof_type_1"] as JObject);
                    Debug.Log($"   roof_type_1 value: '{value}'");
                }
                if (!string.IsNullOrEmpty(value)) SetDropdownValue(roofYearBeforeDropdown, value, constructionYearReverseMap);
                
                // Ceiling type
                value = null;
                if (fields["begin_renovated_ceiling_class_year"] != null)
                {
                    value = ExtractFieldValue(fields["begin_renovated_ceiling_class_year"] as JObject);
                    Debug.Log($"   begin_renovated_ceiling_class_year value: '{value}'");
                }
                else if (fields["ceiling_type_1"] != null)
                {
                    value = ExtractFieldValue(fields["ceiling_type_1"] as JObject);
                    Debug.Log($"   ceiling_type_1 value: '{value}'");
                }
                if (!string.IsNullOrEmpty(value)) SetDropdownValue(ceilingYearBeforeDropdown, value, constructionYearReverseMap);
            }
        }
        
        // Process end_of_project section
        if (jsonData["end_of_project"] != null)
        {
            Debug.Log("📝 Processing end_of_project section...");
            JObject endProject = jsonData["end_of_project"] as JObject;
            if (endProject["fields"] != null)
            {
                JObject fields = endProject["fields"] as JObject;
                Debug.Log($"   end_of_project has {fields.Count} fields");
                
                // Heating system
                string value = null;
                if (fields["end_heating_system_type_1"] != null)
                {
                    value = ExtractFieldValue(fields["end_heating_system_type_1"] as JObject);
                    Debug.Log($"   end_heating_system_type_1 value: '{value}'");
                }
                else if (fields["heating_system_type_1"] != null)
                {
                    value = ExtractFieldValue(fields["heating_system_type_1"] as JObject);
                    Debug.Log($"   heating_system_type_1 value: '{value}'");
                }
                if (!string.IsNullOrEmpty(value)) SetDropdownValue(heatingSystemAfterDropdown, value, heatingSystemReverseMap);
                
                // Window type
                value = null;
                if (fields["end_renovated_window_class_year"] != null)
                {
                    value = ExtractFieldValue(fields["end_renovated_window_class_year"] as JObject);
                    Debug.Log($"   end_renovated_window_class_year value: '{value}'");
                }
                else if (fields["window_type_1"] != null)
                {
                    value = ExtractFieldValue(fields["window_type_1"] as JObject);
                    Debug.Log($"   window_type_1 value: '{value}'");
                }
                if (!string.IsNullOrEmpty(value)) SetDropdownValue(windowYearAfterDropdown, value, constructionYearReverseMap);
                
                // Wall type
                value = null;
                if (fields["end_renovated_wall_class_year"] != null)
                {
                    value = ExtractFieldValue(fields["end_renovated_wall_class_year"] as JObject);
                    Debug.Log($"   end_renovated_wall_class_year value: '{value}'");
                }
                else if (fields["wall_type_1"] != null)
                {
                    value = ExtractFieldValue(fields["wall_type_1"] as JObject);
                    Debug.Log($"   wall_type_1 value: '{value}'");
                }
                if (!string.IsNullOrEmpty(value)) SetDropdownValue(wallYearAfterDropdown, value, constructionYearReverseMap);
                
                // Roof type
                value = null;
                if (fields["end_renovated_roof_class_year"] != null)
                {
                    value = ExtractFieldValue(fields["end_renovated_roof_class_year"] as JObject);
                    Debug.Log($"   end_renovated_roof_class_year value: '{value}'");
                }
                else if (fields["roof_type_1"] != null)
                {
                    value = ExtractFieldValue(fields["roof_type_1"] as JObject);
                    Debug.Log($"   roof_type_1 value: '{value}'");
                }
                if (!string.IsNullOrEmpty(value)) SetDropdownValue(roofYearAfterDropdown, value, constructionYearReverseMap);
                
                // Ceiling type
                value = null;
                if (fields["end_renovated_ceiling_class_year"] != null)
                {
                    value = ExtractFieldValue(fields["end_renovated_ceiling_class_year"] as JObject);
                    Debug.Log($"   end_renovated_ceiling_class_year value: '{value}'");
                }
                else if (fields["ceiling_type_1"] != null)
                {
                    value = ExtractFieldValue(fields["ceiling_type_1"] as JObject);
                    Debug.Log($"   ceiling_type_1 value: '{value}'");
                }
                if (!string.IsNullOrEmpty(value)) SetDropdownValue(ceilingYearAfterDropdown, value, constructionYearReverseMap);
            }
        }
        
        Debug.Log("📝 === PopulateFormValues COMPLETE ===");
    }
    
    /// <summary>
    /// Set dropdown value using reverse map (API code -> Display label)
    /// </summary>
    void SetDropdownValue(Dropdown dropdown, string apiValue, Dictionary<string, string> reverseMap)
    {
        if (dropdown == null)
        {
            Debug.LogWarning($"⚠️ Dropdown is null, cannot set value");
            return;
        }
        
        if (string.IsNullOrEmpty(apiValue))
        {
            Debug.LogWarning($"⚠️ API value is empty for {dropdown.name}");
            return;
        }
        
        Debug.Log($"📍 SetDropdownValue: {dropdown.name} = '{apiValue}'");
        Debug.Log($"   Dropdown has {dropdown.options.Count} options");
        Debug.Log($"   ReverseMap has {reverseMap.Count} entries");
        
        // Try to find display label for API code
        string displayLabel;
        if (reverseMap.TryGetValue(apiValue, out displayLabel))
        {
            Debug.Log($"   ReverseMap hit: '{apiValue}' -> '{displayLabel}'");
            
            // Find option index
            int index = dropdown.options.FindIndex(opt => opt.text == displayLabel);
            if (index >= 0)
            {
                dropdown.value = index;
                Debug.Log($"✓ Set {dropdown.name} to index {index} ('{displayLabel}')");
                return;
            }
            else
            {
                Debug.LogWarning($"   Option '{displayLabel}' not found in dropdown");
            }
        }
        else
        {
            Debug.LogWarning($"   ReverseMap MISS: '{apiValue}' not in map");
        }
        
        // Fallback: try direct value match
        int directIndex = dropdown.options.FindIndex(opt => opt.text == apiValue);
        if (directIndex >= 0)
        {
            dropdown.value = directIndex;
            Debug.Log($"✓ Set {dropdown.name} to index {directIndex} (direct match: '{apiValue}')");
        }
        else
        {
            Debug.LogWarning($"⚠️ Could not find option for value: '{apiValue}' in {dropdown.name}");
        }
    }

    void SaveBuildingInformation()
    {
        Debug.Log("💾 SaveBuildingInformation called");
        
        // Always use THIS monobehaviour to start coroutines
        StartCoroutine(SendPutRequest());
    }

    IEnumerator SendPutRequest()
    {
        // Use same API configuration as BuildingEnergyManager
        string apiBaseUrl = energyManager != null ? energyManager.apiBaseUrl : "https://backend.gisworld-tech.com";
        string accessToken = energyManager != null ? energyManager.accessToken : "";
        string community = energyManager != null ? energyManager.communityId : communityId;
        
        // Construct URL with required field_type=basic parameter
        string url = $"{apiBaseUrl}/geospatial/buildings-energy/{UnityWebRequest.EscapeURL(currentGmlIdBasic)}/?community_id={community}&field_type=basic";
        string jsonPayload = BuildJsonPayload();
        
        Debug.Log($"💾 PUT URL: {url}");
        Debug.Log($"💾 Payload: {jsonPayload}");
        
        if (string.IsNullOrEmpty(accessToken))
        {
            Debug.LogError("❌ No access token available for PUT request");
            yield break;
        }

        using (UnityWebRequest request = new UnityWebRequest(url, "PUT"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("✅ Building data updated successfully!");
                
                // Close form (cache will be updated on next building click)
                CloseForm();
            }
            else
            {
                Debug.LogError($"❌ PUT request failed: {request.error}");
                Debug.LogError($"Response: {request.downloadHandler.text}");
            }
        }
    }

    string BuildJsonPayload()
    {
        Debug.Log("💾 Building JSON payload - extracting from original API structure...");
        Debug.Log($"💾 originalApiData is {(originalApiData != null ? "NOT NULL" : "NULL")}");
        
        if (originalApiData == null)
        {
            Debug.LogError("❌ originalApiData is null - cannot build payload");
            return "{}";
        }
        
        Debug.Log($"💾 Original API structure:\n{originalApiData.ToString()}");
        
        // Build flat payload with all required fields
        JObject payload = new JObject();
        
        // Helper function to get API code from dropdown
        string GetApiCode(Dropdown dropdown, Dictionary<string, string> choiceMap)
        {
            if (dropdown == null || dropdown.value < 0 || dropdown.value >= dropdown.options.Count)
                return null;
            
            string displayLabel = dropdown.options[dropdown.value].text;
            string apiCode;
            if (choiceMap.TryGetValue(displayLabel, out apiCode))
            {
                Debug.Log($"   Mapping '{displayLabel}' -> '{apiCode}'");
                return apiCode;
            }
            
            Debug.LogWarning($"   No API code mapping for '{displayLabel}', using as-is");
            return displayLabel;
        }
        
        // Helper function to extract value from original nested structure
        string GetOriginalValue(string section, string fieldName)
        {
            try
            {
                Debug.Log($"   📍 GetOriginalValue: Looking for {section}/{fieldName}");
                
                if (originalApiData[section] == null)
                {
                    Debug.LogWarning($"   ❌ Section '{section}' not found in original data");
                    Debug.Log($"   Available sections: {string.Join(", ", originalApiData.Properties().Select(p => p.Name))}");
                    return "";
                }
                
                JObject sectionObj = originalApiData[section] as JObject;
                if (sectionObj == null)
                {
                    Debug.LogWarning($"   ❌ Section '{section}' is not JObject");
                    return "";
                }
                
                if (sectionObj["fields"] == null)
                {
                    Debug.LogWarning($"   ❌ 'fields' not found in section '{section}'");
                    Debug.Log($"   Available in section: {string.Join(", ", sectionObj.Properties().Select(p => p.Name))}");
                    return "";
                }
                
                JObject fields = sectionObj["fields"] as JObject;
                if (fields == null)
                {
                    Debug.LogWarning($"   ❌ 'fields' in section '{section}' is not JObject");
                    return "";
                }
                
                if (fields[fieldName] == null)
                {
                    Debug.LogWarning($"   ❌ Field '{fieldName}' not found in section '{section}'");
                    Debug.Log($"   Available fields in {section}: {string.Join(", ", fields.Properties().Select(p => p.Name))}");
                    return "";
                }
                
                JObject fieldDef = fields[fieldName] as JObject;
                if (fieldDef == null)
                {
                    Debug.LogWarning($"   ❌ Field '{fieldName}' in section '{section}' is not JObject");
                    return "";
                }
                
                if (fieldDef["value"] != null && fieldDef["value"].Type != JTokenType.Null)
                {
                    string value = fieldDef["value"].ToString();
                    Debug.Log($"   ✓ Found '{fieldName}' in '{section}': '{value}'");
                    return value;
                }
                
                Debug.LogWarning($"   ❌ 'value' is null or missing for field '{fieldName}' in section '{section}'");
                return "";
            }
            catch (System.Exception e)
            {
                Debug.LogError($"   ❌ Error getting original value for {section}/{fieldName}: {e.Message}\n{e.StackTrace}");
                return "";
            }
        }
        
        // === GENERAL INFO FIELDS ===
        
        // Construction year class
        string constructionYearValue = GetApiCode(constructionYearDropdown, constructionYearChoiceMap);
        if (string.IsNullOrEmpty(constructionYearValue))
        {
            constructionYearValue = GetOriginalValue("general_info", "construction_year_class");
        }
        if (!string.IsNullOrEmpty(constructionYearValue))
        {
            payload["construction_year_class"] = constructionYearValue;
            Debug.Log($"✓ construction_year_class = '{constructionYearValue}'");
        }
        
        // Storey (Number of Storeys)
        string storeyValue = numberOfStoreysInput.text;
        if (string.IsNullOrEmpty(storeyValue))
        {
            storeyValue = GetOriginalValue("general_info", "storey");
        }
        if (!string.IsNullOrEmpty(storeyValue))
        {
            payload["storey"] = storeyValue;
            Debug.Log($"✓ storey = '{storeyValue}'");
        }
        
        // Roof storey
        string roofStoreyValue = GetApiCode(roofStoryDropdown, roofStoreyChoiceMap);
        if (string.IsNullOrEmpty(roofStoreyValue))
        {
            roofStoreyValue = GetOriginalValue("general_info", "roof_storey");
        }
        if (!string.IsNullOrEmpty(roofStoreyValue))
        {
            payload["roof_storey"] = roofStoreyValue;
            Debug.Log($"✓ roof_storey = '{roofStoreyValue}'");
        }
        
        // === BEGIN OF PROJECT (BEFORE RENOVATION) ===
        
        // Heating system - REQUIRED field
        string heatingSystemBeforeValue = GetApiCode(heatingSystemBeforeDropdown, heatingSystemChoiceMap);
        if (string.IsNullOrEmpty(heatingSystemBeforeValue))
        {
            heatingSystemBeforeValue = GetOriginalValue("begin_of_project", "heating_system_type_1");
        }
        // ALWAYS include this field - it's required!
        if (!string.IsNullOrEmpty(heatingSystemBeforeValue))
        {
            payload["begin_heating_system_type_1"] = heatingSystemBeforeValue;
            Debug.Log($"✓ begin_heating_system_type_1 = '{heatingSystemBeforeValue}'");
        }
        else
        {
            Debug.LogWarning("⚠️ WARNING: begin_heating_system_type_1 is empty - API will reject this!");
            Debug.LogWarning($"   Dropdown value: {heatingSystemBeforeDropdown?.value}, Options: {heatingSystemBeforeDropdown?.options.Count}");
        }
        
        // Window type
        string windowBeforeValue = GetApiCode(windowYearBeforeDropdown, constructionYearChoiceMap);
        if (string.IsNullOrEmpty(windowBeforeValue))
        {
            windowBeforeValue = GetOriginalValue("begin_of_project", "begin_renovated_window_class_year");
        }
        if (!string.IsNullOrEmpty(windowBeforeValue))
        {
            payload["begin_renovated_window_class_year"] = windowBeforeValue;
            Debug.Log($"✓ begin_renovated_window_class_year = '{windowBeforeValue}'");
        }
        
        // Wall type
        string wallBeforeValue = GetApiCode(wallYearBeforeDropdown, constructionYearChoiceMap);
        if (string.IsNullOrEmpty(wallBeforeValue))
        {
            wallBeforeValue = GetOriginalValue("begin_of_project", "begin_renovated_wall_class_year");
        }
        if (!string.IsNullOrEmpty(wallBeforeValue))
        {
            payload["begin_renovated_wall_class_year"] = wallBeforeValue;
            Debug.Log($"✓ begin_renovated_wall_class_year = '{wallBeforeValue}'");
        }
        
        // Roof type
        string roofBeforeValue = GetApiCode(roofYearBeforeDropdown, constructionYearChoiceMap);
        if (string.IsNullOrEmpty(roofBeforeValue))
        {
            roofBeforeValue = GetOriginalValue("begin_of_project", "begin_renovated_roof_class_year");
        }
        if (!string.IsNullOrEmpty(roofBeforeValue))
        {
            payload["begin_renovated_roof_class_year"] = roofBeforeValue;
            Debug.Log($"✓ begin_renovated_roof_class_year = '{roofBeforeValue}'");
        }
        
        // Ceiling type
        string ceilingBeforeValue = GetApiCode(ceilingYearBeforeDropdown, constructionYearChoiceMap);
        if (string.IsNullOrEmpty(ceilingBeforeValue))
        {
            ceilingBeforeValue = GetOriginalValue("begin_of_project", "begin_renovated_ceiling_class_year");
        }
        if (!string.IsNullOrEmpty(ceilingBeforeValue))
        {
            payload["begin_renovated_ceiling_class_year"] = ceilingBeforeValue;
            Debug.Log($"✓ begin_renovated_ceiling_class_year = '{ceilingBeforeValue}'");
        }
        
        // === END OF PROJECT (AFTER RENOVATION) ===
        
        // Heating system
        string heatingSystemAfterValue = GetApiCode(heatingSystemAfterDropdown, heatingSystemChoiceMap);
        if (string.IsNullOrEmpty(heatingSystemAfterValue))
        {
            heatingSystemAfterValue = GetOriginalValue("end_of_project", "heating_system_type_1");
        }
        if (!string.IsNullOrEmpty(heatingSystemAfterValue))
        {
            payload["end_heating_system_type_1"] = heatingSystemAfterValue;
            Debug.Log($"✓ end_heating_system_type_1 = '{heatingSystemAfterValue}'");
        }
        
        // Window type
        string windowAfterValue = GetApiCode(windowYearAfterDropdown, constructionYearChoiceMap);
        if (string.IsNullOrEmpty(windowAfterValue))
        {
            windowAfterValue = GetOriginalValue("end_of_project", "end_renovated_window_class_year");
        }
        if (!string.IsNullOrEmpty(windowAfterValue))
        {
            payload["end_renovated_window_class_year"] = windowAfterValue;
            Debug.Log($"✓ end_renovated_window_class_year = '{windowAfterValue}'");
        }
        
        // Wall type
        string wallAfterValue = GetApiCode(wallYearAfterDropdown, constructionYearChoiceMap);
        if (string.IsNullOrEmpty(wallAfterValue))
        {
            wallAfterValue = GetOriginalValue("end_of_project", "end_renovated_wall_class_year");
        }
        if (!string.IsNullOrEmpty(wallAfterValue))
        {
            payload["end_renovated_wall_class_year"] = wallAfterValue;
            Debug.Log($"✓ end_renovated_wall_class_year = '{wallAfterValue}'");
        }
        
        // Roof type
        string roofAfterValue = GetApiCode(roofYearAfterDropdown, constructionYearChoiceMap);
        if (string.IsNullOrEmpty(roofAfterValue))
        {
            roofAfterValue = GetOriginalValue("end_of_project", "end_renovated_roof_class_year");
        }
        if (!string.IsNullOrEmpty(roofAfterValue))
        {
            payload["end_renovated_roof_class_year"] = roofAfterValue;
            Debug.Log($"✓ end_renovated_roof_class_year = '{roofAfterValue}'");
        }
        
        // Ceiling type
        string ceilingAfterValue = GetApiCode(ceilingYearAfterDropdown, constructionYearChoiceMap);
        if (string.IsNullOrEmpty(ceilingAfterValue))
        {
            ceilingAfterValue = GetOriginalValue("end_of_project", "end_renovated_ceiling_class_year");
        }
        if (!string.IsNullOrEmpty(ceilingAfterValue))
        {
            payload["end_renovated_ceiling_class_year"] = ceilingAfterValue;
            Debug.Log($"✓ end_renovated_ceiling_class_year = '{ceilingAfterValue}'");
        }
        
        string jsonString = payload.ToString();
        Debug.Log($"📦 Final Flat Payload:\n{jsonString}");
        
        return jsonString;
    }

    void CloseForm()
    {
        Debug.Log("🚪 CloseForm() called");
        
        if (modalBlocker != null)
        {
            modalBlocker.SetActive(false);
            Debug.Log("   ✓ Modal blocker hidden");
        }
        else
        {
            Debug.LogWarning("   ⚠️ modalBlocker is null!");
        }
        
        if (formPanel != null)
        {
            formPanel.SetActive(false);
            Debug.Log("   ✓ Form panel hidden");
        }
        else
        {
            Debug.LogWarning("   ⚠️ formPanel is null!");
        }
            
        Debug.Log("✅ Form closed - map interaction restored");
    }

    void DestroyForm()
    {
        if (modalBlocker != null)
        {
            Destroy(modalBlocker);
            modalBlocker = null;
        }
        if (formPanel != null)
        {
            Destroy(formPanel);
            formPanel = null;
        }
        
        Debug.Log("✅ Form destroyed");
    }

    void OnDestroy()
    {
        StopAllCoroutines();
        DestroyForm();
    }
}

/// <summary>
/// Blocks scroll events from passing through UI to the scene
/// Implements multiple interfaces to catch all input events
/// </summary>
public class ScrollBlocker : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public void OnScroll(PointerEventData eventData)
    {
        // Consume scroll event - prevents it from reaching map
        Debug.Log("ScrollBlocker: Blocking scroll event");
        eventData.Use();
    }
    
    public void OnBeginDrag(PointerEventData eventData)
    {
        // Block drag events
        eventData.Use();
    }
    
    public void OnDrag(PointerEventData eventData)
    {
        // Block drag events  
        eventData.Use();
    }
    
    public void OnEndDrag(PointerEventData eventData)
    {
        // Block drag events
        eventData.Use();
    }
}
