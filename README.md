# MOONLIGHT

[简体中文](README.md) | [English](README.en.md)

一个本地运行的 Windows 11 深浅色切换工具。双击 `MOONLIGHT.exe` 立即切换主题，也可以按照每天设定的时间自动切换。

默认英文，支持随时切换为简体中文。免安装，无需管理员权限，不联网，不依赖额外下载的运行环境。

## 使用

1. 把 `MOONLIGHT.exe` 放在桌面或其他固定位置，双击切换深浅色，再次双击切回来。
2. 右键任务栏托盘图标，选择 `Settings` 打开设置；双击托盘图标也可以打开。
3. 通过 `Enable scheduled switching` 启用定时切换。默认 07:00 切浅色，19:00 切深色。
4. `Switch Windows theme` 同时控制 Windows 界面和跟随系统的应用主题。取消勾选后，自动和手动切换都会暂停改变颜色。
5. 需要开机运行时，勾选 `Start with Windows`，点击 `Save and apply`。
6. `Save and apply` 与 `Hide to tray` 之间是语言选项。选择 `English` 或 `简体中文` 后，界面和托盘菜单立即切换，语言偏好自动保存。

语言切换会保留正在编辑但尚未保存的时间和勾选项。修改定时或开机启动设置后，仍需点击保存。

手动切换会保持到下一个定时切换点；`Resume schedule` 可以立即恢复自动计划。自动计划暂停时，手动主题持续保留。`Switch Windows theme` 关闭时，需要先勾选并保存才能继续切换。

关闭设置窗口会收起到托盘；从托盘选择 `Exit` 才会退出程序。自动切换需要程序保持运行。

## 界面

界面按以下顺序排列：

- `MOONLIGHT` 名称。
- 启用定时切换。
- 浅色与深色开始时间。
- 切换 Windows 主题、开机启动。
- 切换浅色、切换深色、恢复自动计划。
- 保存并应用、语言选项、收起到托盘。

界面和托盘菜单不使用括号说明。Windows 主题选项的悬停提示说明它同时影响应用主题。

## 兼容性与数据

适用于 Windows 11，使用系统自带的 .NET Framework 4.x。实测系统为 Windows 11 26H2，内部版本 26300。

支持 Windows 配色，以及选择跟随系统的应用。应用自行固定主题时，需要在应用中启用跟随系统；部分应用可能需要重新打开才能刷新。

程序每 15 秒检查一次计划。电脑休眠期间不会唤醒设备，恢复或解锁后补上当前应有的主题。Windows 高对比度模式下暂停切换。

配置和滚动日志仅保存在本机：

```text
%LOCALAPPDATA%\MOONLIGHT\settings.json
%LOCALAPPDATA%\MOONLIGHT\MOONLIGHT.log
```

配置采用原子写入并保留上次备份，损坏时自动恢复。首次升级会读取前一版本的定时设置，并迁移属于前一版本的开机启动项。新安装默认不开机启动。

开机启动只写入当前用户的启动项。启用后请保留程序所在位置。如果在 Windows 设置或任务管理器中禁用了启动项，需要在相应位置重新启用。

删除程序前，先取消开机启动并保存，然后从托盘退出，再删除 `MOONLIGHT.exe`。如需清理配置，可再删除上面的目录。

## 仓库结构

```text
MOONLIGHT/
├── MOONLIGHT.exe
├── README.md
├── README.en.md
├── LICENSE
├── src/
│   ├── Program.cs
│   ├── SettingsForm.cs
│   ├── Localization.cs
│   ├── Core.cs
│   ├── Platform.cs
│   ├── Upgrade.cs
│   ├── app.manifest
│   └── MOONLIGHT.ico
├── tests/Moonlight.Tests.cs
├── scripts/Build.ps1
├── scripts/Test.ps1
├── docs/TESTING.md
└── .github/workflows/build.yml
```

根目录附带已编译的可运行程序。源码、构建脚本、测试、GitHub Actions 和 MIT 许可证一并提供；个人设置和本机日志不放入仓库。

## 构建和测试

在 Windows PowerShell 中进入仓库目录，运行：

```powershell
.\scripts\Build.ps1
.\scripts\Test.ps1
```

构建使用 Windows 自带的 C# 编译器，不下载包，输出到 `build\MOONLIGHT.exe`。默认测试包含计划逻辑、配置恢复、语言切换、布局顺序及升级转换，不改变桌面主题。

需要验证本机真实主题切换和真实时间点的定时触发时，先退出正在运行的 MOONLIGHT，再运行：

```powershell
.\scripts\Test.ps1 -Integration
```

集成测试会短暂切换桌面主题，在结束时恢复原值。测试使用隔离的配置目录；启动项测试使用临时名称，完成后移除。测试报告输出到 `build\test-results\`。

GitHub Actions 在 Windows runner 上执行构建与默认测试，随后保存可运行程序为构建产物。工作流采用官方 [checkout](https://github.com/actions/checkout) 与 [upload-artifact](https://github.com/actions/upload-artifact) 动作。

## 推送到 GitHub

在 GitHub 创建名为 `MOONLIGHT` 的空仓库，解压本项目，在项目目录中运行以下命令。将地址中的 `YOUR_USERNAME` 替换为自己的账号。

```powershell
git init -b main
git add .
git commit -m "Initial MOONLIGHT release"
git remote add origin https://github.com/YOUR_USERNAME/MOONLIGHT.git
git push -u origin main
```

如果已经配置过远程地址，可直接使用已有的 Git 流程。压缩包不包含本机 Git 历史，便于作为新仓库导入。

## 许可证

MIT。详见 [LICENSE](LICENSE)。
