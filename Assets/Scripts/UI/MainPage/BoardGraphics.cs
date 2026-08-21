using System.Collections.Generic;
using AuctionGame;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class BoardGraphics : MaskableGraphic
{
    [Header("Grid")]
    [SerializeField, Min(1)]
    private int columns = 10;

    [SerializeField, Min(1)]
    private int rows = 10;

    [SerializeField, Min(0.5f)]
    private float lineThickness = 1f;

    private bool isUpdatingSize;
    private readonly List<GameObject> itemGraphics = new List<GameObject>();
    private Material revealOneGridMaterial;
    private Material revealSizeMaterial;

    public int Columns => columns;
    public int Rows => rows;
    public float LineThickness => lineThickness;

    public float CellSize
    {
        get
        {
            if (columns <= 0)
            {
                return 0f;
            }

            return rectTransform.rect.width / columns;
        }
    }

    protected override void Awake()
    {
        base.Awake();

        // 网格只负责显示，不拦截鼠标事件。
        raycastTarget = false;
        CreateRevealMaterials();
    }

    protected override void OnDestroy()
    {
        clearItems();

        if (revealOneGridMaterial != null)
        {
            DestroySafely(revealOneGridMaterial);
        }

        if (revealSizeMaterial != null)
        {
            DestroySafely(revealSizeMaterial);
        }

        base.OnDestroy();
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        ApplyHeightFromCurrentWidth();
        SetVerticesDirty();
    }

    /// <summary>
    /// RectTransform 宽度发生变化时由 Unity 自动调用。
    /// 例如 Viewport 尺寸变化、分辨率变化、窗口缩放。
    /// </summary>
    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();

        if (!isActiveAndEnabled || isUpdatingSize)
        {
            return;
        }

        ApplyHeightFromCurrentWidth();
        SetVerticesDirty();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();

        columns = Mathf.Max(1, columns);
        rows = Mathf.Max(1, rows);
        lineThickness = Mathf.Max(0.5f, lineThickness);

        SetVerticesDirty();
    }
#endif

    /// <summary>
    /// 设置网格。
    /// 不再传入 cellSize，cellSize 由当前宽度自动计算。
    /// </summary>
    public void SetGrid(
        int newColumns,
        int newRows,
        float newLineThickness,
        Color lineColor)
    {
        columns = Mathf.Max(1, newColumns);
        rows = Mathf.Max(1, newRows);
        lineThickness = Mathf.Max(0.5f, newLineThickness);
        color = lineColor;

        ApplyHeightFromCurrentWidth();
        SetVerticesDirty();
    }

    public void clearItems()
    {
        foreach (GameObject itemGraphic in itemGraphics)
        {
            if (itemGraphic != null)
            {
                DestroySafely(itemGraphic);
            }
        }

        itemGraphics.Clear();
    }

    private static void DestroySafely(Object target)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            DestroyImmediate(target);
            return;
        }
