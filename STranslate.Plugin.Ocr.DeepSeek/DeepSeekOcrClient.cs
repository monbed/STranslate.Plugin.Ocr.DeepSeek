using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace STranslate.Plugin.Ocr.DeepSeek;

/// <summary>
/// 单页识别结果，对应响应中的 pages[]
/// </summary>
internal sealed record DeepSeekOcrPage(int Index, string Text, string FinishReason);

/// <summary>
/// 接口返回的可预期失败（HTTP 错误状态，或 200 响应体中的 error 对象）
/// </summary>
internal sealed class DeepSeekOcrException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;

    /// <summary>
    /// 429（同时识别数超限）与 503（服务繁忙）发生在识别开始之前，不计费，可以重试
    /// </summary>
    public bool IsTransient => StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
}

/// <summary>
/// DeepSeek OCR 接口客户端，文档见 https://api-docs.deepseek.com/zh-cn/guides/ocr
/// </summary>
internal static class DeepSeekOcrClient
{
    internal const string FinishReasonLength = "length";

    // 接口总时长上限为 180 秒（含上传、识别与返回结果），这里留出少量余量
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(200);

    // 接口要求 multipart/form-data，而宿主 IHttpService 只提供 JSON 与 urlencoded 表单，
    // 所以这里自带 HttpClient；默认沿用系统代理
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
    };

    /// <summary>
    /// 识别一张图片，返回按页序排列的结果
    /// </summary>
    /// <param name="onRetry">重试前回调，参数为（即将进行的尝试序号，最大尝试次数）</param>
    internal static async Task<IReadOnlyList<DeepSeekOcrPage>> RecognizeAsync(
        string url,
        string apiKey,
        byte[] image,
        string purpose,
        int maxRetries,
        int retryDelayMilliseconds,
        Action<int, int>? onRetry,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(0, maxRetries) + 1;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SendAsync(url, apiKey, image, purpose, cancellationToken).ConfigureAwait(false);
            }
            catch (DeepSeekOcrException ex) when (ex.IsTransient && attempt < maxAttempts)
            {
                onRetry?.Invoke(attempt + 1, maxAttempts);
                await Task.Delay(Math.Max(0, retryDelayMilliseconds), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<IReadOnlyList<DeepSeekOcrPage>> SendAsync(
        string url,
        string apiKey,
        byte[] image,
        string purpose,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(RequestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey.Trim());
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Content = BuildContent(image, purpose);

        try
        {
            using var response = await Http.SendAsync(request, linkedCts.Token).ConfigureAwait(false);

            // 识别过程中服务端持续发送换行符保活，它们是 JSON 的前导空白，整个响应体可直接解析
            var body = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
            return ParseResponse(body, response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"DeepSeek OCR did not respond within {(int)RequestTimeout.TotalSeconds} seconds.");
        }
    }

    /// <summary>
    /// 构造请求体：只包含 file 与 purpose 两个字段，传入其他字段接口会返回 400
    /// </summary>
    internal static MultipartFormDataContent BuildContent(byte[] image, string purpose)
    {
        var boundary = "----STranslateDeepSeekOcr" + Guid.NewGuid().ToString("N");
        var form = new MultipartFormDataContent(boundary);

        // .NET 默认给 boundary 加引号，个别网关不识别，改成与 curl 一致的不带引号形式
        form.Headers.Remove("Content-Type");
        form.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=" + boundary);

        // 字段名与文件名显式加引号，同时避免 .NET 额外生成 filename* 参数
        var purposePart = new StringContent(purpose);
        purposePart.Headers.ContentType = null;
        purposePart.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"purpose\"",
        };
        form.Add(purposePart);

        var (fileName, mediaType) = DetectImage(image);
        var filePart = new ByteArrayContent(image);
        filePart.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        filePart.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"file\"",
            FileName = $"\"{fileName}\"",
        };
        form.Add(filePart);

        return form;
    }

    /// <summary>
    /// 按文件头判断图片格式。接口按实际内容识别格式，文件名只需带上即可
    /// </summary>
    internal static (string FileName, string MediaType) DetectImage(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return ("image.png", "image/png");

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return ("image.jpg", "image/jpeg");

        if (data.Length >= 2 && data[0] == 0x42 && data[1] == 0x4D)
            return ("image.bmp", "image/bmp");

        if (data.Length >= 4 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38)
            return ("image.gif", "image/gif");

        if (data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
            data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
            return ("image.webp", "image/webp");

        if (data.Length >= 4 &&
            ((data[0] == 0x49 && data[1] == 0x49 && data[2] == 0x2A && data[3] == 0x00) ||
             (data[0] == 0x4D && data[1] == 0x4D && data[2] == 0x00 && data[3] == 0x2A)))
            return ("image.tiff", "image/tiff");

        return ("image.png", "application/octet-stream");
    }

    /// <summary>
    /// 解析响应体。失败有两种形式：上传阶段以 HTTP 错误状态返回；
    /// 识别开始后状态码已是 200，失败时响应体是 error 对象而不是识别结果
    /// </summary>
    internal static IReadOnlyList<DeepSeekOcrPage> ParseResponse(string body, HttpStatusCode statusCode)
    {
        var success = (int)statusCode is >= 200 and < 300;

        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            // 非 JSON 响应（如网关返回的 HTML 错误页）走下面的兜底
        }

        using (document)
        {
            if (document?.RootElement is { ValueKind: JsonValueKind.Object } root)
            {
                if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                    throw new DeepSeekOcrException(FormatError(ReadErrorMessage(error), statusCode, success), statusCode);

                if (success && root.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array)
                    return ReadPages(pages);
            }
        }

        throw new DeepSeekOcrException(
            success ? $"Unexpected response: {Truncate(body)}" : FormatError(Truncate(body), statusCode, false),
            statusCode);
    }

    private static List<DeepSeekOcrPage> ReadPages(JsonElement pages)
    {
        var result = new List<DeepSeekOcrPage>();
        var position = 0;

        foreach (var page in pages.EnumerateArray())
        {
            var fallbackIndex = position++;
            if (page.ValueKind != JsonValueKind.Object)
                continue;

            var index = page.TryGetProperty("index", out var indexElement) &&
                        indexElement.ValueKind == JsonValueKind.Number &&
                        indexElement.TryGetInt32(out var parsedIndex)
                ? parsedIndex
                : fallbackIndex;

            result.Add(new DeepSeekOcrPage(index, ReadString(page, "text"), ReadString(page, "finish_reason")));
        }

        // 文档约定按页序排列，这里再按页码排一次以防万一（稳定排序）
        return [.. result.OrderBy(page => page.Index)];
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string ReadErrorMessage(JsonElement error) => error.ValueKind switch
    {
        JsonValueKind.String => error.GetString() ?? string.Empty,
        JsonValueKind.Object when ReadString(error, "message") is { Length: > 0 } message => message,
        _ => error.GetRawText(),
    };

    private static string FormatError(string message, HttpStatusCode statusCode, bool success)
    {
        message = message.Trim();
        if (success)
            return message;

        var status = $"HTTP {(int)statusCode} ({statusCode})";
        return message.Length == 0 ? status : $"{message} ({status})";
    }

    private static string Truncate(string text)
    {
        const int maxLength = 300;

        text = text.Trim();
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
