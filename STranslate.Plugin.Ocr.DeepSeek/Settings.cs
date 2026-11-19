namespace STranslate.Plugin.Ocr.DeepSeek;

public class Settings
{
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>
    /// 接口地址，留空使用官方地址
    /// </summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>
    /// 识别模式
    /// </summary>
    public DeepSeekOcrMode Mode { get; set; } = DeepSeekOcrMode.TextRecognition;
    /// <summary>
    /// 服务繁忙（429/503）时的自动重试次数，null 或 &lt;= 0 不重试
    /// </summary>
    public int? MaxRetries { get; set; } = 2;
    public int RetryDelayMilliseconds { get; set; } = 1000;
}