#endif

        Destroy(target);
    }

    public void revealOneGrid(Vector2Int topLeft, ItemRarity rarity)
    {
        CreateRevealGraphic(
            topLeft,
            topLeft,
            GetRarityColor(rarity),
            revealOneGridMaterial,
            "RevealOneGrid");
    }

    public void revealSize(Vector2Int topLeft, Vector2Int bottomRight, ItemRarity rarity)
    {
        CreateRevealGraphic(
            topLeft,
            bottomRight,
            GetRarityColor(rarity),
            revealSizeMaterial,
            "RevealSize");
    }

    public void paintItem(
        Sprite itemImage,
        ItemRarity rarity,
        Vector2Int topLeft,
        Vector2Int bottomRight,
        Vector2Int itemSize)
    {
        RectTransform background = CreateItemRect(topLeft, bottomRight, "PaintItem");
        Image backgroundImage = background.gameObject.AddComponent<Image>();
        Color rarityColor = GetRarityColor(rarity);
        rarityColor.a = 0.7f;
        backgroundImage.color = rarityColor;
        backgroundImage.raycastTarget = false;

        if (itemImage == null)
        {
            return;
        }

        GameObject imageObject = new GameObject(
            "ItemImage",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        imageObject.layer = gameObject.layer;
        imageObject.transform.SetParent(background, false);

        RectTransform imageRect = imageObject.GetComponent<RectTransform>();
        imageRect.anchorMin = new Vector2(0.5f, 0.5f);
        imageRect.anchorMax = new Vector2(0.5f, 0.5f);
        imageRect.pivot = new Vector2(0.5f, 0.5f);
        imageRect.anchoredPosition = Vector2.zero;

        int gridWidth = bottomRight.x - topLeft.x + 1;
        int gridHeight = bottomRight.y - topLeft.y + 1;
        bool rotate = itemSize.x == gridHeight && itemSize.y == gridWidth &&
                      (itemSize.x != gridWidth || itemSize.y != gridHeight);

        imageRect.sizeDelta = rotate
            ? new Vector2(background.rect.height, background.rect.width)
            : background.rect.size;
        imageRect.localEulerAngles = rotate ? new Vector3(0f, 0f, 90f) : Vector3.zero;

        Image image = imageObject.GetComponent<Image>();
        image.sprite = itemImage;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private void CreateRevealGraphic(
        Vector2Int topLeft,
        Vector2Int bottomRight,
        Color rarityColor,
        Material material,
        string graphicName)
    {
        RectTransform graphicRect = CreateItemRect(topLeft, bottomRight, graphicName);
        Image image = graphicRect.gameObject.AddComponent<Image>();
        image.color = rarityColor;
        image.material = material;
        image.raycastTarget = false;
    }

    private RectTransform CreateItemRect(Vector2Int topLeft, Vector2Int bottomRight, string graphicName)
    {
        GameObject graphicObject = new GameObject(graphicName, typeof(RectTransform), typeof(CanvasRenderer));
        graphicObject.layer = gameObject.layer;
        graphicObject.transform.SetParent(rectTransform, false);
        itemGraphics.Add(graphicObject);

        float cellSize = CellSize;
        int width = Mathf.Max(1, bottomRight.x - topLeft.x + 1);
        int height = Mathf.Max(1, bottomRight.y - topLeft.y + 1);

        RectTransform graphicRect = graphicObject.GetComponent<RectTransform>();
        graphicRect.anchorMin = new Vector2(0f, 1f);
        graphicRect.anchorMax = new Vector2(0f, 1f);
        graphicRect.pivot = new Vector2(0f, 1f);
        graphicRect.anchoredPosition = new Vector2(topLeft.x * cellSize, -topLeft.y * cellSize);
        graphicRect.sizeDelta = new Vector2(width * cellSize, height * cellSize);
        return graphicRect;
    }

    private void CreateRevealMaterials()
    {
        Shader shader = Shader.Find("UI/BoardReveal");
        if (shader == null)
        {
            return;
        }

        revealOneGridMaterial = new Material(shader)
        {
            name = "BoardRevealOneGrid"
        };
        revealOneGridMaterial.SetFloat("_BorderWidth", 4f);
        revealOneGridMaterial.SetFloat("_Dashed", 0f);

        revealSizeMaterial = new Material(shader)
        {
            name = "BoardRevealSize"
        };
        revealSizeMaterial.SetFloat("_BorderWidth", 1f);
        revealSizeMaterial.SetFloat("_Dashed", 1f);
    }

    private static Color GetRarityColor(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.SSR:
                return GlobalSettings.BoardRarityColor.SSR;
            case ItemRarity.SR:
                return GlobalSettings.BoardRarityColor.SR;
            case ItemRarity.R:
                return GlobalSettings.BoardRarityColor.R;
            default:
                return GlobalSettings.BoardRarityColor.N;
        }
    }

    /// <summary>
    /// 根据当前宽度计算单格尺寸，再计算完整仓库高度。
    /// </summary>
    private void ApplyHeightFromCurrentWidth()
    {
        if (isUpdatingSize || columns <= 0)
        {
            return;
        }

        float currentWidth = rectTransform.rect.width;

        // 某些初始化阶段 RectTransform 可能还没有有效宽度。
        if (currentWidth <= 0f)
        {
            return;
        }

        float currentCellSize = currentWidth / columns;
        float targetHeight = currentCellSize * rows;

        if (Mathf.Abs(rectTransform.rect.height - targetHeight) < 0.01f)
        {
            return;
        }

        isUpdatingSize = true;

        rectTransform.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            targetHeight
        );

        isUpdatingSize = false;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = rectTransform.rect;

        float scaleFactor = canvas != null
            ? Mathf.Max(0.0001f, canvas.scaleFactor)
            : 1f;

        // 单格尺寸直接由当前宽度计算。
        float currentCellSize = rect.width / columns;

        // 把线宽转换为整数屏幕像素。
        int thicknessPixels = Mathf.Max(
            1,
            Mathf.RoundToInt(lineThickness * scaleFactor)
        );

        // 再转换回当前 Canvas 的 UI 单位。
        float actualThickness =
            thicknessPixels / scaleFactor;

        float halfThickness =
            actualThickness * 0.5f;

        float left = rect.xMin;
        float right = rect.xMax;
        float top = rect.yMax;
        float bottom = rect.yMin;

        // 绘制竖线。
        for (int x = 0; x <= columns; x++)
        {
            // 左边框完全绘制在 Rect 内。
            if (x == 0)
            {
                AddQuad(vh, left, bottom, left + actualThickness, top);

                continue;
            }

            // 右边框完全绘制在 Rect 内。
            if (x == columns)
            {
                AddQuad(vh, right - actualThickness, bottom, right, top);
                continue;
            }

            float lineX =
                left + x * currentCellSize;

            lineX = SnapLineCenter(lineX, scaleFactor, thicknessPixels);

            AddQuad(vh, lineX - halfThickness, bottom, lineX + halfThickness, top);
        }

        // 绘制横线。
        for (int y = 0; y <= rows; y++)
        {
            // 上边框完全绘制在 Rect 内。
            if (y == 0)
            {
                AddQuad(vh, left, top - actualThickness, right, top);

                continue;
            }

            // 下边框完全绘制在 Rect 内。
            if (y == rows)
            {
                AddQuad(vh, left, bottom, right, bottom + actualThickness);

                continue;
            }

            float lineY = top - y * currentCellSize;

            lineY = SnapLineCenter(lineY, scaleFactor, thicknessPixels);

            AddQuad(vh, left, lineY - halfThickness, right, lineY + halfThickness);
        }
    }

    private static float SnapLineCenter(float localPosition, float scaleFactor, int thicknessPixels)
    {
        float pixelPosition = localPosition * scaleFactor;

        if ((thicknessPixels & 1) == 1)
        {
            // 奇数像素宽线条的中心落在半像素位置。
            pixelPosition = Mathf.Floor(pixelPosition) + 0.5f;
        }
        else
        {
            // 偶数像素宽线条的中心落在整数像素位置。
            pixelPosition = Mathf.Round(pixelPosition);
        }

        return pixelPosition / scaleFactor;
    }

    private void AddQuad(VertexHelper vh, float left, float bottom, float right, float top)
    {
        int startIndex = vh.currentVertCount;

        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;

        vertex.position = new Vector3(left, bottom);
        vertex.uv0 = Vector2.zero;
        vh.AddVert(vertex);

        vertex.position = new Vector3(left, top);
        vertex.uv0 = Vector2.up;
        vh.AddVert(vertex);

        vertex.position = new Vector3(right, top);
        vertex.uv0 = Vector2.one;
        vh.AddVert(vertex);

        vertex.position = new Vector3(right, bottom);
        vertex.uv0 = Vector2.right;
        vh.AddVert(vertex);

        vh.AddTriangle(startIndex, startIndex + 1, startIndex + 2);

        vh.AddTriangle(startIndex + 2, startIndex + 3, startIndex);
    }
}
