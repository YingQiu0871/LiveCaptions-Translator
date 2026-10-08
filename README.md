<div align="center">

<img src="src/LiveCaptions-Translator.ico" width="128" height="128" alt="图标"/>

# 课堂同传助手

### *听外语课：实时转录 + 中文翻译 + 分节小结 + 耳机同传 + 课后笔记*

[![Build](https://github.com/YingQiu0871/LiveCaptions-Translator/actions/workflows/dotnet-build.yml/badge.svg)](https://github.com/YingQiu0871/LiveCaptions-Translator/actions/workflows/dotnet-build.yml)
[![Windows 11](https://img.shields.io/badge/platform-Windows11-blue?logo=windows11&color=1E9BFA)](https://www.microsoft.com/windows/windows-11)
[![License](https://img.shields.io/github/license/SakiRinn/LiveCaptions-Translator)](LICENSE)

[SakiRinn/LiveCaptions-Translator](https://github.com/SakiRinn/LiveCaptions-Translator) 的课堂同传特化分支 · 原版说明：[English](README.upstream.md) | [中文](README.upstream_zh-CN.md)

</div>

> [!NOTE]
> **这是原项目的课堂同传特化分支，不是官方版本。**
> 本仓库从 [SakiRinn/LiveCaptions-Translator](https://github.com/SakiRinn/LiveCaptions-Translator) 分出，专门针对“听外语课”改造：界面只有中文、只支持 Windows 11，加入了云端语音识别、分节总结、按课程保存和课后笔记，也去掉了原版的一些通用设置。
> 如果想给视频、会议、直播加实时翻译字幕，或者需要英文界面、更多翻译接口，推荐使用[原项目](https://github.com/SakiRinn/LiveCaptions-Translator)。
> 本分支中与课堂无关的几处通用修复，已整理成独立的小改动提交给原项目，供原作者参考（[#286](https://github.com/SakiRinn/LiveCaptions-Translator/pull/286)–[#290](https://github.com/SakiRinn/LiveCaptions-Translator/pull/290)）。本分支的问题请在本仓库反馈，以免给原作者添麻烦。
>
> *A classroom-interpretation fork of [SakiRinn/LiveCaptions-Translator](https://github.com/SakiRinn/LiveCaptions-Translator) (Chinese UI only). Many thanks to SakiRinn and all contributors of the original project, which is the one to use for general-purpose live caption translation.*

## 这是什么

市面上的同声传译工具大多很贵，而且不是为上课设计的。这个工具把老师的话转成文字，再接上 DeepSeek、通义千问等大模型，帮你：

- **看懂**：实时显示外语原文和中文译文，每说完几句再由大模型结合上下文整段精修。
- **跟上**：每讲完一节（一页课件、一个话题），自动生成中文小结。
- **不用盯屏幕**：用蓝牙耳机听小结，或者开“实时同传”，每句中文译文一出来就读给你听。
- **课后复习**：每节课单独保存，按“课程 → 每节课”整理。每节课自动存成 Markdown，含小节总结、原文和译文，还可以自动生成一份结合课件的笔记初稿。

语音识别有两种：Windows 11 自带的“实时辅助字幕”（免费），或阿里云实时语音识别（更准，按时长收费）。翻译和总结按大模型 API 用量计费，用 DeepSeek 的话，一节课大约几毛到一两块钱（估算）。

## 功能

| 功能 | 说明 |
|---|---|
| 语音识别 | **Windows 系统实时字幕**：免费，在本机识别。**阿里云实时语音识别**（paraformer-realtime-v2）：专业词汇更准，声音小时自动放大，按语义断句 |
| 实时翻译 | DeepSeek、通义千问等 OpenAI 兼容接口，也可以用免费的 Google 翻译。“中外对照”时译文上方保留原文；也可以“只转录，不翻译” |
| 段落精修 | 每说完几句，大模型结合上下文、课程学科和当前课件页，纠正听错的词，再整段翻译，替换逐句的即时翻译 |
| 课程学科 | 填写学科（例如“基因治疗”），翻译、精修和小结都会按这个领域的术语来 |
| 术语表 | 每行一个专业词，可写“术语 = 译法”，也能从课件一键提取。阿里云识别会把它当热词提高识别率；翻译接口选 DeepSeek / 通义千问时，翻译、精修、小结和笔记都按这里的译法统一（Google 翻译只有小结和笔记会用） |
| 分节总结 | 上传了课件就按老师讲到的页码分节；没有课件就按话题变化分节；话题判断失败时按固定时间分节；同一话题讲太久也会强制分节。**Ctrl+Alt+S** 手动结束本节。字幕页右侧会显示总结进度和出错原因 |
| 课件 | 以 PDF 为主，也支持 .pptx。扫描版或图片页面自动用 Windows 自带的 OCR 识别 |
| 耳机播报 | 四种模式：关闭 / 只读小节总结 / 小节总结 + 译文 / **实时同传**（每句中文译文马上读，跟不上时跳过旧句子）。可选输出设备、语音、语速、音量。**Ctrl+Alt+M** 开关 |
| 按课保存 | 每次“开始”到“停止”算一节课。停止后弹框填写本节课名称、选择课程，自动保存为 `保存位置\课程名\本节课名称.md` |
| 笔记初稿 | （可关闭）下课后结合小节总结、转录和课件，生成按课件结构整理的笔记，标出“【课上补充】”的内容，附复习题，保存为 `本节课名称 笔记初稿.md` |
| 历史 | 三层浏览：课程文件夹 → 每节课 → 详细内容（每节总结 + 原文 + 译文）。可重命名、移到其他课程、删除、重新生成笔记初稿 |
| 时间线 | 选课程和某一节课，查看这节课的小节总结，并导出这节课的笔记 |
| 悬浮字幕 | 标题栏的“悬浮字幕”把译文显示在一个可缩放、置顶的小窗里 |

## 系统要求

- **Windows 11 22H2 或更高版本**。
- 一个大模型 API Key，推荐 [DeepSeek](https://platform.deepseek.com/)，也可以用[阿里云百炼](https://bailian.console.aliyun.com/)的通义千问。只用 Google 翻译的话可以先不填，但就没有小结、段落精修和笔记初稿了。
- 用阿里云实时语音识别的话，需要阿里云百炼的 API Key（和通义千问可以是同一个 Key）。

## 下载

到 [Releases](https://github.com/YingQiu0871/LiveCaptions-Translator/releases/latest) 下载最新版本：

- `LiveCaptionsTranslator-win-x64-setup.msi`：**推荐**。Windows 标准安装程序，双击安装，可以选择安装位置。装好后开始菜单和桌面都有“课堂同传助手”的快捷方式；以后装新版会自动替换旧版，卸载在“设置 > 应用”里。
- `LiveCaptionsTranslator-win-x64-withruntime.exe`：免安装版，自带运行库，下载后直接运行。
- `LiveCaptionsTranslator-win-x64.exe`：免安装版，体积小，需要先安装 [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)。
- ARM 电脑选文件名带 `arm64` 的版本。

数据保存位置：

- 设置（`setting.json`）和历史数据库（`translation_history.db`，含课程、每节课、小结）在 `%LOCALAPPDATA%\LiveCaptionsTranslator`，装新版、卸载都不会动它们。旧版本放在程序文件夹里的设置，第一次启动时会自动搬过去。
- 每节课的 Markdown 文件和笔记初稿在“设置 ⑥”选的文件夹里，默认是 `文档\课堂同传助手`。

发布新版本：在 Actions 里手动运行 "CI/CD Pipeline"，勾选 "Publish a GitHub Release from this build"。

## 第一次使用

### 1. 准备语音识别

**用 Windows 系统实时字幕（免费）：**

1. 按 **Win + Ctrl + L** 打开实时辅助字幕。第一次打开时，按提示同意并下载语音识别文件。
2. 在 **设置 → 时间和语言 → 语言和区域** 里添加课程语言（例如英语），并安装它的“语音识别”组件。
3. 在实时辅助字幕的 ⚙️ 里**关掉它自带的“翻译”**，否则送给本工具的已经是中文，会漏句，也看不到外语原文。

系统字幕在窗口最小化或移出屏幕后就不再出字，所以上课时它会以一条窄条的形式贴在任务栏上方。可以把它拖到别处，下次会留在你放的位置。

**用阿里云实时语音识别：** 在阿里云百炼开通后创建 API Key，填到“设置 ⓪”里，点“测试”。这时系统字幕用不上，会自动最小化。

另外：扫描版课件需要 Windows 的“光学字符识别”组件（一般随语言包装好）；想听中文播报，需要中文语言包里的“文本到语音”组件。

### 2. 打开软件，进入“设置”页

左侧导航的第二个图标就是“设置”，所有设置都在这一页：

1. **⓪ 输入源**：选语音识别方式（系统实时字幕 / 阿里云）。网课、视频选“电脑播放的声音”；线下课选“麦克风”，再选用哪个麦克风。**不要选蓝牙耳机上的麦克风**，否则耳机会切换到通话模式，音质变差。
2. **① 模型与 API**：选服务商（DeepSeek / 通义千问 / 其他兼容接口），粘贴 API Key，点“测试连接”。小结、段落精修和笔记初稿都用这里的模型。
3. **② 课件**（可选）：上传本节课的 PDF 或 .pptx。
4. **③ 分节总结**：一般保持默认。
5. **④ 耳机播报**：选读出内容和输出设备，点“试听”确认耳机里有声音。
6. **⑤ 翻译**：填这门课的学科和术语表（上传课件后可以点“从课件提取术语”，再检查一下译法）；翻译接口默认“DeepSeek / 通义千问”（用 ① 的 Key），也可以换成免费的 Google 翻译；按需打开“段落精修”“中外对照”或“只转录，不翻译”。
7. **⑥ 转录保存**：选保存位置，决定是否“下课后自动生成笔记初稿”。

### 3. 上课

1. 点标题栏的 **开始**。字幕页左边是原文和译文，右边是本节课的小节总结和总结进度。
2. 上课中：
   - **Ctrl+Alt+S**：立即结束本节并生成小结（老师换话题而工具没跟上时用）。
   - **Ctrl+Alt+M**：打开 / 关闭耳机播报（标题栏上也有开关）。
3. 下课点 **停止**，在弹框里填本节课名称、选择课程（可以直接输入新课程）。软件会总结最后一节，保存 Markdown 文件；打开了笔记初稿的话，再过半分钟到一两分钟会生成笔记初稿。
4. 下一节课再点“开始”，就是新的一节，不会和上一节混在一起，默认放进同一门课。

### 4. 课后

- **历史** 页：课程文件夹 → 每节课 → 详细内容。右键可以重命名、移到其他课程、删除；详细内容页可以“打开文件位置”或“生成笔记初稿”。以前版本的记录按天放在“以前的记录”里；“全部句子”保留了原来的总表。
- **时间线** 页：选课程和某一节课，看这节课的小节总结，右上角导出这节课的笔记。

## 建议的设置

| 场景 | 输入源 | 播报输出设备 | Windows 默认输出 |
|---|---|---|---|
| 线下教室 | 麦克风（电脑自带或外接麦克风，坐近一点） | 蓝牙耳机 | 电脑扬声器 |
| 网课 / 视频 | 电脑播放的声音 | 蓝牙耳机 | 你听课用的设备 |

识别听的是 Windows 默认输出设备（或麦克风）里的声音。如果播报也从这个设备放出来，就会被当成老师的话再识别一遍，所以播报期间软件会暂停识别。按上表把播报放到蓝牙耳机，就不会互相打断。

## 常见问题

- **没有小结？** 看字幕页右侧“小节总结”下面的状态行，出错时会写原因；再到“设置 ①”点“测试连接”。小结始终用 ① 里的模型，就算翻译选了 Google 也一样。
- **漏字多？** 阿里云识别会自动放大小声音；线下课尽量让麦克风离老师近一些。系统实时字幕请确认关掉了它自带的“翻译”。
- **系统字幕的窄条一直在？** 系统字幕不能真正隐藏，否则不出字；可以把它拖到屏幕边上。改用阿里云识别后它会自动最小化。
- **笔记初稿没有结合课件？** 课件只认当时载入的那份，下课点“停止”之前不要移除课件。
- **自动切换麦克风没生效？** 系统实时字幕没有公开接口，软件是模拟点它的菜单实现的。失败时会弹出它的设置菜单，请在“首选项”里手动勾选或取消“包含麦克风音频”。
- **课件读不出字？** 在 OCR 语言里选课件的语言，或者打开“所有页面都用 OCR”。旧版 .ppt 请先另存为 PDF。
- **“全部删除”** 会删掉所有课程里的录音和小结，课程文件夹和已经保存的文件会保留。
- 本版本关闭了原版的“有新版本”提示，以免误装原版、丢掉这些功能。

## 致谢与许可

本项目基于 [SakiRinn/LiveCaptions-Translator](https://github.com/SakiRinn/LiveCaptions-Translator) 修改，感谢原作者和所有贡献者。许可证与原项目相同，为 [Apache-2.0](LICENSE)。

使用的第三方库：[WPF-UI](https://github.com/lepoco/wpfui)、[PdfPig](https://github.com/UglyToad/PdfPig)、[NAudio](https://github.com/naudio/NAudio)、System.Speech、Microsoft.Data.Sqlite、CsvHelper。可选的云服务：阿里云百炼（实时语音识别、通义千问）、DeepSeek。

请遵守学校和老师关于课堂录音、转写的规定。
