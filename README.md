<div align="center">

<img src="src/LiveCaptions-Translator.ico" width="128" height="128" alt="图标"/>

# 课堂同传助手

### *听外语课：实时字幕 + 中文翻译 + 分节小结 + 耳机播报*

[![Build](https://github.com/YingQiu0871/LiveCaptions-Translator/actions/workflows/dotnet-build.yml/badge.svg)](https://github.com/YingQiu0871/LiveCaptions-Translator/actions/workflows/dotnet-build.yml)
[![Windows 11](https://img.shields.io/badge/platform-Windows11-blue?logo=windows11&color=1E9BFA)](https://www.microsoft.com/windows/windows-11)
[![License](https://img.shields.io/github/license/SakiRinn/LiveCaptions-Translator)](LICENSE)

基于 [SakiRinn/LiveCaptions-Translator](https://github.com/SakiRinn/LiveCaptions-Translator) 修改 · 原版说明：[English](README.upstream.md) | [中文](README.upstream_zh-CN.md)

</div>

## 这是什么

市面上的同声传译工具大多很贵，而且不是为上课设计的。这个工具用 Windows 11 自带的"实时辅助字幕"识别老师的话，再接上 DeepSeek 等大模型，帮你：

- **看懂**：实时显示外语原文和中文译文。
- **跟上**：每讲完一节（一页课件、一个要点），自动生成几句中文小结，按时间线排好。
- **不用盯屏幕**：用蓝牙耳机听小结，也可以连同每句译文一起听。
- **课后复习**：一键导出当天的 Markdown 笔记，包含时间线、每节小结、原文和译文。

语音识别在本机完成，不需要另外装模型。翻译和总结按 API 用量计费，用 DeepSeek 的话，一节课大约几毛到一两块钱（估算）。

## 功能

| 功能 | 说明 |
|---|---|
| 实时字幕与翻译 | 原版功能。翻译可选 DeepSeek、通义千问等 OpenAI 兼容接口，也可以用免费的 Google 翻译 |
| 分节总结 | 上传了课件就按老师讲到的页码分节；没有课件就按话题变化分节；自动识别失败时才按固定时间分节；同一话题讲太久也会强制分节。随时按 **Ctrl+Alt+S** 手动结束本节 |
| 课件 | 以 PDF 为主，也支持 .pptx。扫描版或图片页面自动用 Windows 自带的 OCR 识别，小结会参考当前页的文字纠正术语 |
| 输入源 | 在软件里选"电脑播放的声音"（网课、视频）或"麦克风"（线下教室），并选择用哪个麦克风 |
| 耳机播报 | 只读小结，或小结加每句译文；可选输出设备、语音、语速、音量。跟不上时自动跳过旧译文。**Ctrl+Alt+M** 静音 |
| 时间线 | Timeline 页按时间列出每一节的小结，可以查看往日记录、导出笔记 |

## 系统要求

- **Windows 11 22H2 或更高版本**（需要"实时辅助字幕"）。
- 一个大模型 API Key，推荐 [DeepSeek](https://platform.deepseek.com/)。只用 Google 翻译的话，也可以先不填，但就没有小节总结了。

## 下载

到 [Releases](https://github.com/YingQiu0871/LiveCaptions-Translator/releases/latest) 下载最新版本：

- `LiveCaptionsTranslator-win-x64-setup.msi`：**推荐**。Windows 标准安装程序，双击安装，安装时可以选择安装位置。装好后开始菜单和桌面都有“上课同传助手”的快捷方式；以后装新版会自动替换旧版，卸载在“设置 > 应用”里。
- `LiveCaptionsTranslator-win-x64-withruntime.exe`：免安装版，自带运行库，下载后直接运行。
- `LiveCaptionsTranslator-win-x64.exe`：免安装版，体积小，需要先安装 [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)。
- ARM 电脑选文件名带 `arm64` 的版本。

设置（`setting.json`）和历史记录（`translation_history.db`）保存在程序所在的文件夹。用免安装版时，建议把 exe 单独放进一个文件夹。卸载时这两个文件会保留。

发布新版本：在 Actions 里手动运行 "CI/CD Pipeline"，勾选 "Publish a GitHub Release from this build"。

## 第一次使用

### 1. 准备 Windows 实时辅助字幕

1. 按 **Win + Ctrl + L** 打开实时辅助字幕。第一次打开时，按提示同意并下载语音识别文件。
2. 在 **设置 → 时间和语言 → 语言和区域** 里添加课程语言（例如英语），并安装它的"语音识别"组件。扫描版课件需要 OCR 的话，还要有"光学字符识别"组件（一般随语言包一起装好）。
3. 想听中文播报，需要安装中文语言包里的"文本到语音"组件。
4. 在实时辅助字幕里点 ⚙️，选择 **位置（Position）→ 在屏幕上叠加（Overlaid on screen）**，否则隐藏它之后屏幕上会出现显示错误（原版的要求）。然后把它关掉。

### 2. 打开软件，进入"课堂设置"页

左侧导航的第二个图标就是"课堂设置"，所有设置都在这一页：

1. **⓪ 输入源**：网课、视频选"电脑播放的声音"；线下课选"麦克风"，再选用哪个麦克风。**不要选蓝牙耳机上的麦克风**，否则耳机会切换到通话模式，音质变差。
2. **① 模型与 API**：选服务商（例如 DeepSeek），粘贴 API Key，然后点"测试连接"。
3. **② 课件**（可选）：上传本节课的 PDF。
4. **③ 分节总结**：一般保持默认。
5. **④ 耳机播报**：选输出设备和读出内容，点"试听"确认耳机里有声音。

### 3. 上课

- 主窗口显示实时原文和译文；**Timeline** 页显示每一节的小结。
- **Ctrl+Alt+S**：立即结束本节并生成小结（老师换话题而工具没跟上时用）。
- **Ctrl+Alt+M**：打开 / 关闭耳机播报（标题栏上也有开关）。
- 下课后在 Timeline 页右上角导出 Markdown 笔记。

## 建议的设置

| 场景 | 输入源 | 播报输出设备 | Windows 默认输出 |
|---|---|---|---|
| 线下教室 | 麦克风（电脑自带或外接麦克风，坐近一点） | 蓝牙耳机 | 电脑扬声器 |
| 网课 / 视频 | 电脑播放的声音 | 蓝牙耳机 | 你听课用的设备 |

实时辅助字幕会听 Windows 默认输出设备里的所有声音。如果播报也从这个设备放出来，就会被当成老师的话识别进字幕。所以播报走默认设备时，软件会在播报期间暂停接收字幕。线下课按上表设置，播报就不会打断字幕。

## 常见问题

- **没有小结？** 先在"课堂设置"里点"测试连接"。小结始终使用"① 模型与 API"里的模型，就算翻译选了 Google 也一样。
- **自动切换麦克风没生效？** 实时辅助字幕没有提供公开接口，软件是模拟点它的菜单实现的。如果失败，会弹出它的设置菜单，请在"首选项"里手动勾选或取消"包含麦克风音频"。
- **课件读不出字？** 在 OCR 语言里选课件的语言，或者打开"所有页面都用 OCR"。旧版 .ppt 请先另存为 PDF。
- **删除历史记录** 会把 Timeline 上的小节一起删掉。
- 本版本关闭了原版的"有新版本"提示，以免误装原版、丢掉这些功能。

更详细的说明见 [docs/lecture-assistant.zh-CN.md](docs/lecture-assistant.zh-CN.md)。

## 致谢与许可

本项目基于 [SakiRinn/LiveCaptions-Translator](https://github.com/SakiRinn/LiveCaptions-Translator) 修改，感谢原作者和所有贡献者。许可证与原项目相同，为 [Apache-2.0](LICENSE)。

使用的第三方库：[WPF-UI](https://github.com/lepoco/wpfui)、[PdfPig](https://github.com/UglyToad/PdfPig)、[NAudio](https://github.com/naudio/NAudio)、System.Speech、Microsoft.Data.Sqlite、CsvHelper。

请遵守学校和老师关于课堂录音、转写的规定。
