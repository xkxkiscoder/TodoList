# AGENTS.md — TodoList 开发准则

本文件是 AI Agent 与开发者修改本仓库时必须遵守的准则。

## 1. 构建与验证

```powershell
# 工作目录 = 仓库根（TodoList.sln 所在处）
dotnet build TodoList.sln -c Debug
```

- **每次改动 XAML / C# 后必须编译验证**，以 0 错误为完成标准；XAML 编译器（XamlCompiler）会校验 `x:Name` 引用、Setter Target、事件处理器签名，只改 XAML 也必须走编译。
- **编译前关闭正在运行的 TodoList.exe**（锁定 `bin` 目录会导致 `MSB3027` 文件锁错误）：
  ```powershell
  $p = Get-Process TodoList -ErrorAction SilentlyContinue
  if ($p) { $p | Stop-Process -Force; Start-Sleep -Seconds 2 }
  ```
- 输出路径：`TodoList\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\TodoList.exe`
- 发布（自包含）：`dotnet publish TodoList\TodoList.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained`（**必须带 `-p:Platform=x64`**，WAS 自包含不支持 AnyCPU）

### 禁止改动的构建配置

| 配置 | 原因 |
|---|---|
| `global.json` → SDK `10.0.301`（`rollForward: disable`） | MVVM 生成器 emit 的 `partial property` 实现依赖新版编译器特性；**回退 .NET 8 SDK 会报 CS0103（`field` 不存在）/ CS8050** |
| `TodoList.csproj` → `TargetFramework=net10.0-windows10.0.19041.0` | 与 SDK 10 对齐的运行目标；构建 SDK（编译器）与 TFM（运行时）是两个维度，勿混淆——SDK 10 编译 net10 产物、`SelfContained=true` 发布时自带运行时 |
| `TodoList.csproj` → `LangVersion=preview` | preview 语法前提 |
| `TodoList.csproj` → `AccelerateBuildsInVisualStudio=false` | 避免 VS 增量编译与 XAML 生成器缓存不一致 |
| `CommunityToolkit.Mvvm` 版本 `8.4.0-preview3` | partial property 语法需要 preview 包；升级需整体验证 |

## 2. MVVM 预览版语法

本项目启用 `LangVersion=preview` + CommunityToolkit.Mvvm **8.4.0-preview3**，使用其引入的 **partial property** 语法。

### 2.1 新增可观察属性：首选 partial property（preview 语法）

```csharp
[ObservableProperty]
public partial string SearchText { get; set; }

[ObservableProperty]
public partial TodoStatus Status { get; set; }
```

生成的属性名 = 去掉下划线前缀的 PascalCase（对应 XAML 中 `x:Bind SearchText`）。

**默认值规则**：partial property **不允许**写 `= 初始值`（会报 CS8050「只有自动实现的属性才能具有初始值设定项」）。默认值在构造器中赋：

```csharp
public MainViewModel()
{
    SearchText = string.Empty;
    Status = TodoStatus.NotStarted;
}
```

### 2.2 存量字段形式：兼容但不新增

旧代码大量使用字段形式，这是**合法的存量写法**，可以保留、不必批量迁移：

```csharp
[ObservableProperty]
private bool _hideCompleted;          // 生成属性 HideCompleted

[ObservableProperty]
private string _listTitle = "全部待办"; // 字段形式允许初始化器
```

规则：
- **新属性一律用 2.1 的 partial property 形式**；
- 字段形式只允许修饰符 + 私有字段（`_camelCase`），初始化器写在字段上；
- **禁止混用**：同一属性不得同时存在字段与 partial property 两种声明。

> 为什么新代码优先 partial property：字段形式在 WinUI 3 的 CsWinRT 生成链路下不产生正确的 WinRT marshalling 代码（工具包会报 `MVVMTK0045` 警告），partial property 是官方推荐的 AOT 兼容形式。

### 2.3 变更回调

属性变更钩子命名与形式不变，partial property / 字段形式通用：

```csharp
partial void OnSearchTextChanged(string value)
{
    // 值已变更后触发
}
```

### 2.4 其他 MVVM 规则

- ViewModel 继承/实现通知走工具包生成，**不要手写 `INotifyPropertyChanged` 样板**。
- 集合绑定用 `ObservableCollection<T>`；分组视图用 `CollectionViewSource`（见 `MainViewModel.RecreateGroupViews`）。
- 数据持久化走 `Services/TodoStore`、`Services/AppSettings`，ViewModel 不直接碰文件 IO。
- XAML 绑定一律用编译期 **`x:Bind`**（WinUI，非 WPF `{Binding}`），模式显式标注 `Mode=OneWay` / `OneTime`。

## 3. 自适应布局（官方标准）

