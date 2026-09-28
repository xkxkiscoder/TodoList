# TodoList

极简玻璃风格的桌面待办事项管理应用，基于 **WinUI 3（Windows App SDK）** 构建，Mica 材质背景 + 自定义标题栏。

## 功能特性

- **三种视图**：全部 / 今日 / 日历（月历 + 当日计划侧栏）
- **任务字段**：标题、备注、优先级四色（低 / 中 / 高 / 紧急）、三态状态（未开始 ○ → 进行中 ● → 已完成 ✓，点击循环切换）
- **分组展示**：按状态分组（未开始 / 进行中 / 已完成），组头可折叠、带计数徽标
- **计划日**：可选日期，列表与日历双视图联动
- **搜索**：按标题 / 备注实时过滤
- **排序**：列表拖拽排序；删除带 5 秒撤销条
- **设置**：窗口置顶（默认关闭）、隐藏已完成、开机启动
- **系统托盘**：不占用任务栏/Alt-Tab（`IsShownInSwitchers=false`），常驻通知区域；点 X 或 Alt+F4 **隐藏到托盘**继续运行，托盘双击/左键恢复窗口，右键菜单可「显示主窗口 / 退出」
- **窗口记忆**：尺寸 / 位置 / 上次视图自动恢复
- **自定义标题栏**：空白区拖动窗口，仅保留关闭按钮
- **自适应布局**：窗口变窄时快速添加行与日历面板自动重排（见下文断点表）

## 技术栈

| 项 | 值 |
|---|---|
| 框架 | WinUI 3 / Windows App SDK 1.5.240627000 |
| 运行时 | .NET 10（`net10.0-windows10.0.19041.0`，x64，**自包含**、非 MSIX） |
| MVVM | CommunityToolkit.Mvvm **8.4.0-preview3**（`LangVersion=preview`） |
| 构建 SDK | **.NET 10.0.301**（`global.json` 锁定，勿回退） |

## 构建与运行

先决条件：安装 .NET SDK **10.0.301**（`global.json` 已锁定版本与 rollForward 策略）。

```powershell
# 仓库根目录（TodoList.sln 所在处）
dotnet build TodoList.sln -c Debug
```

发布（**自包含**，产物自带 .NET 10 运行时 + Windows App SDK 原生依赖，目标机器无需安装任何运行时；`-p:Platform=x64` 必须显式指定——WAS 自包含不支持 AnyCPU）：

```powershell
dotnet publish TodoList\TodoList.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained
```

运行产物：

```
TodoList\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\TodoList.exe          # 调试
TodoList\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\publish\TodoList.exe # 发布（约 165 MB）
```

> **注意**：正在运行的 TodoList.exe 会锁定 `bin` 输出目录，编译前请先关闭应用，否则会报 `MSB3027/MSB3021` 文件锁错误。

## 快捷键

| 快捷键 | 功能 |
|---|---|
| `Ctrl+N` | 聚焦快速添加输入框 |
| `Ctrl+F` | 聚焦搜索框 |
| `Ctrl+1` / `Ctrl+2` / `Ctrl+3` | 切换 全部 / 今日 / 日历 视图 |
| `Esc` | 关闭详情页或设置面板 |

## 自适应布局断点

自适应布局由集中式状态函数实现（`SizeChanged` 驱动，见 [AGENTS.md](AGENTS.md) §3.1 实测记录——VSM + AdaptiveTrigger 在本项目架构下不可用），阈值为**容器宽度**：

| 区域 | 断点 | 窄窗口行为 |
|---|---|---|
| 快速添加行 | `< 576px` | 标题独占第一行，优先级 / 计划日 / 添加按钮换到第二行 |
| 日历面板 | `< 656px` | 由左右两列改为上下堆叠（月历在上、当日计划在下） |

设计规范详见 [AGENTS.md](AGENTS.md)。

## 项目结构

```
TodoList.sln
global.json                  # 锁定 .NET SDK 10.0.301
TodoList/
├── App.xaml(.cs)            # 应用资源：双主题画刷、GlassCard/IconBtn 等样式
├── MainWindow.xaml(.cs)     # 主窗口：三视图、快速添加、日历生成、拖拽热区、设置浮层
├── Models/
│   ├── TodoItem.cs          # 待办实体
│   └── TodoEnums.cs         # 优先级 / 状态枚举
├── ViewModels/
│   ├── MainViewModel.cs     # 主 ViewModel：视图切换、分组、CRUD、设置联动
│   ├── TodoItemVm.cs        # 行级 VM：状态循环、删除线/画刷等绑定属性
│   └── StatusGroup.cs       # 状态分组（折叠）
└── Services/
    ├── TodoStore.cs         # 数据持久化与排序
    └── AppSettings.cs       # 窗口与偏好设置持久化
```

## 架构约定

- **MVVM**：视图逻辑放 ViewModel，窗口 / 拖拽区 / 日历网格等窗口特有逻辑留在 code-behind
- **绑定**：XAML 一律用编译期 `x:Bind`
- **主题**：颜色画刷集中在 `App.xaml` 的 `ThemeDictionaries`（深色 / 浅色双份）
- 完整开发准则见 [AGENTS.md](AGENTS.md)
