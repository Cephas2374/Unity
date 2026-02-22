using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Displays an Energy Demand class legend overlay matching the German Energieausweis
/// classification (kWh/m²a). Counts buildings per class from BuildingEnergyManager cache.
/// 
/// On HoloLens 2: WorldSpace canvas, follows user gaze (top-right).
/// On Desktop: ScreenSpaceOverlay, anchored top-right.
/// </summary>
public class EnergyDemandLegend : MonoBehaviour
{
    [Header("References")]
    public BuildingEnergyManager energyManager;

    [Header("Settings")]
    public bool isXRDevice = true;
    public float updateInterval = 5f;

    // German Energieausweis energy demand classes (kWh/m²a thresholds)
    // based on EnEV / GEG standard ranges
    private static readonly EnergyClass[] energyClasses = new EnergyClass[]
    {
        new EnergyClass("A+",    0,   30, new Color32( 0, 128,  0, 255)),  // dark green
        new EnergyClass("A",    30,   50, new Color32( 0, 176,  80, 255)), // green
        new EnergyClass("B",    50,   75, new Color32(146, 208,  80, 255)), // lime
        new EnergyClass("C",    75,  100, new Color32(255, 255,   0, 255)), // yellow
        new EnergyClass("D",   100,  130, new Color32(255, 192,   0, 255)), // amber
        new EnergyClass("E",   130,  160, new Color32(255, 128,   0, 255)), // orange
        new EnergyClass("F",   160,  200, new Color32(255,  64,   0, 255)), // red-orange
        new EnergyClass("G",   200,  250, new Color32(224,  32,  32, 255)), // red
        new EnergyClass("H",   250, 9999, new Color32(160,   0,   0, 255)), // dark red
    };

    private Camera mainCamera;
    private GameObject legendPanel;
    private Text[] countTexts;
    private Text titleText;
    private float updateTimer;
    private int totalBuildings;

    private struct EnergyClass
    {
        public string label;
        public int minKwh;
        public int maxKwh;
        public Color32 color;

        public EnergyClass(string label, int min, int max, Color32 color)
        {
            this.label = label;
            this.minKwh = min;
            this.maxKwh = max;
            this.color = color;
        }
    }