### 3.1 核心规则：集中式状态函数 + SizeChanged 驱动（VSM 实测不可用）

**按宽度重排布局时，规则必须收敛到集中函数**（`ApplyAdaptiveStates` → `UpdateQuickAddLayout` / `UpdateCalendarLayout`），由 `SizeChanged` / `Loaded` / 视图切换统一驱动；**禁止**把布局切换逻辑散落在多个事件处理器里。

> **⚠️ 实测记录（WAS 1.5.240627000 / WinUI 3 / 本项目 Window 直接内容、无 Page）**：
> 官方 `VisualStateManager` + `AdaptiveTrigger` 方案**在本项目不可用**，已逐一实测排除：
> 1. 状态组挂在 Window 根 Grid 上——`AdaptiveTrigger` **不评估**；
> 2. 状态组挂在 `Control` 实例（Button）上——`AdaptiveTrigger` **仍不评估**；
> 3. 手动 `VisualStateManager.GoToState(...)`——状态组能读到（`GetVisualStateGroups` 返回 2 组、状态名正确），但 **GoToState 返回 false**，setters 不应用。
>
> 根因：UWP/WinUI 对 `VisualStateGroups` 附加属性的合法位置限制为**模板根 / Page / UserControl 根**，本项目是 Window 直接挂 Grid 内容（无 Page），全部不满足。**不要再次尝试 VSM/AdaptiveTrigger/GoToState**，除非先把窗口内容重构进 Page（x:Name → code-behind 引用需整体迁移，属大型重构）。

当前实现模式：

```csharp
// SetupCustomTitleBar() 中统一挂接
SizeChanged += (_, _) =>
{
    ApplyAdaptiveStates();
    DispatcherQueue.TryEnqueue(UpdateDragRegions);
};
Root.Loaded += (_, _) => { ApplyAdaptiveStates(); DispatcherQueue.TryEnqueue(UpdateDragRegions); };

/// <summary>集中入口：所有自适应状态切换走这里。</summary>
private void ApplyAdaptiveStates()
{
    UpdateQuickAddLayout();   // 快速添加行：容器宽 < 520 → 两行
    UpdateCalendarLayout();   // 日历面板：容器宽 < 600 → 上下堆叠
}
```

每个 `UpdateXxxLayout` 的标准结构：
1. **空值 / 未完成布局守卫**（`ActualWidth <= 0` 早退——面板初始 `Collapsed` 时为 0）；
2. **布尔缓存**（`bool? _narrow`）避免每次尺寸变化重复排布；
3. 窄/宽两个分支对称设置列宽 + `Grid.SetRow/SetColumn/SetColumnSpan`；
4. **XAML 基线 = 宽态**，保证首次显示正确。

**视图切换时机**：初始 `Collapsed` 的面板（`CalendarPanel`）切到可见后 `ActualWidth` 需布局 pass 才有值，`ShowView` 中必须 `DispatcherQueue.TryEnqueue(UpdateCalendarLayout)` 补算（此 bug 已实际发生）。

### 3.2 断点与阈值换算

- 阈值判定基于**容器实际宽度**（`Grid.ActualWidth`，DIP），不是窗口宽度——换算关系：**窗口宽 ≈ 容器宽 + 56**（Root `Padding=14`×2 + 卡片 `Padding=14`×2）。
- 现有断点（改布局需求时同步维护 README 断点表）：

  | 集中函数 | 窄态条件（容器宽） | 对应窗口宽 |
  |---|---|---|
  | `UpdateQuickAddLayout` | `< 520` | `< 576` |
  | `UpdateCalendarLayout` | `< 600` | `< 656` |

### 3.3 布局函数编写规则

1. **每个区域一个 `UpdateXxxLayout` 函数**，窄/宽两分支对称，禁止把切换逻辑写进别的事件处理器；
2. **布尔缓存 + `ActualWidth <= 0` 守卫**（见 3.1 标准结构）；
3. **XAML 基线（元素默认 Row/Column/列宽）= 宽态布局**，窄态完全由代码切换、宽态恢复基线值；
4. 需要代码定位的子元素与列定义**必须 `x:Name`**（如 `QuickAddCol1`、`CalendarCol1`、`MonthCard`、`DayPlanCard`），供 code-behind 引用；
5. 列宽用 `new GridLength(...)`：`GridUnitType.Star` / `Auto` / `new GridLength(0)` / `new GridLength(240)`；
6. 位置切换成组调用：`Grid.SetRow` / `Grid.SetColumn` / `Grid.SetColumnSpan`，三个维度每分支都要显式设置（含恢复值），避免状态残留。

### 3.4 例外：允许代码计算的场景

