using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace STranslate.Plugin.Ocr.DeepSeek.ViewModel;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IPluginContext _context;
    private readonly Settings _settings;
    private readonly Main _main;

    public SettingsViewModel(IPluginContext context, Settings settings, Main main)
    {
        _context = context;
        _settings = settings;
        _main = main;

        Url = _settings.Url;
        ApiKey = _settings.ApiKey;
        Mode = Modes.Contains(_settings.Mode) ? _settings.Mode : DeepSeekOcrMode.TextRecognition;

        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ApiKey):
                _settings.ApiKey = ApiKey;
                break;
            case nameof(Url):
                _settings.Url = Url;
                break;
            case nameof(Mode):
                _settings.Mode = Mode;
                break;
            default:
                return;
        }
        _context.SaveSettingStorage<Settings>();
    }

    public List<DeepSeekOcrMode> Modes => [.. Enum.GetValues<DeepSeekOcrMode>()];

    [ObservableProperty] public partial string ValidateResult { get; set; } = string.Empty;
    [ObservableProperty] public partial string Url { get; set; }
    [ObservableProperty] public partial string ApiKey { get; set; }
    [ObservableProperty] public partial DeepSeekOcrMode Mode { get; set; }

    // 输入框下方展示的实际请求地址，与识别时使用的是同一套补全规则
    public string FinalUrl => DeepSeekOcrUrl.Build(Url);

    partial void OnUrlChanged(string value) => OnPropertyChanged(nameof(FinalUrl));

    [RelayCommand]
    public async Task ValidateAsync()
    {
        try
        {
            // 复用真实识别管线；接口没有免费的探测方式，测试图片会按 1 页计费
            await _main.ValidateApiAsync(CreateTestImage());

            ValidateResult = _context.GetTranslation("ValidationSuccess");
        }
        catch (Exception ex)
        {
            ValidateResult = _context.GetTranslation("ValidationFailure");
            _context.Logger.LogError(ex, _context.GetTranslation("ValidationFailure"));
        }
    }

    /// <summary>
    /// 生成一张带文字的小图用于验证，避免随插件附带额外的图片文件
    /// </summary>
    private static byte[] CreateTestImage()
    {
        const int width = 320;
        const int height = 96;

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

            var text = new FormattedText(
                "Hello DeepSeek",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                32,
                Brushes.Black,
                1.0);
            drawing.DrawText(text, new Point((width - text.Width) / 2, (height - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public void Dispose() => PropertyChanged -= OnPropertyChanged;
}
