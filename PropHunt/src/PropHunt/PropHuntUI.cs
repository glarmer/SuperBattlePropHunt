using System.Linq;
using Gamemode_Lib.Teams;
using PropHunt;
using PropHunt.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class PropHuntUI : MonoBehaviour
{
    private Canvas canvas;
    private RectTransform rootPanel;

    private TMP_FontAsset gabaritoFont;
    private Sprite panelSprite;
    private Material panelBackgroundStencilMaterial;
    private Material panelOuterBackgroundMaterial;

    private Camera minimapCamera;
    private RenderTexture minimapTexture;
    private RawImage minimapImage;
    private RawImage propHeatImage;
    private RectTransform propHeatRect;
    private Texture2D propHeatTexture;

    private TMP_Text teamNameText;
    private TMP_Text leftInfoText;
    private TMP_Text rightInfoText;

    private Transform player;

    private PropHuntPlayer localPropHuntPlayer;
    private Vector2 cachedPropHeatDirection = Vector2.up;
    private bool hasPropHeatDirection;
    private float nextPropHeatUpdateTime;
    private float propHeatInitializationTime;
    private float propHeatActivationTime;

    private const float PropHeatSize = 108f;
    private const int PanelBackgroundStencil = 16;
    private const float PanelOuterBackgroundOutset = 2f;
    private const float PanelOuterBackgroundBottomOutset = 3f;

    private void Awake()
    {
        if (GameManager.LocalPlayerInfo)
        {
            localPropHuntPlayer = GameManager.LocalPlayerInfo.gameObject.GetComponent<PropHuntPlayer>();
        }
    }

    private void Start()
    {
        FindGameAssets();
        FindPlayer();

        CreateCanvas();
        CreatePanel();
        CreateRows();
        CreateMinimapCamera();

        propHeatInitializationTime = Time.time;
        RefreshPropHeatActivationTime();

        if (ConfigurationHandler.Instance != null)
            ConfigurationHandler.Instance.ConfigChanged += OnConfigurationChanged;
    }

    private void Update()
    {
        if (TeamManager.Instance)
        {
            string team = "";
            bool isHunter = TeamManager.Instance.LocalPlayerTeam.teamId == PropHuntGamemode.HUNTER_TEAM;
            team = isHunter ? "Hunter" : "Prop";

            string hex = "FFFFFF";
            hex = ColorUtility.ToHtmlStringRGB(isHunter ? TeamManager.Instance.Teams[PropHuntGamemode.HUNTER_TEAM].Color : TeamManager.Instance.Teams[PropHuntGamemode.PROP_TEAM].Color);

            teamNameText.text = $"Team: <color=#{hex}>{team}</color>";
            if (isHunter)
            {
                leftInfoText.text = $"Health: {localPropHuntPlayer.hunterHealth}";
                rightInfoText.text = $"Hunters: {PropManager.Instance.GetNumberOfHunters()}";
            }
            else
            {
                leftInfoText.text = $"Disguises: {localPropHuntPlayer.maxNumberOfDisguisesTaken - localPropHuntPlayer.numberOfDisguisesTaken}/{localPropHuntPlayer.maxNumberOfDisguisesTaken}";
                rightInfoText.text = $"Decoys: {localPropHuntPlayer.maxNumberOfDecoysCanPlace - localPropHuntPlayer.numberOfDecoysPlaced}/{localPropHuntPlayer.maxNumberOfDecoysCanPlace}";
            }

            UpdatePropHeatIndicator(isHunter);
        }
    }

    private void LateUpdate()
    {
        UpdateMinimapCamera();
    }

    private void FindGameAssets()
    {
        panelSprite = Resources.FindObjectsOfTypeAll<Sprite>()
            .FirstOrDefault(s => s.name == "UI_Tutorial_Objective_0");

        gabaritoFont = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
            .FirstOrDefault(f => f.name == "Gabarito-SemiBold SDF");

        if (panelSprite == null)
            Plugin.Log.LogWarning("Could not find sprite: UI_Tutorial_Objective_0");

        if (gabaritoFont == null)
            Plugin.Log.LogWarning("Could not find font: Gabarito-SemiBold SDF");

        CreatePanelBackgroundMaterials();
    }

    private void FindPlayer()
    {
        GameObject playerObj = GameManager.LocalPlayerInfo.gameObject;

        if (playerObj != null)
            player = playerObj.transform;
        else
            Plugin.Log.LogInfo("Could not find player");
    }

    private void CreateCanvas()
    {
        GameObject canvasObj = new GameObject("PropHunt_Mod_Canvas");
        canvasObj.transform.SetParent(transform, false);

        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();
    }

    private void CreatePanel()
    {
        GameObject panelObj = new GameObject("PropHunt_UI_Panel");
        panelObj.transform.SetParent(canvas.transform, false);

        rootPanel = panelObj.AddComponent<RectTransform>();
        
        rootPanel.anchorMin = new Vector2(1f, 1f);
        rootPanel.anchorMax = new Vector2(1f, 1f);
        rootPanel.pivot = new Vector2(1f, 1f);
        rootPanel.anchoredPosition = new Vector2(-25f, -25f);
        
        rootPanel.sizeDelta = new Vector2(320f, 400f);
        
        Image maskImage = panelObj.AddComponent<Image>();
        maskImage.sprite = panelSprite;
        maskImage.type = Image.Type.Sliced;
        maskImage.pixelsPerUnitMultiplier = 1f;
        maskImage.color = Color.white;

        Mask rootMask = panelObj.AddComponent<Mask>();
        rootMask.showMaskGraphic = false;

        GameObject bgObj = new GameObject("Panel_Background");
        bgObj.transform.SetParent(rootPanel, false);
        
        LayoutElement bgLayout = bgObj.AddComponent<LayoutElement>();
        bgLayout.ignoreLayout = true;

        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.sprite = panelSprite;
        bgImage.type = Image.Type.Sliced;
        bgImage.fillMethod = Image.FillMethod.Radial360;
        bgImage.fillAmount = 1f;
        bgImage.pixelsPerUnitMultiplier = 1f;
        bgImage.color = Color.white;
        bgImage.raycastTarget = false;

        RectTransform bgRt = bgImage.rectTransform;
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;

        bgObj.transform.SetAsFirstSibling();

        CreatePanelBackgroundStencilWriter();
        CreateOuterPanelBackground();

        VerticalLayoutGroup vertical = panelObj.AddComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(4, 4, 4, 0);
        vertical.spacing = 0f;
        vertical.childAlignment = TextAnchor.UpperCenter;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;
    }

    private void CreatePanelBackgroundStencilWriter()
    {
        GameObject stencilObj = new GameObject("Panel_Background_Stencil_Writer");
        stencilObj.transform.SetParent(canvas.transform, false);

        Image stencilImage = stencilObj.AddComponent<Image>();
        stencilImage.sprite = panelSprite;
        stencilImage.type = Image.Type.Sliced;
        stencilImage.fillMethod = Image.FillMethod.Radial360;
        stencilImage.fillAmount = 1f;
        stencilImage.pixelsPerUnitMultiplier = 1f;
        stencilImage.color = Color.white;
        stencilImage.raycastTarget = false;
        stencilImage.material = panelBackgroundStencilMaterial;

        RectTransform stencilRt = stencilImage.rectTransform;
        CopyRootPanelLayout(stencilRt);

        stencilObj.transform.SetSiblingIndex(rootPanel.GetSiblingIndex());
        rootPanel.SetAsLastSibling();
    }

    private void CreateOuterPanelBackground()
    {
        GameObject outerBgObj = new GameObject("Panel_Background_Outer");
        outerBgObj.transform.SetParent(canvas.transform, false);

        Image outerBgImage = outerBgObj.AddComponent<Image>();
        outerBgImage.sprite = panelSprite;
        outerBgImage.type = Image.Type.Sliced;
        outerBgImage.fillMethod = Image.FillMethod.Radial360;
        outerBgImage.fillAmount = 1f;
        outerBgImage.pixelsPerUnitMultiplier = 1f;
        outerBgImage.color = Color.white;
        outerBgImage.raycastTarget = false;
        outerBgImage.material = panelOuterBackgroundMaterial;

        RectTransform outerBgRt = outerBgImage.rectTransform;
        CopyRootPanelLayout(outerBgRt);
        outerBgRt.anchoredPosition = rootPanel.anchoredPosition + Vector2.one * PanelOuterBackgroundOutset;
        outerBgRt.sizeDelta = rootPanel.sizeDelta + new Vector2(
            PanelOuterBackgroundOutset * 2f,
            PanelOuterBackgroundOutset + PanelOuterBackgroundBottomOutset
        );

        outerBgObj.transform.SetSiblingIndex(rootPanel.GetSiblingIndex());
        rootPanel.SetAsLastSibling();
    }

    private void CopyRootPanelLayout(RectTransform target)
    {
        target.anchorMin = rootPanel.anchorMin;
        target.anchorMax = rootPanel.anchorMax;
        target.pivot = rootPanel.pivot;
        target.anchoredPosition = rootPanel.anchoredPosition;
        target.sizeDelta = rootPanel.sizeDelta;
    }

    private void CreatePanelBackgroundMaterials()
    {
        Shader uiShader = Shader.Find("UI/Default");
        if (uiShader == null)
        {
            Plugin.Log.LogWarning("Could not find shader: UI/Default");
            return;
        }

        panelBackgroundStencilMaterial = new Material(uiShader)
        {
            name = "PropHunt_Panel_Background_Stencil"
        };
        ConfigureStencilMaterial(
            panelBackgroundStencilMaterial,
            CompareFunction.Always,
            StencilOp.Replace,
            PanelBackgroundStencil,
            0
        );

        panelOuterBackgroundMaterial = new Material(uiShader)
        {
            name = "PropHunt_Panel_Outer_Background"
        };
        ConfigureStencilMaterial(
            panelOuterBackgroundMaterial,
            CompareFunction.NotEqual,
            StencilOp.Keep,
            PanelBackgroundStencil,
            ColorWriteMask.All
        );
    }

    private void ConfigureStencilMaterial(
        Material material,
        CompareFunction stencilComparison,
        StencilOp stencilOperation,
        int stencilReference,
        ColorWriteMask colorWriteMask)
    {
        material.SetInt("_StencilComp", (int)stencilComparison);
        material.SetInt("_Stencil", stencilReference);
        material.SetInt("_StencilOp", (int)stencilOperation);
        material.SetInt("_StencilWriteMask", PanelBackgroundStencil);
        material.SetInt("_StencilReadMask", PanelBackgroundStencil);
        material.SetInt("_ColorMask", (int)colorWriteMask);
        material.SetInt("_UseUIAlphaClip", 1);
        material.EnableKeyword("UNITY_UI_ALPHACLIP");
    }

    private void CreateRows()
    {
        GameObject row1 = CreateRow("Row_TeamName", rootPanel, 36);
        teamNameText = CreateText(row1.transform, "TEAM PROPS", 26, TextAlignmentOptions.Center);

        CreateRowDivider(rootPanel, 4f);

        GameObject row2 = CreateRow("Row_Info", rootPanel, 36f);

        HorizontalLayoutGroup horizontal = row2.AddComponent<HorizontalLayoutGroup>();
        horizontal.spacing = 0f;
        horizontal.childAlignment = TextAnchor.MiddleCenter;
        horizontal.childControlWidth = true;
        horizontal.childControlHeight = true;
        horizontal.childForceExpandWidth = false;
        horizontal.childForceExpandHeight = true;

        leftInfoText = CreateText(row2.transform, "Props: 0", 22, TextAlignmentOptions.Center);
        AddFlexibleLayout(leftInfoText.gameObject, 1);

        CreateColumnDivider(row2.transform, 1f);

        rightInfoText = CreateText(row2.transform, "Hunters: 0", 22, TextAlignmentOptions.Center);
        AddFlexibleLayout(rightInfoText.gameObject, 4);

        CreateRowDivider(rootPanel, 4f);

        float height = 310f;
        GameObject row3 = CreateRow("Row_Minimap_Spacer", rootPanel, height);

        CreateBottomMinimapOverlay(height);
    }
    
    private GameObject CreateRowDivider(Transform parent, float height = 4f)
    {
        GameObject dividerObj = new GameObject("Row_Divider");
        dividerObj.transform.SetParent(parent, false);

        Image image = dividerObj.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.35f);
        image.raycastTarget = false;

        RectTransform rt = dividerObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, height);

        LayoutElement layout = dividerObj.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
        layout.flexibleWidth = 1f;

        return dividerObj;
    }

    private GameObject CreateColumnDivider(Transform parent, float width)
    {
        GameObject dividerObj = new GameObject("Column_Divider");
        dividerObj.transform.SetParent(parent, false);

        Image image = dividerObj.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.35f);
        image.raycastTarget = false;

        LayoutElement layout = dividerObj.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.minWidth = width;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 1f;

        return dividerObj;
    }
    
    private void CreateBottomMinimapOverlay(float height)
    {
        GameObject maskObj = new GameObject("Minimap_Panel_Shape_Mask");
        maskObj.transform.SetParent(rootPanel, false);

        LayoutElement layout = maskObj.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        Image maskImage = maskObj.AddComponent<Image>();
        maskImage.sprite = panelSprite;
        maskImage.type = Image.Type.Sliced;
        maskImage.pixelsPerUnitMultiplier = 1f;
        maskImage.color = Color.white;
        maskImage.raycastTarget = false;

        Mask mask = maskObj.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        RectTransform maskRt = maskImage.rectTransform;
        
        maskRt.anchorMin = Vector2.zero;
        maskRt.anchorMax = Vector2.one;
        maskRt.pivot = new Vector2(0.5f, 0.5f);
        maskRt.offsetMin = Vector2.zero;
        maskRt.offsetMax = Vector2.zero;

        GameObject mapObj = new GameObject("Minimap_Image");
        mapObj.transform.SetParent(maskObj.transform, false);

        minimapImage = mapObj.AddComponent<RawImage>();
        minimapImage.color = Color.white;
        minimapImage.raycastTarget = false;

        RectTransform mapRt = minimapImage.rectTransform;

        mapRt.anchorMin = new Vector2(0f, 0f);
        mapRt.anchorMax = new Vector2(1f, 0f);
        mapRt.pivot = new Vector2(0.5f, 0f);
        mapRt.anchoredPosition = Vector2.zero;

        float sideInset = 4f;
        float bottomInset = 3f;

        mapRt.sizeDelta = new Vector2(0f, height);
        mapRt.offsetMin = new Vector2(sideInset, bottomInset);
        mapRt.offsetMax = new Vector2(-sideInset, height + bottomInset);

        CreatePropHeatIndicator(mapObj.transform);

        maskObj.transform.SetSiblingIndex(1);
    }

    private void CreatePropHeatIndicator(Transform parent)
    {
        GameObject heatObj = new GameObject("Prop_Heat_Indicator");
        heatObj.transform.SetParent(parent, false);

        propHeatImage = heatObj.AddComponent<RawImage>();
        propHeatImage.texture = CreatePropHeatTexture(96);
        propHeatImage.color = new Color(1f, 0.04f, 0f, 0f);
        propHeatImage.raycastTarget = false;

        propHeatRect = propHeatImage.rectTransform;
        propHeatRect.anchorMin = new Vector2(0.5f, 0.5f);
        propHeatRect.anchorMax = new Vector2(0.5f, 0.5f);
        propHeatRect.pivot = new Vector2(0.5f, 0.5f);
        propHeatRect.sizeDelta = new Vector2(PropHeatSize, PropHeatSize);
        propHeatRect.anchoredPosition = Vector2.zero;

        heatObj.SetActive(false);
        heatObj.transform.SetAsLastSibling();
    }

    private Texture2D CreatePropHeatTexture(int size)
    {
        propHeatTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        propHeatTexture.name = "PropHunt_Prop_Heat_Texture";
        propHeatTexture.wrapMode = TextureWrapMode.Clamp;

        float center = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / center;
                float dy = (y - center) / center;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(1f - distance);
                alpha = alpha * alpha;
                propHeatTexture.SetPixel(x, y, new Color(1f, 0.02f, 0f, alpha));
            }
        }

        propHeatTexture.Apply();
        return propHeatTexture;
    }

    private GameObject CreateRow(string name, Transform parent, float height)
    {
        GameObject rowObj = new GameObject(name);
        rowObj.transform.SetParent(parent, false);

        RectTransform rt = rowObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, height);

        LayoutElement layout = rowObj.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
        layout.flexibleWidth = 1f;

        return rowObj;
    }

    private TMP_Text CreateText(
        Transform parent,
        string value,
        int fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject textObj = new GameObject("TMP_Text");
        textObj.transform.SetParent(parent, false);

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.black;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;

        if (gabaritoFont != null)
            text.font = gabaritoFont;

        RectTransform rt = text.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        return text;
    }

    private void AddFlexibleLayout(GameObject obj, int flexibleWidth)
    {
        LayoutElement layout = obj.GetComponent<LayoutElement>();

        if (layout == null)
            layout = obj.AddComponent<LayoutElement>();

        layout.flexibleWidth = flexibleWidth;
    }

    private void CreateMinimapCamera()
    {
        GameObject camObj = new GameObject("PropHunt_Minimap_Camera");
        minimapCamera = camObj.AddComponent<Camera>();

        minimapCamera.orthographic = true;
        minimapCamera.orthographicSize = 35f;

        minimapCamera.clearFlags = CameraClearFlags.SolidColor;
        minimapCamera.backgroundColor = Color.black;

        minimapCamera.nearClipPlane = 0.1f;
        minimapCamera.farClipPlane = 500f;

        minimapTexture = new RenderTexture(512, 512, 16);
        minimapTexture.name = "PropHunt_Minimap_RenderTexture";
        minimapTexture.Create();

        minimapCamera.targetTexture = minimapTexture;
        minimapImage.texture = minimapTexture;
    }

    private void UpdateMinimapCamera()
    {
        if (player == null || minimapCamera == null)
            return;

        Vector3 p = player.position;

        minimapCamera.transform.position = new Vector3(p.x, p.y + 80f, p.z);
        minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private void UpdatePropHeatIndicator(bool isHunter)
    {
        if (propHeatImage == null || propHeatRect == null)
            return;

        if (!isHunter || PropManager.Instance == null || player == null || Time.time < propHeatActivationTime)
        {
            propHeatImage.gameObject.SetActive(false);
            hasPropHeatDirection = false;
            nextPropHeatUpdateTime = 0f;
            return;
        }

        if (Time.time >= nextPropHeatUpdateTime)
        {
            bool refreshed = TryRefreshPropHeatDirection();
            hasPropHeatDirection = refreshed;
            nextPropHeatUpdateTime = Time.time + (refreshed ? GetPropHeatUpdateInterval() : 0.25f);
        }

        propHeatImage.gameObject.SetActive(hasPropHeatDirection);
        if (!hasPropHeatDirection)
            return;

        float pulse = 0.78f + Mathf.Sin(Time.time * 4f) * 0.22f;
        propHeatImage.color = new Color(1f, 0.04f, 0f, 0.48f * pulse);
    }

    private bool TryRefreshPropHeatDirection()
    {
        Rect minimapRect = minimapImage.rectTransform.rect;
        if (minimapRect.width < 1f || minimapRect.height < 1f)
            return false;

        Vector3 totalPosition = Vector3.zero;
        Vector3 nearestOffset = Vector3.zero;
        float nearestDistanceSquared = float.PositiveInfinity;
        int propCount = 0;

        foreach (PlayerInfo prop in PropManager.Instance.GetRemainingProps())
        {
            if (prop == null || prop.transform == null)
                continue;

            Vector3 propPosition = prop.transform.position;
            Vector3 propOffset = propPosition - player.position;
            float distanceSquared = propOffset.sqrMagnitude;

            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestOffset = propOffset;
            }

            totalPosition += propPosition;
            propCount++;
        }

        if (propCount == 0)
            return false;

        Vector3 averagePropPosition = totalPosition / propCount;
        Vector3 offset = averagePropPosition - player.position;
        Vector2 direction = new Vector2(offset.x, offset.z);

        if (direction.sqrMagnitude < 0.01f)
            direction = new Vector2(nearestOffset.x, nearestOffset.z);

        if (direction.sqrMagnitude < 0.01f)
            direction = cachedPropHeatDirection;

        cachedPropHeatDirection = direction.normalized;
        propHeatRect.anchoredPosition = GetMinimapEdgePosition(cachedPropHeatDirection, minimapRect);
        return true;
    }

    private Vector2 GetMinimapEdgePosition(Vector2 direction, Rect minimapRect)
    {
        float halfWidth = Mathf.Max(0f, minimapRect.width * 0.5f - PropHeatSize * 0.5f);
        float halfHeight = Mathf.Max(0f, minimapRect.height * 0.5f - PropHeatSize * 0.5f);

        float xScale = Mathf.Abs(direction.x) > 0.001f ? halfWidth / Mathf.Abs(direction.x) : float.PositiveInfinity;
        float yScale = Mathf.Abs(direction.y) > 0.001f ? halfHeight / Mathf.Abs(direction.y) : float.PositiveInfinity;
        float scale = Mathf.Min(xScale, yScale);

        return direction * scale;
    }

    private float GetPropHeatActivationDelay()
    {
        ConfigurationHandler configuration = ConfigurationHandler.Instance;
        if (configuration == null)
            return 0f;

        return configuration.RoundTimerLengthInSeconds * (configuration.PropHeatActivationPercent / 100f);
    }

    private float GetPropHeatUpdateInterval()
    {
        ConfigurationHandler configuration = ConfigurationHandler.Instance;
        if (configuration == null)
            return 5f;

        return Mathf.Max(1f, configuration.PropHeatUpdateIntervalInSeconds);
    }

    private void RefreshPropHeatActivationTime()
    {
        propHeatActivationTime = propHeatInitializationTime + GetPropHeatActivationDelay();
    }

    private void OnConfigurationChanged()
    {
        RefreshPropHeatActivationTime();
    }

    public void SetTeamName(string teamName)
    {
        if (teamNameText != null)
            teamNameText.text = teamName;
    }

    public void SetInfoText(string left, string right)
    {
        if (leftInfoText != null)
            leftInfoText.text = left;

        if (rightInfoText != null)
            rightInfoText.text = right;
    }

    private void OnDestroy()
    {
        if (minimapTexture != null)
        {
            minimapTexture.Release();
            Destroy(minimapTexture);
        }

        if (minimapCamera != null)
            Destroy(minimapCamera.gameObject);

        if (propHeatTexture != null)
            Destroy(propHeatTexture);

        if (panelBackgroundStencilMaterial != null)
            Destroy(panelBackgroundStencilMaterial);

        if (panelOuterBackgroundMaterial != null)
            Destroy(panelOuterBackgroundMaterial);

        if (ConfigurationHandler.Instance != null)
            ConfigurationHandler.Instance.ConfigChanged -= OnConfigurationChanged;
    }
}
