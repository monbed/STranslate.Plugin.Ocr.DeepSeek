using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace STranslate.Plugin.Ocr.DeepSeek;

/// <summary>
/// 一个文本块。Box 为按页面宽高归一化到 0–1000 的 [x1, y1, x2, y2]（左上角与右下角），没有坐标时为 null
/// </summary>
internal sealed record DeepSeekOcrBlock(string Text, float[]? Box);

/// <summary>
/// 解析单页的模型原始输出。
/// 版面识别（rich_layout_recognition）每个块以定位标记开头，标记里是块类型，正文跟在后面，例如
/// <c>&lt;｜ref｜&gt;title&lt;｜/ref｜&gt;&lt;｜box｜&gt;[[52, 40, 948, 96]]&lt;｜/box｜&gt; 标题文字</c>；
/// 文字识别（text_recognition）对图片返回不带标记的 Markdown 文本。两种模式下表格都是 HTML。
/// </summary>
internal static class DeepSeekOcrParser
{
    private const float CoordinateMax = 1000f;

    // 分隔符同时兼容全角竖线（接口实际返回）与半角竖线，坐标标记同时兼容 box 与 det
    private static readonly Regex MarkerRegex = new(
        @"<[｜|]ref[｜|]>.*?<[｜|]/ref[｜|]>\s*<[｜|](?:box|det)[｜|]>(?<box>.*?)<[｜|]/(?:box|det)[｜|]>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex QuadRegex = new(
        @"\[\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\]",
        RegexOptions.Compiled);

    // 未成对出现的残留标记，不应出现在最终文本里
    private static readonly Regex StrayTokenRegex = new(
        @"<[｜|]/?(?:ref|box|det|grounding)[｜|]>|<｜[^<>｜\s]{1,40}｜>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TableRegex = new(
        @"<table\b[^>]*>(?<body>.*?)</table>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex RowRegex = new(
        @"<tr\b[^>]*>(?<row>.*?)</tr>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex CellRegex = new(
        @"<t[dh]\b[^>]*>(?<cell>.*?)</t[dh]>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex TagRegex = new(@"<[^<>]+>", RegexOptions.Compiled);

    internal static List<DeepSeekOcrBlock> Parse(string? pageText)
    {
        var text = FlattenTables((pageText ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n'));
        var blocks = new List<DeepSeekOcrBlock>();

        var matches = MarkerRegex.Matches(text);
        if (matches.Count == 0)
        {
            AddLines(blocks, text);
            return blocks;
        }

        // 第一个标记之前的内容没有坐标，按普通文本保留
        AddLines(blocks, text[..matches[0].Index]);

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var start = match.Index + match.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;

            // 正文是标记之后、下一个标记之前的内容；图片等没有正文的块跳过
            var content = Clean(text[start..end]);
            if (content.Length > 0)
                blocks.Add(new DeepSeekOcrBlock(content, ParseBox(match.Groups["box"].Value)));
        }

        return blocks;
    }

    private static void AddLines(List<DeepSeekOcrBlock> blocks, string text)
    {
        foreach (var line in Clean(text).Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (!string.IsNullOrWhiteSpace(trimmed))
                blocks.Add(new DeepSeekOcrBlock(trimmed, null));
        }
    }

    private static string Clean(string text) => StrayTokenRegex.Replace(text, string.Empty).Trim();

    /// <summary>
    /// 把 HTML 表格转成纯文本：一行一条，单元格之间用制表符分隔
    /// </summary>
    private static string FlattenTables(string text) => TableRegex.Replace(text, table =>
    {
        var rows = RowRegex.Matches(table.Groups["body"].Value)
            .Select(row => string.Join('\t', CellRegex.Matches(row.Groups["row"].Value)
                .Select(cell => WebUtility.HtmlDecode(TagRegex.Replace(cell.Groups["cell"].Value, " ")).Trim())))
            .Where(row => row.Trim().Length > 0)
            .ToList();

        // 结构不符合预期的表格保持原样
        return rows.Count == 0 ? table.Value : "\n" + string.Join('\n', rows) + "\n";
    });

    /// <summary>
    /// 解析 [[x1, y1, x2, y2]]；出现多个框时取外接框。坐标无效时返回 null
    /// </summary>
    private static float[]? ParseBox(string text)
    {
        var quads = QuadRegex.Matches(text);
        if (quads.Count == 0)
            return null;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

        foreach (Match quad in quads)
        {
            var x1 = ParseCoordinate(quad.Groups[1].Value);
            var y1 = ParseCoordinate(quad.Groups[2].Value);
            var x2 = ParseCoordinate(quad.Groups[3].Value);
            var y2 = ParseCoordinate(quad.Groups[4].Value);

            minX = Math.Min(minX, Math.Min(x1, x2));
            minY = Math.Min(minY, Math.Min(y1, y2));
            maxX = Math.Max(maxX, Math.Max(x1, x2));
            maxY = Math.Max(maxY, Math.Max(y1, y2));
        }

        return maxX > minX && maxY > minY ? [minX, minY, maxX, maxY] : null;
    }

    private static float ParseCoordinate(string value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? Math.Clamp(number, 0f, CoordinateMax)
            : 0f;
}
