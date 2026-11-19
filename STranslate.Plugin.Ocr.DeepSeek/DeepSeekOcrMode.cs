namespace STranslate.Plugin.Ocr.DeepSeek;

/// <summary>
/// 识别模式，对应 OCR 接口的 purpose 参数
/// </summary>
public enum DeepSeekOcrMode
{
    /// <summary>
    /// text_recognition：对图片返回 Markdown 文本，不含坐标
    /// </summary>
    TextRecognition,

    /// <summary>
    /// rich_layout_recognition：按文本块返回，带坐标
    /// </summary>
    RichLayoutRecognition,
}

internal static class DeepSeekOcrModeExtensions
{
    /// <summary>
    /// 接口只接受这两个取值，配置里出现其他内容时按文字识别处理
    /// </summary>
    internal static string ToPurpose(this DeepSeekOcrMode mode) =>
        mode == DeepSeekOcrMode.RichLayoutRecognition ? "rich_layout_recognition" : "text_recognition";
}
