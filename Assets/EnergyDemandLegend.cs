using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Displays a DYNAMIC Energy Demand class legend overlay.
/// Colors are derived from the actual backend API colors stored in
/// BuildingEnergyManager.buildingColorCache — not hardcoded.
///
/// For each German Energieausweis class (A+ through H), the legend samples
/// the real API color from a building in that class, so the swatch always
/// matches what is painted on the 3D Tiles.
///
/// Uses energyDemandBefore (begin state = current condition) to classify
/// buildings, matching the begin.color.energy_demand_specific_color used
/// for vertex coloring.
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
    // Colors are populated dynamically from the API at runtime
    private static readonly EnergyClassDef[] energyClassDefs = new EnergyClassDef[]
    {
        new EnergyClassDef("A+",   0,   30),
        new EnergyClassDef("A",   30,   50),
        new EnergyClassDef("B",   50,   75),
        new EnergyClassDef("C",   75,  100),
        new EnergyClassDef("D",  100,  130),
        new EnergyClassDef("E",  130,  160),
        new EnergyClassDef("F",  160,  200),
        new EnergyClassDef("G",  200,  250),
        new EnergyClassDef("H",  250, 9999),
    };

    // Fallback colors (German EnEV gradient) used only if no building exists in a class
    private static readonly Color32[] fallbackColors = new Color32[]
    {
        new Color32(  0, 128,   0, 255), // A+ dark green
        new Color32(  0, 176,  80, 255), // A  green
        new Color32(146, 208,  80, 255), // B  lime
        new Color32(255, 255,   0, 255), // C  yellow
        new Color32(255, 192,   0, 255), // D  amber
        new Color32(255, 128,   0, 255), // E  orange
        new Color32(255,  64,   0, 255), // F  red-orange
        new Color32(224,  32,  32, 255), // G  red
        new Color32(160,   0,   0, 255), // H  dark red
    };

    private Camera mainCamera;
    private GameObject legendPanel;
    private Image[] swatchImages;        // Swatch images (updated dynamically)
    private Text[] countTexts;
    private Image noDataSwatch;           // "No data" row swatch
    private Text noDataCountText;         // "No data" row count
    private Text titleText;
    private float updateTimer;
    private int totalBuildings;
    private bool colorsResolved = false; // True once we have sampled API colors

    private struct EnergyClassDef
    {
        public string label;
        public int minKwh;
        public int maxKwh;

        public EnergyClassDef(string label, int min, int max)
        {
            this.label = label;
            this.minKwh = min;
            this.maxKwh = max;
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
            cRect.sizeDelta = new Vector2(320, 460);
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
            panelRect.sizeDelta = new Vector2(310, 440);
            panelRect.anchoredPosition = Vector2.zero;
        }
        else
        {
            panelRect.anchorMin = new Vector2(1, 1); // top-right
            panelRect.anchorMax = new Vector2(1, 1);
            panelRect.pivot = new Vector2(1, 1);
            panelRect.sizeDelta = new Vector2(260, 390);
            panelRect.anchoredPosition = new Vector2(-10, -10);
        }

        // Subtle border
        Outline border = legendPanel.AddComponent<Outline>();
        border.effectColor = new Color(0.3f, 0.3f, 0.3f, 0.6f);
        border.effectDistance = new Vector2(1, -1);

        // Title
        float yTop = isXRDevice ? 190f : 170f;
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
        int classCount = energyClassDefs.Length;
        swatchImages = new Image[classCount];
        countTexts = new Text[classCount];
        float rowStartY = yTop - 64f;
        float rowHeight = isXRDevice ? 34f : 28f;
        float swatchSize = isXRDevice ? 24f : 18f;

        for (int i = 0; i < classCount; i++)
        {
            float y = rowStartY - i * rowHeight;
            var ec = energyClassDefs[i];

            // Color swatch (initially fallback; updated dynamically when data arrives)
            GameObject swatch = CreateChild("Swatch_" + ec.label, legendPanel, new Vector2(swatchSize, swatchSize));
            RectTransform swR = swatch.GetComponent<RectTransform>();
            swR.anchoredPosition = new Vector2(isXRDevice ? -120f : -100f, y);
            swatchImages[i] = swatch.AddComponent<Image>();
            swatchImages[i].color = fallbackColors[i];

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

        // "No data" row below the energy classes
        float noDataY = rowStartY - energyClassDefs.Length * rowHeight - (isXRDevice ? 10f : 8f);

        // Separator line
        GameObject separator = CreateChild("Separator", legendPanel, new Vector2(isXRDevice ? 260f : 220f, 1f));
        RectTransform sepRect = separator.GetComponent<RectTransform>();
        sepRect.anchoredPosition = new Vector2(0, noDataY + rowHeight * 0.55f);
        Image sepImg = separator.AddComponent<Image>();
        sepImg.color = new Color(0.4f, 0.4f, 0.4f, 0.5f);

        // No data swatch (white = uncolored buildings)
        GameObject ndSwatch = CreateChild("Swatch_NoData", legendPanel, new Vector2(swatchSize, swatchSize));
        RectTransform ndSwR = ndSwatch.GetComponent<RectTransform>();
        ndSwR.anchoredPosition = new Vector2(isXRDevice ? -120f : -100f, noDataY);
        noDataSwatch = ndSwatch.AddComponent<Image>();
        noDataSwatch.color = Color.white;

        // No data label
        GameObject ndLblObj = CreateChild("Label_NoData", legendPanel, new Vector2(160, rowHeight));
        RectTransform ndLblR = ndLblObj.GetComponent<RectTransform>();
        ndLblR.anchoredPosition = new Vector2(isXRDevice ? -20f : -5f, noDataY);
        Text ndLblText = ndLblObj.AddComponent<Text>();
        ndLblText.text = "No data";
        ndLblText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        ndLblText.fontSize = isXRDevice ? 22 : 14;
        ndLblText.alignment = TextAnchor.MiddleLeft;
        ndLblText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
        ndLblText.fontStyle = FontStyle.Italic;

        // No data count
        GameObject ndCntObj = CreateChild("Count_NoData", legendPanel, new Vector2(70, rowHeight));
        RectTransform ndCntR = ndCntObj.GetComponent<RectTransform>();
        ndCntR.anchoredPosition = new Vector2(isXRDevice ? 115f : 95f, noDataY);
        noDataCountText = ndCntObj.AddComponent<Text>();
        noDataCountText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        noDataCountText.fontSize = isXRDevice ? 20 : 13;
        noDataCountText.alignment = TextAnchor.MiddleRight;
        noDataCountText.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        noDataCountText.text = "";

        // Initial counts
        RefreshCounts();
    }

    // === DATA ===

    void RefreshCounts()
    {
        if (energyManager == null || countTexts == null) return;

        // Count buildings per energy class and sample API colors
        int classCount = energyClassDefs.Length;
        int[] counts = new int[classCount];
        Color[] sampledColors = new Color[classCount]; // first API color found per class
        bool[] colorFound = new bool[classCount];
        int noData = 0;
        totalBuildings = 0;

        foreach (var kvp in energyManager.buildingDataCache)
        {
            totalBuildings++;
            // Use energyDemandBefore (begin state = current condition)
            int kwh = kvp.Value.energyDemandBefore;

            if (kwh <= 0)
            {
                noData++;
                continue;
            }

            for (int i = 0; i < classCount; i++)
            {
                if (kwh >= energyClassDefs[i].minKwh && kwh < energyClassDefs[i].maxKwh)
                {
                    counts[i]++;

                    // Sample the actual API color for this class (first match wins)
                    if (!colorFound[i] && energyManager.buildingColorCache.TryGetValue(kvp.Key, out Color apiColor))
                    {
                        sampledColors[i] = apiColor;
                        colorFound[i] = true;
                    }
                    break;
                }
            }
        }

        // Update swatch colors dynamically from API data
        for (int i = 0; i < classCount; i++)
        {
            if (colorFound[i] && swatchImages[i] != null)
            {
                swatchImages[i].color = sampledColors[i];
            }
            // If no building in this class, keep fallback color (already set at creation)
        }
        colorsResolved = true;

        // Update count labels (all percentages relative to totalBuildings → sums to 100%)
        for (int i = 0; i < classCount; i++)
        {
            if (totalBuildings > 0)
            {
                float pct = counts[i] * 100f / totalBuildings;
                countTexts[i].text = $"{pct:F1}%";
            }
            else
            {
                countTexts[i].text = "—";
            }
        }

        // Update "No data" row
        if (noDataCountText != null)
        {
            if (totalBuildings > 0)
            {
                float noDataPct = noData * 100f / totalBuildings;
                noDataCountText.text = $"{noDataPct:F1}%";
            }
            else
            {
                noDataCountText.text = "—";
            }
        }

        // Update title with total building count
        if (totalBuildings > 0)
        {
            titleText.text = $"Energy Demand Classes\n<size={( isXRDevice ? 18 : 12)}><color=#999999>{totalBuildings} buildings</color></size>";
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
