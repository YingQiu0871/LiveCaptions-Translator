<div align="center">

<img src="src/LiveCaptions-Translator.ico" width="128" height="128" alt="图标"/>

# 课堂同传助手

### *听外语课：实时转录 + 中文翻译 + 分节小结 + 耳机同传 + 课后笔记*

[![Build](https://github.com/YingQiu0871/LiveCaptions-Translator/actions/workflows/dotnet-build.yml/badge.svg)](https://github.com/YingQiu0871/LiveCaptions-Translator/actions/workflows/dotnet-build.yml)
[![Platform](https://img.shields.io/badge/platform-Windows%2011%20%7C%20macOS%20%7C%20Linux-blue?color=1E9BFA)](#下载)
[![License](https://img.shields.io/github/license/SakiRinn/LiveCaptions-Translator)](LICENSE)

[SakiRinn/LiveCaptions-Translator](https://github.com/SakiRinn/LiveCaptions-Translator) 的课堂同传特化分支 · 原版说明：[English](README.upstream.md) | [中文](README.upstream_zh-CN.md)

</div>

> [!NOTE]
> **这是原项目的课堂同传特化分支，不是官方版本。**
> 本仓库从 [SakiRinn/LiveCaptions-Translator](https://github.com/SakiRinn/LiveCaptions-Translator) 分出，专门针对“听外语课”改造：界面只有中文，支持 Windows 11、macOS 和 Linux，加入了云端语音识别、分节总结、按课程保存和课后笔记，也去掉了原版的一些通用设置。
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

语音识别有两种：Windows 11 自带的“实时辅助字幕”（免费，仅 Windows），或阿里云实时语音识别（更准，按时长收费；macOS 和 Linux 只能用这个）。翻译和总结按大模型 API 用量计费，用 DeepSeek 的话，一节课大约几毛到一两块钱（估算）。

## 功能

| 功能 | 说明 |
|---|---|
| 语音识别 | **Windows 系统实时字幕**：免费，在本机识别。**阿里云实时语音识别**（paraformer-realtime-v2）：专业词汇更准，声音小时自动放大，按语义断句 |
| 实时翻译 | DeepSeek、通义千问等 OpenAI 兼容接口，也可以用免费的 Google 翻译。“中外对照”时译文上方保留原文；也可以“只转录，不翻译” |
| 段落精修 | 每说完几句，大模型结合上下文、课程学科和当前课件页，纠正听错的词，再整段翻译，替换逐句的即时翻译 |
| 课程学科 | 填写学科（例如“基因治疗”），翻译、精修和小结都会按这个领域的术语来 |
| 纯英文模式 | 不翻译，只显示英文原文；小节总结、段落精修（只纠错）和笔记初稿都用英文。没有译文可读，所以耳机只会读英文的小节总结，“实时同传”和“每句译文”两种模式用不上 |
| 术语表 | 每行一个专业词，可写“术语 = 译法”，也能从课件一键提取。阿里云识别会把它当热词提高识别率；翻译接口选 DeepSeek / 通义千问时，翻译、精修、小结和笔记都按这里的译法统一（Google 翻译只有小结和笔记会用） |
| 分节总结 | 上传了课件就按老师讲到的页码分节；没有课件就按话题变化分节；话题判断失败时按固定时间分节；同一话题讲太久也会强制分节。**Ctrl+Alt+S** 手动结束本节。字幕页右侧会显示总结进度和出错原因 |
| 课件 | 以 PDF 为主，也支持 .pptx。PDF 里的扫描版或图片页面自动用 OCR 识别（Windows 用系统自带 OCR；macOS / Linux 见下面“macOS 和 Linux 版”）；.pptx 只读文字和表格，整页是图片的幻灯片读不出字 |
| 耳机播报 | 四种模式：关闭 / 只读小节总结 / 小节总结 + 每句译文 / **实时同传**（每句中文译文马上读，跟不上时跳过旧句子；这个模式不读小结）。开了“段落精修”时，“小节总结 + 每句译文”读的是精修后的整段，不是逐句。可选输出设备（仅 Windows / macOS）、语音、语速、音量。**Ctrl+Alt+M** 开关 |
| 按课保存 | 每次“开始”到“停止”算一节课。停止后弹框填写本节课名称、选择课程，自动保存为 `保存位置\课程名\本节课名称.md` |
| 笔记初稿 | （可关闭）下课后结合小节总结、转录和课件，生成按课件结构整理的笔记，标出“【课上补充】”的内容，附复习题，保存为 `本节课名称 笔记初稿.md` |
| 历史 | 三层浏览：课程文件夹 → 每节课 → 详细内容（每节总结 + 原文 + 译文）。可重命名、移到其他课程、删除、重新生成笔记初稿 |
| 时间线 | 选课程和某一节课，查看这节课的小节总结，可以手动结束本节、上传课件，也能导出这节课的转录 Markdown（小节总结 + 原文 + 译文，和点“停止”时保存的是同一个文件） |
| 悬浮字幕 | 标题栏的字幕图标（鼠标停上去显示“悬浮字幕窗”）把原文和译文显示在一个可缩放、置顶的小窗里。标题栏还有暂停图标（“暂停翻译（只记录原文）”）和图钉图标（“窗口置顶”） |

## 系统要求

- **Windows 11 22H2 或更高版本**，或 **macOS 13 或更高版本**（Apple 芯片、Intel 都有对应安装包），或 **Linux x64**（需要 PulseAudio 或 PipeWire）。macOS 和 Linux 版是新加的，见下面“[macOS 和 Linux 版](#macos-和-linux-版)”。
- 一个大模型 API Key，推荐 [DeepSeek](https://platform.deepseek.com/)，也可以用[阿里云百炼](https://bailian.console.aliyun.com/)的通义千问。只用 Google 翻译的话可以先不填，但就没有小结、段落精修和笔记初稿了。
- 用阿里云实时语音识别的话，需要阿里云百炼的 API Key（和通义千问可以是同一个 Key）。

## 下载

到 [Releases](https://github.com/YingQiu0871/LiveCaptions-Translator/releases/latest) 下载最新版本。

**Windows：**

- `LiveCaptionsTranslator-win-x64-setup.msi`：**推荐**。Windows 标准安装程序，双击安装，可以选择安装位置。装好后开始菜单和桌面都有“课堂同传助手”的快捷方式；以后装新版会自动替换旧版，卸载在“设置 > 应用”里。
- `LiveCaptionsTranslator-win-x64-withruntime.exe`：免安装版，自带运行库，下载后直接运行。
- `LiveCaptionsTranslator-win-x64.exe`：免安装版，体积小，需要先安装 [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)。
- ARM 电脑选文件名带 `arm64` 的版本。

**macOS / Linux：**

- `LiveCaptionsTranslator-macos-arm64.dmg`（Apple 芯片）/ `LiveCaptionsTranslator-macos-x64.dmg`（Intel 芯片）
- `LiveCaptionsTranslator-linux-x64.tar.gz`

安装方法见下面“macOS 和 Linux 版”。

数据保存位置（Windows）：

- 设置（`setting.json`）和历史数据库（`translation_history.db`，含课程、每节课、小结）在 `%LOCALAPPDATA%\LiveCaptionsTranslator`，装新版、卸载都不会动它们。旧版本放在程序文件夹里的设置，第一次启动时会自动复制过去。
- 每节课的 Markdown 文件和笔记初稿在“设置 ⑥”选的文件夹里，默认是 `文档\课堂同传助手`。

发布新版本：在 Actions 里手动运行 "CI/CD Pipeline"，勾选 "Publish a GitHub Release from this build"。

## macOS 和 Linux 版

> [!WARNING]
> **macOS 和 Linux 版是新加的，还没有在真机上测试过**，可能有各种问题。遇到问题（打不开、没声音、识别不出字、播报异常等）请到本仓库的 [Issues](https://github.com/YingQiu0871/LiveCaptions-Translator/issues) 反馈，写上系统版本和出错提示，谢谢。

翻译、小结、段落精修、分节、按课保存、笔记初稿、历史、时间线这些功能和 Windows 版相同，设置页的布局也一样。区别如下：

| 方面 | macOS / Linux 版 |
|---|---|
| 语音识别 | **只有阿里云实时语音识别**，没有 Windows 的“实时辅助字幕”。需要阿里云百炼 API Key（见下面“第一次使用”里的“用阿里云实时语音识别”） |
| 采集声音 | “电脑播放的声音”：Linux 用 `parec`（装 `pulseaudio-utils`，PulseAudio 和 PipeWire 都可以）；macOS 用系统的 ScreenCaptureKit，需要 macOS 13 以上和“屏幕录制”权限。“麦克风”：macOS 第一次使用会请求麦克风权限 |
| 课件 OCR | PDF 里的扫描页用 `pdftoppm`（poppler）+ `tesseract` 识别，需要自己安装，并装上课程语言的语言包（例如 `tesseract-ocr-eng`）。没装的话，扫描页读不出字，只能读有文字的页 |
| 耳机播报 | macOS 用系统的 `say`，可以选输出设备；Linux 用 `spd-say`（speech-dispatcher），没有的话用 `espeak-ng`，**只能从系统默认输出设备播放**（设置里选的输出设备不起作用），播报时会暂停识别。Linux 上的中文发音效果一般 |
| 快捷键 | **Ctrl+Alt+S**（结束本节）、**Ctrl+Alt+M**（开关播报）只在软件窗口处于前台时有效，不是系统全局快捷键。macOS 上也是 Ctrl 和 Option，不是 Command |
| 系统字幕相关设置 | 没有（没有 Windows 系统字幕的窄条、麦克风切换等） |

### Linux 安装

1. 下载 `LiveCaptionsTranslator-linux-x64.tar.gz`，解压：`tar -xzf LiveCaptionsTranslator-linux-x64.tar.gz`。
2. 进入解压出的文件夹，运行 `./install.sh`。不需要 root 权限，程序装到 `~/.local/opt/LiveCaptionsTranslator`，并在应用菜单里加上“课堂同传助手”。
3. 安装需要的系统组件（Debian / Ubuntu 的包名）：
   - `pulseaudio-utils`：采集声音（必需）
   - `fonts-noto-cjk`：中文字体
   - `speech-dispatcher` 或 `espeak-ng`：语音播报（可选）
   - `poppler-utils`、`tesseract-ocr` 和语言包（如 `tesseract-ocr-eng`）：扫描版课件 OCR（可选）

### macOS 安装

1. 按芯片下载：Apple 芯片（M1 及以后）选 `LiveCaptionsTranslator-macos-arm64.dmg`，Intel 芯片选 `LiveCaptionsTranslator-macos-x64.dmg`。不确定的话，点左上角苹果菜单 → 关于本机：显示“芯片”（Apple M…）的选 arm64，显示“处理器”（Intel）的选 x64。
2. 打开 dmg，把“课堂同传助手”拖进“应用程序”。
3. **第一次打开会被拦下**，因为应用没有苹果的公证签名，macOS 15 及以上不能再靠右键“打开”放行。先双击打开一次，再到 **系统设置 → 隐私与安全性**，点“仍要打开”。也可以在终端运行：`xattr -dr com.apple.quarantine /Applications/课堂同传助手.app`
4. 按提示授权：听“电脑播放的声音”需要 **屏幕录制**权限，用“麦克风”需要 **麦克风**权限。每次更新软件后，这两个权限可能需要重新授权。
5. 需要扫描版课件 OCR 的话，另外安装 poppler 和 tesseract（例如用 Homebrew：`brew install poppler tesseract tesseract-lang`）。

### 数据保存位置

| | 设置和历史数据库 | 每节课的 Markdown 默认保存位置 |
|---|---|---|
| macOS | `~/Library/Application Support/LiveCaptionsTranslator` | `~/Documents/课堂同传助手` |
| Linux | `~/.local/share/LiveCaptionsTranslator`（或 `$XDG_DATA_HOME/LiveCaptionsTranslator`） | “文档”文件夹下的 `课堂同传助手`（中文系统通常是 `~/文档/课堂同传助手`；没有配置“文档”文件夹时用 `~/Documents/课堂同传助手`） |

保存位置可以在“设置 ⑥”里改。

## 第一次使用

### 1. 准备语音识别

以下 Windows 系统字幕的步骤只适用于 Windows；macOS 和 Linux 直接看后面的“用阿里云实时语音识别”。

**用 Windows 系统实时字幕（免费）：**

1. 按 **Win + Ctrl + L** 打开实时辅助字幕。第一次打开时，按提示同意并下载语音识别文件。
2. 在 **设置 → 时间和语言 → 语言和区域** 里添加课程语言（例如英语），并安装它的“语音识别”组件。
3. 在实时辅助字幕的 ⚙️ 里**关掉它自带的“翻译”**，否则送给本工具的已经是中文，会漏句，也看不到外语原文。

系统字幕在窗口最小化或移出屏幕后就不再出字，所以上课时它会以一条窄条的形式贴在任务栏上方。可以把它拖到别处，在关闭本软件之前它会留在你放的位置。

**用阿里云实时语音识别：** 在阿里云百炼开通后创建 API Key，填到“设置 ⓪”里，点“测试连接”。这时系统字幕用不上，点“开始”时会自动关掉它。另外在“老师讲课的语言”里选老师讲课用的语言（英语、中文、日语、韩语、德语、法语、俄语、粤语，或自动判断）。

另外：扫描版课件需要 Windows 的“光学字符识别”组件（一般随语言包装好）；想听中文播报，需要中文语言包里的“文本到语音”组件。

### 2. 打开软件，进入“设置”页

左侧导航的第二个图标就是“设置”，所有设置都在这一页：

1. **⓪ 输入源**：选语音识别方式（系统实时字幕 / 阿里云）。网课、视频选“电脑播放的声音”；线下课选“麦克风”，再选用哪个麦克风（Windows 上选了之后它会被设为默认录音设备；Linux 上只供本程序使用；macOS 用系统默认麦克风）。**不要选蓝牙耳机上的麦克风**，否则耳机会切换到通话模式，音质变差。
2. **① 模型与 API**：选服务商（DeepSeek / 通义千问 / 其他兼容接口），粘贴 API Key，点“测试连接”。小结、段落精修和笔记初稿都用这里的模型。保存 Key 后，⑤ 的翻译接口会自动切换到“DeepSeek / 通义千问”。
3. **② 课件（可选）**：点“上传课件 PDF”选本节课的课件。如果是 .pptx，在打开文件的窗口右下角把文件类型改成 PowerPoint。
4. **③ 分节总结**：一般保持默认。也可以关掉“自动分节并总结”，或改成“固定按时间”；“每节最长”（默认 6 分钟）和“按时间分节”的间隔（默认 2 分钟）可以用滑块调。
5. **④ 耳机播报**：选读出内容和输出设备，点“试听”确认耳机里有声音。
6. **⑤ 翻译**：填这门课的学科和术语表（上传课件后可以点“从课件提取术语”，再检查一下译法）；“翻译接口”一开始是免费的 Google 翻译，在 ① 保存 Key 后会自动切换到“DeepSeek / 通义千问”（用 ① 的设置），也可以在这里改回去；不用它的话，“段落精修”不起作用，术语表也只有小结和笔记会用；下拉里还有原项目的 Ollama、DeepL、Youdao（有道）、Baidu（百度）等接口，选了之后点“其他接口的配置…”填它们的 Key；“翻译成”可以改目标语言；按需打开“纯英文模式”“段落精修”“结合上下文翻译”“中外对照”或“只转录，不翻译”。
7. **⑥ 转录保存**：选保存位置，决定是否“下课后自动生成笔记初稿”。页面最底下的“高级：总结提示词”可以改小结用的提示词，也能恢复默认。

### 3. 上课

1. 点标题栏的 **开始**。字幕页左边是原文和译文，右边是本节课的小节总结和总结进度。
2. 上课中：
   - **Ctrl+Alt+S**：立即结束本节并生成小结（老师换话题而工具没跟上时用）。
   - **Ctrl+Alt+M**：打开 / 关闭耳机播报（标题栏上也有开关）。
   - 标题栏的小旗子图标（鼠标停上去显示“结束本节并总结”）和 Ctrl+Alt+S 作用一样。
3. 下课点 **停止**，在弹框里填本节课名称、选择课程（可以直接输入新课程）。软件会总结最后一节，保存 Markdown 文件；打开了笔记初稿的话，再过半分钟到一两分钟会生成笔记初稿。
4. 下一节课再点“开始”，就是新的一节，不会和上一节混在一起，默认放进同一门课。

### 4. 课后

- **历史** 页：课程文件夹 → 每节课 → 详细内容。课程文件夹右键可以重命名，或删除（只有空文件夹能删）；每节课右键可以重命名 / 移到其他课程，或删除这节课；详细内容页可以“打开文件位置”或“生成笔记初稿”。以前版本的记录按天放在“以前的记录”里；“全部句子”保留了原来的总表。
- **时间线** 页：选课程和某一节课，看这节课的小节总结；导出按钮导出的是这节课的转录 Markdown（小节总结 + 原文 + 译文），和“停止”时保存的一样，不是笔记初稿。
- **笔记初稿**：下课后自动生成（如果开着），也可以在历史页的详细内容里点“生成笔记初稿”。

## 建议的设置

| 场景 | 输入源 | 播报输出设备 | Windows 默认输出 |
|---|---|---|---|
| 线下教室 | 麦克风（电脑自带或外接麦克风，坐近一点） | 蓝牙耳机 | 电脑扬声器 |
| 网课 / 视频 | 电脑播放的声音 | 蓝牙耳机 | 你听课用的设备 |

识别听的是系统默认输出设备（macOS 是整个系统的声音）或麦克风里的声音。如果播报也从这个设备放出来，就会被当成老师的话再识别一遍，所以播报期间软件会暂停识别。按上表把播报放到蓝牙耳机，就不会互相打断。（选输出设备只在 Windows / macOS 上可用；Linux 上播报总是从系统默认输出设备播出。）

## 常见问题

- **没有小结？** 看字幕页右侧“小节总结”下面的状态行，出错时会写原因；再到“设置 ①”点“测试连接”。小结始终用 ① 里的模型，就算翻译选了 Google 也一样。
- **点“开始”后一直没字？** 15 秒内没识别到任何文字，字幕页会提示可能的原因（输入源选错、麦克风静音、系统字幕没出字等），按提示检查。
- **Zoom、Teams 等会议软件开会时，系统字幕和阿里云都没字？** 先在会议里按 Win+Ctrl+L 打开 Windows 自带的实时字幕：如果它也没字，说明会议软件用独占模式占住了声卡，别的程序都抓不到声音。打开控制面板 → 声音 → 播放，选中默认播放设备 → 属性 → 高级，取消勾选“允许应用程序独占控制该设备”（下面那一项也一起取消），再重新进会议。还不行的话，看会议软件的扬声器是不是选了某个具体设备：系统字幕和阿里云抓的都是 Windows 默认播放设备的声音，扬声器应该选“与系统相同”，或者选和 Windows 默认相同的设备。
- **字幕全是中文，看不到原文？** 多半是系统实时辅助字幕自带的“翻译”开着，本工具检测到时会提醒，到它的 ⚙️ 菜单里关掉即可。
- **漏字多？** 阿里云识别会自动放大小声音；线下课尽量让麦克风离老师近一些。系统实时字幕请确认关掉了它自带的“翻译”。
- **系统字幕的窄条一直在？** 系统字幕不能真正隐藏，否则不出字；可以把它拖到屏幕边上。改用阿里云识别后它会自动关掉。
- **耗电？** 不上课时软件基本不工作。用阿里云识别时，系统实时字幕会被关掉，因为它自己的语音识别也很耗电。用系统字幕时，没人说话会降低读取频率。笔记本上课时，建议在 Windows 里选“最佳能效”或“平衡”电源模式。
- **笔记初稿没有结合课件？** 笔记初稿用的是“当前载入”的课件，课件不会随这节课保存，重启软件后也要重新上传。下课点“停止”之前不要移除课件；在历史页给以前的课重新生成笔记前，先在“设置 ②”载入那节课的课件。
- **自动切换麦克风没生效？** 系统实时字幕没有公开接口，软件是模拟点它的菜单实现的。失败时会弹出它的设置菜单，请在“首选项”里手动勾选或取消“包含麦克风音频”。
- **课件读不出字？** PDF 可以在 OCR 语言里选课件的语言，或者打开“所有页面都用 OCR”。.pptx 里整页是图片的幻灯片不会做 OCR，请导出成 PDF 再上传；旧版 .ppt 也请先另存为 PDF。
- **“全部删除”**（历史页“全部句子”里的垃圾桶图标）会删掉所有课程里每节课的记录（转录的句子和小节总结），课程文件夹和已经保存的 Markdown 文件会保留。软件本身不录音。
- 本版本关闭了原版的“有新版本”提示，以免误装原版、丢掉这些功能。

## 致谢与许可

本项目基于 [SakiRinn/LiveCaptions-Translator](https://github.com/SakiRinn/LiveCaptions-Translator) 修改，感谢原作者和所有贡献者。许可证与原项目相同，为 [Apache-2.0](LICENSE)。

使用的第三方库：[WPF-UI](https://github.com/lepoco/wpfui)、[NAudio](https://github.com/naudio/NAudio)、System.Speech（这三个是 Windows 版用的）、[PdfPig](https://github.com/UglyToad/PdfPig)、Microsoft.Data.Sqlite、CsvHelper；macOS / Linux 版界面用 [Avalonia](https://avaloniaui.net/)。可选的云服务：阿里云百炼（实时语音识别、通义千问）、DeepSeek。

请遵守学校和老师关于课堂录音、转写的规定。
