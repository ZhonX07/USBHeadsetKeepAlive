# USB 耳机 KeepAlive

一个轻量的 Windows 桌面工具，持续向指定音频输出设备发送极低幅度、非零的音频信号，防止部分 USB 耳机或 DAC 因检测到长时间数字静音而进入待机，造成下一段声音的开头丢失。

## 功能

- 选择任意当前可用的 Windows 音频输出设备
- 一键启动或停止 KeepAlive
- 默认输出 `-80 dBFS` 的 440 Hz 信号，并可在 `-100` 至 `-50 dBFS` 之间调整
- 使用 WASAPI 共享模式，不独占设备
- 可随 Windows 用户登录自动启动，并自动开始保活
- 可在自启时直接开始保活并隐藏到系统托盘
- 关闭或最小化窗口后继续在托盘运行

## 系统要求

- Windows 10 或 Windows 11
- 构建需要 .NET 8 SDK
- 普通构建后的运行需要 .NET 8 Desktop Runtime

## 构建和运行

在本项目目录打开 PowerShell：

```powershell
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

构建后的程序位于：

```text
bin\Release\net8.0-windows\UsbHeadsetKeepAlive.exe
```

## 发布为独立单文件程序

下面的命令会生成 Windows x64 独立程序，目标电脑无需预装 .NET：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

发布结果位于：

```text
bin\Release\net8.0-windows\win-x64\publish\
```

## 使用建议

1. 选择发生“首音丢失”的 USB 耳机或 DAC。
2. 保持默认 `-80 dBFS`，点击界面中醒目的“启动 KeepAlive”。只有状态显示绿色的“运行中”时，程序才会建立音频流，并出现在 Windows 音量合成器里。
3. 等待足够长时间，让设备原本应该进入待机，然后播放提示音测试。
4. 如果仍会丢首音，将强度逐步提高，例如 `-75`、`-70`、`-65 dBFS`；以能可靠保活且听不到为准。

注意：Windows 或设备上的静音、应用音量混音器、厂商驱动处理都可能影响信号。更换或拔插设备后，请刷新设备并重新启动 KeepAlive。

## 依赖

- [NAudio 2.2.1](https://www.nuget.org/packages/NAudio/2.2.1)（锁定版本）

开机自启仅写入当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，不需要管理员权限。

## 许可证

本项目采用 MIT License，详见 [LICENSE](LICENSE)。第三方依赖信息见
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