- **`UpdateDragRegions()`**（标题栏拖拽热区）依赖 `TransformToVisual` 实际坐标与 DPI 换算，属窗口集成逻辑，**保留代码实现**；新增可点击控件时确保其位于 `ActionsPanel` 内（该区域已被自动排除出拖拽区）。
- 交互态的短暂延迟收起（如列表状态胶囊的 160ms 计时器）不属于布局，允许 code-behind。

### 3.5 文本截断规范

列表行标题 / 备注一律**换行而非硬截**：

```xml
<TextBlock Text="{x:Bind Title, Mode=OneWay}"
           TextWrapping="Wrap"
           MaxLines="2"
           TextTrimming="CharacterEllipsis" />
```

禁止新增 `TextWrapping="NoWrap"` 的主要信息文本（列宽固定的状态 / 日期列除外）。

## 4. 通用编码规范

- **命名空间**：文件作用域（`namespace TodoList;`，无大括号嵌套）。
- **可空引用**：`Nullable=enable` 全局启用；非空属性给默认值，可空用 `T?`。
- **命名**：类型/属性 PascalCase；字段与局部变量 `_camelCase` / `camelCase`；布尔属性前缀 `Is/Has/Can/Should`。
- **异常**：禁止空 `catch { }`，至少加注释（`// ignore`）或日志；布局变换等可恢复失败用 `try/catch` 包裹并注释原因。
- **不用 WPF API**：本项目是 WinUI 3（`Microsoft.UI.Xaml.*`），没有 `System.Windows.*`、`Application.Current.Dispatcher`（用 `DispatcherQueue`）、`RoutedEventArgs` 命名空间为 `Microsoft.UI.Xaml`。
- **资源集中**：画刷 / 样式放 `App.xaml`；颜色需同时写 `Default`（深色）与 `Light` 两个 `ThemeDictionaries` 条目。
- **窗口按钮**：标题栏**仅保留关闭按钮**（产品决定，勿加回最小化 / 最大化按钮或折叠浮层）。
- **托盘与关闭行为**：窗口不显示在任务栏/Alt-Tab（`AppWindow.IsShownInSwitchers=false`），常驻托盘（`Services/TrayIconService.cs`，原生 Shell_NotifyIcon + message-only 窗口）。**点 X / Alt+F4 = 隐藏到托盘**（WAS 1.5 的 Window 没有 `Closing` 事件也没有 `Hide()`，用 WndProc 子类化拦 `WM_CLOSE` + `ShowWindow(SW_HIDE)`）；只有托盘菜单「退出」（`_exitRequested=true`）才真正关闭。GDI/托盘句柄必须按 `Dispose` 全量释放（`DestroyIcon`/`DeleteObject`/`Shell_NotifyIcon(NIM_DELETE)`；`LoadIcon` 共享句柄不可销毁）。
- **托盘图标（`CreateIconIndirect` 实测坑，勿回退）**：`ICONINFO.fIcon` 必须用 **`int`（值 1）**——`bool`（含 `[MarshalAs(Bool)]`、结构大小同为 24）在两种运行时下**稳定失败**；**hbmMask 必须是 `CreateBitmap` 的新鲜位图且不可绘制**——对单色 mask 做 `PatBlt`/`FillRect` 初始化会使 `CreateIconIndirect` 返回 NULL（实验矩阵已验证四种组合）；hbmColor 用 **32bpp DIB section**（GDI 绘制后需手动补 alpha：非黑像素 → 0xFF）。失败时兜底 `LoadIcon(IDI_APPLICATION)`。
- **默认值**：窗口置顶**默认关闭**（`AppSettings.AlwaysOnTop` 与 ViewModel 字段均为 false）。
- **重构范围**：只改任务要求的部分，不做无关重构（历史代码风格不统一处保持原样）。

## 5. 提交前检查清单

- [ ] `dotnet build TodoList.sln -c Debug` 0 错误（`MVVMTK0045` 等既有警告可容忍，**不得新增**错误类警告）
- [ ] 新增可观察属性用 partial property 形式，无 `= 初始值`
- [ ] 布局自适应走集中 `UpdateXxxLayout` 函数（含 `ActualWidth<=0` 守卫 + 布尔缓存），未尝试 VSM/AdaptiveTrigger（实测不可用，见 3.1）
- [ ] 需代码定位的元素 / 列定义已 `x:Name`
- [ ] 主题相关颜色在两套 `ThemeDictionaries` 中都已定义
- [ ] 长文本为 `Wrap + MaxLines=2`，未引入新的硬截断
- [ ] README.md 断点表与代码阈值（520/600 容器宽）一致（如改动过断点）
- [ ] 初始 `Collapsed` 的面板在 `ShowView` 切换后有 `DispatcherQueue.TryEnqueue` 补算
