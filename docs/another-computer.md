# 在另一台 Windows 电脑使用

## 直接运行

取得 `SilverWolfPet-v2.6.19-preview-Windows-x64.zip` 便携包后，完整解压并运行 `SilverWolfPet.exe`。不要单独移动程序，`assets` 文件夹必须与程序放在一起。若仓库没有提供便携包，可按下方步骤从源码构建。

如果电脑上已有旧版桌宠，先在托盘菜单退出旧版。

## 从源码构建

下载或克隆整个仓库，在 Windows PowerShell 中进入项目目录，运行：

```powershell
.\build.ps1
```

构建依赖 Windows 的 64 位 .NET Framework 4.x 编译器与 WPF 组件。输出位于 `dist/SilverWolfPet-v2.6.19-preview/`，启动其中的 `SilverWolfPet.exe`。

## 新电脑需要重新设置的内容

- 在新电脑的 Codex 中登录自己的账号，额度与任务提醒使用该电脑上的 Codex。
- 任务完成监测针对本机任务，不会因为同步了桌宠源码就自动监测另一台电脑。
- 桌宠位置、开机自启动、游戏免打扰列表等设置在新电脑上重新配置。旧电脑的配置文件、登录凭据和聊天 API Key 不随源码上传。
- 聊天 API 验证功能未包含在当前版本中。

本仓库的源码和素材用于继续维护桌宠；本地历史备份、测试输出、运行日志不纳入源码同步。
