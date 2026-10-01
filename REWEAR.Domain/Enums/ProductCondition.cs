namespace REWEAR.Domain.Enums;

/// <summary>
/// Tình trạng sản phẩm secondhand/upcycled.
/// </summary>
public enum ProductCondition
{
    /// <summary>
    /// Gần như mới, rất ít dấu hiệu sử dụng.
    /// </summary>
    LikeNew = 0,

    /// <summary>
    /// Tình trạng rất tốt, có dấu hiệu sử dụng nhẹ.
    /// </summary>
    Excellent = 1,

    /// <summary>
    /// Tình trạng tốt, có dấu hiệu sử dụng rõ nhưng vẫn đảm bảo chất lượng.
    /// </summary>
    Good = 2,

    /// <summary>
    /// Đã qua sử dụng đáng kể nhưng vẫn đạt tiêu chuẩn bán của REWEAR.
    /// </summary>
    Fair = 3
}
