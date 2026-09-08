// SPDX-License-Identifier: GPL-3.0-or-later

# QuickLook.Plugin.SnpViewer

一个 [QuickLook](https://github.com/QL-Win/QuickLook) 插件（Windows），在资源管理器里选中
Touchstone S 参数文件（`.s1p`、`.s2p`、…、`.snp`、`.ts`）后按**空格**，即可以
OxyPlot 图表的形式预览频响曲线。

## 功能

- 解析 **Touchstone v1.1 / v2.x**：RI / MA / DB 三种数据格式，S / Y / Z / G / H
  参数类型，Hz / kHz / MHz / GHz 频率单位，逐端口参考阻抗。
- 支持**跨行数据**：多端口文件常把一个频点的数据拆在多个物理行，解析器会
  按数据流自动归并（修过把续行误读成负频率的 bug）。
- 支持 `[Noise]` 噪声块（噪声系数 + 等效噪声电阻）。
- 一张图绘制全部 `Sij`（或 `Yij` / `Zij` / …）曲线：
  - Y 轴：dB / 幅值 / 实部 / 虚部 / 相位（°）胶囊切换；
  - X 轴：线性 / 对数切换；
  - 图例按端口数排成矩阵（s2p → 2×2，s3p → 3×3 …），可点击显示/隐藏曲线；
  - 悬停数据点显示读数 tooltip。
- 跟随 Windows 浅色 / 深色模式（读 `AppsUseLightTheme`，图表、选项条、图例、
  tooltip 整体换肤）。
- 界面语言跟随系统显示语言：中文系统显示中文，否则显示英文。
- 大文件保护：绘图抽稀到每条曲线 ≤ 4000 点，避免 OxyPlot 卡死。
- 文件头嗅探：非 Touchstone 文件直接放行，不抢其他预览插件的活。
- More 菜单：用系统默认文本编辑器打开、在资源管理器中定位。

## 支持的扩展名

```
.s1p .s2p .s3p .s4p .s5p .s6p .s7p .s8p .snp .ts
```

## 安装（用户）

从 [Releases](https://github.com/riverstill/quicklook-plugin-snpviewer/releases)
下载 `QuickLook.Plugin.SnpViewer.qlplugin`，在资源管理器里选中它按**空格**，
点安装即可。

手动安装也可以：

1. 退出 QuickLook（托盘图标 → Quit）。
2. 把 `.qlplugin`（就是个 ZIP）解压，将内容拷进插件目录下的
   `QuickLook.Plugin.SnpViewer` 文件夹（没有就新建）。Installer 版路径一般是
   `%AppData%\pooi.moe\QuickLook\QuickLook.Plugin\`，其他发行版见
   [官方 wiki](https://github.com/QL-Win/QuickLook/wiki/Differences-Between-Distributions#user-data-location)。
3. 启动 QuickLook，选中 `.s2p` 文件按**空格**。

## 构建 / 测试 / 打包（仅 Windows）

> Linux 下编不了（WPF / net462），以 CI（`Build` workflow，`windows-latest` +
> .NET SDK 8）结果为准，不要在本地 `dotnet build` 碰运气。

```cmd
:: 编译检查
dotnet build QuickLook.Plugin.SnpViewer.sln -c Release

:: 测试
dotnet test tests\QuickLook.Plugin.SnpViewer.Tests.csproj

:: 只跑一个测试
dotnet test tests\QuickLook.Plugin.SnpViewer.Tests.csproj --filter "FullyQualifiedName~<TestName>"

:: 打包（生成的 QuickLook.Plugin.SnpViewer.qlplugin 在仓库根目录）
powershell -ExecutionPolicy Bypass -File package.ps1 -Configuration Release -Platform AnyCPU
```

注意：

- `.sln` 里的平台名是 **`Any CPU`（带空格）**，`.csproj` 里是 **`AnyCPU`（不带空格）**，
  两边拼写必须同步，否则报 MSB4126。
- `.qlplugin` 必须用 `package.ps1`（走 `dotnet publish`，保证 NuGet 依赖 DLL
  被 stage 进来）生成，不要手卷 zip，更不要直接打包 `Build\...` 目录。
  包内约定：插件 DLL + `OxyPlot*.dll` + `Translations.config` +
  `QuickLook.Plugin.Metadata.config` 全放 zip 根目录；**不含**
  `QuickLook.Common.dll`（宿主自带）、`*.pdb` 和缓存。
- 发版前记得 bump `QuickLook.Plugin.Metadata.config` 里的 `<Version>`，
  空格键安装器就认这个文件。

测试用 `samples/` 里的 fixture（`.s1p` / `.s2p`，含 `[Noise]`）断言解析出的
精确数值；解析器改动请同步加回归测试。

## 项目结构

```
.
├── QuickLook.Plugin.SnpViewer/
│   ├── Plugin.cs                         # IViewer 实现（嗅探 + 预览入口）
│   ├── Plugin.MoreMenu.cs                # IMoreMenu 实现（打开/定位）
│   ├── SnpPanel.xaml(.cs)                # WPF 面板 + OxyPlot 视图模型
│   ├── ThemeHelper.cs                    # 系统深浅色检测 + 图表配色 + 语言选择
│   ├── TouchstoneParser.cs               # Touchstone v1/v2 文本解析器
│   ├── TouchstoneDocument.cs             # 数据模型
│   ├── Translations.config
│   ├── QuickLook.Plugin.Metadata.config  # 空格键安装器用的元数据（含版本号）
│   └── QuickLook.Plugin.SnpViewer.csproj
├── samples/                              # 解析器 fixture
├── tests/                                # xUnit 测试
├── package.ps1                           # 打 .qlplugin（CI 和本地共用同一脚本）
├── build.cmd / build.sh                  # 快捷构建脚本
├── AGENTS.md                             # 给 AI 助手的仓库专属注意事项
└── LICENSE-GPL.txt
```

依赖（NuGet 自动拉取，无需手动安装）：`OxyPlot.Wpf 2.1.2`（注意**不是**
2.2+，图例 API 不一样）、`QuickLook.Common 4.5.0`。目标框架 net462。

## 排错

- 预览显示 "Failed to preview" 但构建是绿的：先看
  `%AppData%\pooi.moe\QuickLook\QuickLook.Exception.log` 里本插件的堆栈，
  不要猜。
- 已知的几个坑都记在 `AGENTS.md`（BAML 加载上下文、冻结 brush 不能改、
  `Plot.Model` 必须代码赋值等），改 UI 相关代码前建议先读一遍。

## 许可

GPL-3.0-or-later，见 `LICENSE-GPL.txt`。所有源码文件头带 SPDX 标识。

## 致谢

- [QuickLook](https://github.com/QL-Win/QuickLook)（QL-Win 社区）。
- [OxyPlot](https://github.com/oxyplot/oxyplot)（WPF 绘图引擎）。
- [Touchstone File Format Specification v2.1](https://ibis.org/touchstone_ver2.1/)
  （IBIS Open Forum）。
