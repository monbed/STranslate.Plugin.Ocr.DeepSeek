using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using STranslate.Plugin.Ocr.DeepSeek.View;
using STranslate.Plugin.Ocr.DeepSeek.ViewModel;
using System.IO;
using System.Net.Http;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace STranslate.Plugin.Ocr.DeepSeek;

public class Main : ObservableObject, IOcrPlugin
{
    private Control? _settingUi;
    private SettingsViewModel? _viewModel;
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;

    // 接口没有语言参数，由模型自行识别
    public IEnumerable<LangEnum> SupportedLanguages => Enum.GetValues<LangEnum>();

    // 只有版面识别会返回文字坐标（文字识别对图片返回纯 Markdown 文本），图片翻译需要坐标
    public bool SupportBoxPoints() => Settings.Mode == DeepSeekOcrMode.RichLayoutRecognition;

    public Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, Settings, this);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }

    public void Init(IPluginContext context)
    {
        Context = context;
        Settings = context.LoadSettingStorage<Settings>();
    }

    public void Dispose() => _viewModel?.Dispose();

    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
    {
        var result = new OcrResult();

        if (string.IsNullOrWhiteSpace(Settings.ApiKey))
            return result.Fail(Context.GetTranslation("STranslate_Plugin_Ocr_DeepSeek_ApiKeyMissing"));

        IReadOnlyList<DeepSeekOcrPage> pages;
        try
        {
            pages = await SendAsync(request.ImageData, Settings.MaxRetries ?? 0, cancellationToken);
        }
        catch (DeepSeekOcrException ex)
        {
            // 接口明确返回的失败（密钥无效、余额不足、文件无法识别等），原因已在消息里
            Context.Logger.LogWarning("DeepSeek OCR failed: {Message}", ex.Message);
            return result.Fail(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutException)
        {
            Context.Logger.LogError(ex, "DeepSeek OCR request failed");
            return result.Fail(ex.Message);
        }

        var (width, height) = GetPixelSize(request);
        var truncated = false;

        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            truncated |= page.FinishReason == DeepSeekOcrClient.FinishReasonLength;

            // 坐标按页归一化，只有第一页与传入的图片对应
            var withBoxes = i == 0 && width > 0 && height > 0;

            foreach (var block in DeepSeekOcrParser.Parse(page.Text))
            {
                var content = new OcrContent { Text = block.Text };
                if (withBoxes && block.Box is { } box)
                    content.BoxPoints = ToBoxPoints(box, width, height);

                result.OcrContents.Add(content);
            }
        }

        if (truncated)
        {
            Context.Logger.LogWarning("DeepSeek OCR output is incomplete, finish_reason: length");
            Context.Snackbar.ShowWarning(Context.GetTranslation("STranslate_Plugin_Ocr_DeepSeek_Truncated"), 5000);
        }

        return result;
    }

    /// <summary>
    /// 用一张测试图片走一遍真实的识别请求，只确认连通性与鉴权，不重试
    /// </summary>
    internal async Task ValidateApiAsync(byte[] image, CancellationToken cancellationToken = default) =>
        await SendAsync(image, maxRetries: 0, cancellationToken);

    private Task<IReadOnlyList<DeepSeekOcrPage>> SendAsync(byte[] image, int maxRetries, CancellationToken cancellationToken) =>
        DeepSeekOcrClient.RecognizeAsync(
            DeepSeekOcrUrl.Build(Settings.Url),
            Settings.ApiKey,
            image,
            Settings.Mode.ToPurpose(),
            maxRetries,
            Settings.RetryDelayMilliseconds,
            (attempt, maxAttempts) => Context.Logger.LogWarning(
                "DeepSeek OCR is busy, retrying ({Attempt}/{MaxAttempts})", attempt, maxAttempts),
            cancellationToken);

    /// <summary>
    /// 图片像素尺寸，用于把归一化坐标换算成像素坐标。宿主未传入时从图片数据里读取
    /// </summary>
    private static (int Width, int Height) GetPixelSize(OcrRequest request)
    {
        if (request.PixelWidth > 0 && request.PixelHeight > 0)
            return (request.PixelWidth, request.PixelHeight);

        try
        {
            using var stream = new MemoryStream(request.ImageData);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch
        {
            // 读不出尺寸时只返回文本，不带坐标
            return (0, 0);
        }
    }

    /// <summary>
    /// 归一化坐标（0–1000）换算为图片像素坐标，按左上、右上、右下、左下的顺序返回
    /// </summary>
    private static List<BoxPoint> ToBoxPoints(float[] box, int width, int height)
    {
        var left = box[0] * width / 1000f;
        var top = box[1] * height / 1000f;
        var right = box[2] * width / 1000f;
        var bottom = box[3] * height / 1000f;

        return
        [
            new BoxPoint(left, top),
            new BoxPoint(right, top),
            new BoxPoint(right, bottom),
            new BoxPoint(left, bottom),
        ];
    }
}
