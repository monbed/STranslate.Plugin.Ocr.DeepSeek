namespace STranslate.Plugin.Ocr.DeepSeek;

/// <summary>
/// 由“接口”设置推导实际请求地址。
/// 规则：留空使用官方地址；不以 /tools/ocr 结尾时自动补全；以 # 结尾则原样使用。
/// </summary>
internal static class DeepSeekOcrUrl
{
    internal const string DefaultUrl = "https://api.deepseek.com";

    private const string VersionPath = "/v1";
    private const string OcrPath = "/tools/ocr";

    internal static string Build(string? url)
    {
        var input = url?.Trim() ?? string.Empty;

        // 以 # 结尾：强制使用填写的地址，不做补全
        var forced = input.EndsWith('#');
        if (forced)
            input = input.TrimEnd('#').TrimEnd();

        // 留空（或只填了 #）时回退到官方地址，并照常补全
        if (input.Length == 0)
        {
            input = DefaultUrl;
            forced = false;
        }

        try
        {
            var builder = new UriBuilder(input);

            if (!forced)
            {
                var path = builder.Path.TrimEnd('/');
                if (!path.EndsWith(OcrPath, StringComparison.OrdinalIgnoreCase))
                {
                    // 官方路径为 /v1/tools/ocr：已带 /v1 时只补后半段
                    builder.Path = path.EndsWith(VersionPath, StringComparison.OrdinalIgnoreCase)
                        ? path + OcrPath
                        : path + VersionPath + OcrPath;
                }
            }

            return builder.Uri.ToString();
        }
        catch
        {
            // 无法解析的地址原样返回，由请求阶段报错
            return input;
        }
    }
}