    void Start()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("[EnergyLegend] No Main Camera found!");
            enabled = false;
            return;
        }

        if (energyManager == null)
        {
            energyManager = FindObjectOfType<BuildingEnergyManager>();
            if (energyManager == null)
            {
                Debug.LogError("[EnergyLegend] No BuildingEnergyManager found!");
                enabled = false;
                return;
            }
        }

        CreateLegendUI();
        Debug.Log("<color=cyan>[EnergyLegend] Energy Demand legend created.</color>");
    }

    void LateUpdate()
    {
        if (legendPanel == null || mainCamera == null) return;

        // Keep legend positioned in user's view (HoloLens WorldSpace)
        if (isXRDevice)
        {
            PositionLegend();
        }

        // Periodic data refresh
        updateTimer += Time.deltaTime;
        if (updateTimer >= updateInterval)
        {
            updateTimer = 0f;
            RefreshCounts();
        }
    }

    // === UI CREATION ===

    void CreateLegendUI()
    {
        // Canvas
        GameObject canvasObj = new GameObject("EnergyLegendCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();

        if (isXRDevice)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 90;
            RectTransform cRect = canvasObj.GetComponent<RectTransform>();
            cRect.sizeDelta = new Vector2(320, 420);
            canvasObj.transform.localScale = Vector3.one * 0.0004f;
        }
        else
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
        }

        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        // Panel background
        legendPanel = new GameObject("LegendPanel");
        legendPanel.transform.SetParent(canvasObj.transform, false);

        Image bg = legendPanel.AddComponent<Image>();
        bg.color = new Color(0.06f, 0.06f, 0.06f, 0.88f);

        RectTransform panelRect = legendPanel.GetComponent<RectTransform>();
        if (isXRDevice)
        {
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(310, 400);
            panelRect.anchoredPosition = Vector2.zero;
        }
        else
        {
            panelRect.anchorMin = new Vector2(1, 1); // top-right
            panelRect.anchorMax = new Vector2(1, 1);
            panelRect.pivot = new Vector2(1, 1);
            panelRect.sizeDelta = new Vector2(260, 360);
            panelRect.anchoredPosition = new Vector2(-10, -10);
        }

        // Subtle border
        Outline border = legendPanel.AddComponent<Outline>();
        border.effectColor = new Color(0.3f, 0.3f, 0.3f, 0.6f);
        border.effectDistance = new Vector2(1, -1);

        // Title
        float yTop = isXRDevice ? 170f : 155f;
        GameObject titleObj = CreateChild("Title", legendPanel, new Vector2(280, 36));
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchoredPosition = new Vector2(0, yTop);
        titleText = titleObj.AddComponent<Text>();
        titleText.text = "Energy Demand Classes";
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize = isXRDevice ? 28 : 18;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = Color.white;
        titleText.fontStyle = FontStyle.Bold;

        // Subtitle with unit
        GameObject subObj = CreateChild("Subtitle", legendPanel, new Vector2(280, 24));
        RectTransform subRect = subObj.GetComponent<RectTransform>();
        subRect.anchoredPosition = new Vector2(0, yTop - 28f);
        Text subText = subObj.AddComponent<Text>();
        subText.text = "[kWh/m²a]";
        subText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        subText.fontSize = isXRDevice ? 20 : 13;
        subText.alignment = TextAnchor.MiddleCenter;
        subText.color = new Color(0.7f, 0.7f, 0.7f, 1f);

        // Class rows
        countTexts = new Text[energyClasses.Length];
        float rowStartY = yTop - 64f;
        float rowHeight = isXRDevice ? 34f : 28f;
        float swatchSize = isXRDevice ? 24f : 18f;

        for (int i = 0; i < energyClasses.Length; i++)
        {
            float y = rowStartY - i * rowHeight;
            var ec = energyClasses[i];

            // Color swatch
            GameObject swatch = CreateChild("Swatch_" + ec.label, legendPanel, new Vector2(swatchSize, swatchSize));
            RectTransform swR = swatch.GetComponent<RectTransform>();
            swR.anchoredPosition = new Vector2(isXRDevice ? -120f : -100f, y);
            Image swImg = swatch.AddComponent<Image>();
            swImg.color = ec.color;

            // Label: "A+ (0–30)"
            string rangeLabel = ec.maxKwh >= 9999
                ? $"{ec.label}  (>{ec.minKwh})"
                : $"{ec.label}  ({ec.minKwh}–{ec.maxKwh})";
            GameObject lblObj = CreateChild("Label_" + ec.label, legendPanel, new Vector2(160, rowHeight));
            RectTransform lblR = lblObj.GetComponent<RectTransform>();
            lblR.anchoredPosition = new Vector2(isXRDevice ? -20f : -5f, y);
            Text lblText = lblObj.AddComponent<Text>();
            lblText.text = rangeLabel;
            lblText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            lblText.fontSize = isXRDevice ? 22 : 14;
            lblText.alignment = TextAnchor.MiddleLeft;
            lblText.color = Color.white;

            // Count text (right-aligned)
            GameObject cntObj = CreateChild("Count_" + ec.label, legendPanel, new Vector2(70, rowHeight));
            RectTransform cntR = cntObj.GetComponent<RectTransform>();
            cntR.anchoredPosition = new Vector2(isXRDevice ? 115f : 95f, y);
            countTexts[i] = cntObj.AddComponent<Text>();
            countTexts[i].font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            countTexts[i].fontSize = isXRDevice ? 20 : 13;
            countTexts[i].alignment = TextAnchor.MiddleRight;
            countTexts[i].color = new Color(0.8f, 0.8f, 0.8f, 1f);
            countTexts[i].text = "";
        }

        // Initial counts
        RefreshCounts();
    }

    // === DATA ===

    void RefreshCounts()
    {
        if (energyManager == null || countTexts == null) return;

        // Count buildings per energy class using cached data
        int[] counts = new int[energyClasses.Length];
        int noData = 0;
        totalBuildings = 0;

        foreach (var kvp in energyManager.buildingDataCache)
        {
            totalBuildings++;
            int kwh = kvp.Value.energyDemandAfter;

            if (kwh <= 0)
            {
                noData++;
                continue;
            }

            for (int i = 0; i < energyClasses.Length; i++)
            {
                if (kwh >= energyClasses[i].minKwh && kwh < energyClasses[i].maxKwh)
                {
                    counts[i]++;
                    break;
                }
            }
        }

        // Update UI
        for (int i = 0; i < energyClasses.Length; i++)
        {
            if (totalBuildings > 0)
            {
                float pct = counts[i] * 100f / totalBuildings;
                countTexts[i].text = $"{pct:F1}% ({counts[i]})";
            }
            else
            {
                countTexts[i].text = "—";
            }
        }

        // Update title with total + noData
        if (totalBuildings > 0 && noData > 0)
        {
            float noDataPct = noData * 100f / totalBuildings;
            titleText.text = $"Energy Demand Classes\n<size={( isXRDevice ? 18 : 12)}><color=#999999>No data: {noDataPct:F1}% ({noData})</color></size>";
        }
        else
        {
            titleText.text = "Energy Demand Classes";
        }
    }

    // === POSITIONING (HoloLens WorldSpace) ===

    void PositionLegend()
    {
        if (legendPanel == null || mainCamera == null) return;

        Transform cam = mainCamera.transform;
        Vector3 forward = cam.forward;
        forward.y = 0;
        if (forward == Vector3.zero) forward = Vector3.forward;
        forward.Normalize();

        // Top-right of user's field of view
        Vector3 right = Vector3.Cross(Vector3.up, forward) * -1f;
        Vector3 pos = cam.position
            + forward * 0.65f
            + right * 0.28f
            + Vector3.up * 0.12f;

        Transform canvasT = legendPanel.transform.parent;
        canvasT.position = pos;
        canvasT.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    // === HELPERS ===

    GameObject CreateChild(string name, GameObject parent, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return obj;
    }

    /// <summary>Toggle legend visibility.</summary>
    public void ToggleLegend()
    {
        if (legendPanel != null)
        {
            legendPanel.SetActive(!legendPanel.activeSelf);
        }
    }

    public void Show() { if (legendPanel != null) legendPanel.SetActive(true); }
    public void Hide() { if (legendPanel != null) legendPanel.SetActive(false); }

    void OnDestroy()
    {
        if (legendPanel != null && legendPanel.transform.parent != null)
        {
            Destroy(legendPanel.transform.parent.gameObject);
        }
    }
}
