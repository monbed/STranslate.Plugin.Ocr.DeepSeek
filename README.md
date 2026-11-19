# STranslate.Plugin.Ocr.DeepSeek

适用于 [STranslate](https://github.com/STranslate/STranslate) 的 DeepSeek OCR 插件，通过 DeepSeek 的 [OCR 接口](https://api-docs.deepseek.com/zh-cn/guides/ocr) 识别截图和图片中的文字。

## 功能

- 截图 OCR、静默 OCR 和 OCR 窗口中的文字识别
- 两种识别模式：文字识别、版面识别
- 版面识别模式返回文字坐标，可作为图片翻译的 OCR 服务
- 表格按行输出为纯文本，单元格之间用制表符分隔
- 接口地址自动补全，支持官方地址和第三方中转地址
- 服务繁忙（429 / 503）时自动重试
- 识别结果因长度上限被截断时给出提示
- 界面语言：简体中文、繁體中文、English、日本語、한국어、Türkçe、Русский、Українська、فارسی

## 安装

1. 在 [Releases](https://github.com/monbed/STranslate.Plugin.Ocr.DeepSeek/releases) 页面下载最新的 `.spkg` 插件包
2. 打开 STranslate，进入 设置 > 插件，将下载的文件拖入窗口即可安装

需要 STranslate 2.0.10 或更高版本。

## 配置

1. 在 STranslate 中添加 DeepSeek OCR 服务
2. 填写 API 密钥，可在 [DeepSeek 开放平台](https://platform.deepseek.com/api_keys) 创建
3. 点击“验证”确认配置可用

“验证”会发送一张测试图片并完成一次真实识别，按识别 1 页计费。

### 接口地址

| 填写内容 | 实际请求地址 |
| --- | --- |
| 留空 | `https://api.deepseek.com/v1/tools/ocr` |
| `https://api.deepseek.com` | `https://api.deepseek.com/v1/tools/ocr` |
| `https://example.com/v1` | `https://example.com/v1/tools/ocr` |
| `https://example.com/v1/tools/ocr` | 原样使用 |
| `https://example.com/api/ocr#` | `https://example.com/api/ocr` |

- 地址不以 `/tools/ocr` 结尾时会自动补全
- 以 `#` 结尾表示按填写的地址原样请求，不做补全
- 输入框下方会显示实际请求地址

### 识别模式

对应接口的 `purpose` 参数：

| 选项 | `purpose` | 识别图片时的输出 |
| --- | --- | --- |
| `TextRecognition`（文字识别，默认） | `text_recognition` | Markdown 文本，同一段落的多行合并为一行，标题带 `#` 前缀，不含坐标 |
| `RichLayoutRecognition`（版面识别） | `rich_layout_recognition` | 按文本块输出，保留原有换行，每个文本块带坐标 |

用作图片翻译的 OCR 服务时需要选择 `RichLayoutRecognition`：只有这个模式返回坐标，选择 `TextRecognition` 时本服务不会出现在图片翻译的 OCR 列表里。

### 代理

OCR 接口要求以 `multipart/form-data` 上传图片，STranslate 提供给插件的网络接口不支持这种格式，所以插件自行发送请求。请求使用系统代理，STranslate 设置里单独配置的代理对本插件不生效。

## 计费与限制

以 [DeepSeek 文档](https://api-docs.deepseek.com/zh-cn/guides/ocr) 为准：

- 按识别成功的页数计费，一张图片为 1 页
- 关闭窗口或取消识别不会中止服务端已经开始的识别，完成后照常计费
- 每个账号最多同时进行 15 个识别

## 从源码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。

```powershell
dotnet build .\STranslate.Plugin.Ocr.DeepSeek\STranslate.Plugin.Ocr.DeepSeek.csproj --configuration Release
```

构建完成后，插件包位于 `.artifacts/plugins` 目录。

## 贡献

欢迎提交 Issue 和 Pull Request。

## 相关链接

- [STranslate](https://github.com/STranslate/STranslate)
- [DeepSeek OCR 指南](https://api-docs.deepseek.com/zh-cn/guides/ocr)
- [DeepSeek OCR API 参考](https://api-docs.deepseek.com/zh-cn/api/ocr)
- [DeepSeek 开放平台](https://platform.deepseek.com/)

## 许可证

[MIT](LICENSE)
