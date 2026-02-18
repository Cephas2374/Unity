using UnityEngine;
using UnityEngine.UI;
using System.Text;

public class BuildingInfoPanel : MonoBehaviour
{
    [Header("UI References")]
    public Text titleText;
    public Text gmlIdText;
    public Text constructionYearText;
    public Text numberOfStoreyText;
    public Text energyConsumptionText;
    public Text heatingSystemText;
    public Text co2EmissionsText;
    public Text energyDemandText;
    public Button closeButton;
    public Button editButton;
    
    [Header("Panel Settings")]
    public float displayDuration = 0f; // 0 = stay until manually closed
    
    private GameObject panel;
    private BuildingData currentData;
    private Color buildingColor = Color.white;
    private float hideTimer = 0f;
    private bool isDisplaying = false;
    private BuildingEnergyManager buildingManager;
    
    void Start()
    {
        // Get reference to building energy manager for color lookups
        buildingManager = FindObjectOfType<BuildingEnergyManager>();
        
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(ClosePanel);
        }
        
        if (editButton != null)
        {
            editButton.onClick.AddListener(OnEditClicked);
        }
        
        // Start hidden
        if (panel == null)
        {
            panel = this.gameObject;
        }
        panel.SetActive(false);
    }
    
    void Update()
    {
        if (isDisplaying && displayDuration > 0)
        {
            hideTimer -= Time.deltaTime;
            if (hideTimer <= 0f)
            {
                ClosePanel();
            }
        }
    }
    
    public void DisplayData(BuildingData data)
    {
        currentData = data;
        
        if (titleText != null)
            titleText.text = "Building Information";
        
        if (gmlIdText != null)
            gmlIdText.text = $"ID: {data.gmlId}";
        
        if (constructionYearText != null)
            constructionYearText.text = $"Construction Year: {data.constructionYear}";
        
        if (numberOfStoreyText != null)
            numberOfStoreyText.text = $"Number of Storeys: {data.numberOfStorey}";
        
        if (energyConsumptionText != null)
            energyConsumptionText.text = $"Energy Consumption: {data.energyConsumption:F2} kWh/m²";
        
        if (heatingSystemText != null)
        {
            string heatingInfo = $"Heating: {data.heatingSystemBefore}";
            if (!string.IsNullOrEmpty(data.heatingSystemAfter))
            {
                heatingInfo += $" → {data.heatingSystemAfter}";
            }
            heatingSystemText.text = heatingInfo;
        }
        
        panel.SetActive(true);
        isDisplaying = true;
        hideTimer = displayDuration;
    }
    
    public void ClosePanel()
    {
        panel.SetActive(false);
        isDisplaying = false;
    }
    
    void OnEditClicked()
    {
        if (currentData != null)
        {
            BuildingEnergyManager manager = FindObjectOfType<BuildingEnergyManager>();
            if (manager != null)
            {
                manager.OpenAttributesForm(currentData.gmlId);
            }
        }
    }
    
    void OnDestroy()
    {
        // Stop coroutines to prevent GC handle errors on domain reload
        StopAllCoroutines();
        
        // Clean up event listeners to prevent GC handle errors
        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton = null;
        }
        
        if (editButton != null)
        {
            editButton.onClick.RemoveAllListeners();
            editButton = null;
        }
    }
}
