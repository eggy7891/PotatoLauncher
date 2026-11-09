# PotatoLauncher

PotatoLauncher 是一个原创 Material Design 3 风格的 Windows Minecraft 启动器。它采用 .NET 10、WPF 和 MVVM 构建，支持离线与微软账号、本地实例、原版/Fabric/Quilt/Forge/NeoForge 安装、Modrinth 内容、并行下载以及离线自定义皮肤和披风。

本项目参考了 Minecraft 启动协议的公开行为，并根据 PCL2 2.99.0 与 HMCL 3.16.4 的可观察运行流程做独立实现。它不包含或复用这些启动器的名称、图标、文案、代码或视觉资产。

## License

PotatoLauncher 以 [MIT License](LICENSE) 开源。你可以使用、修改、分发和再发布本项目，但必须保留版权与许可证声明。Minecraft、PCL 和 HMCL 不属于本项目，相关商标与第三方组件遵循各自许可证。

签名发布通过 [SignPath Foundation](https://signpath.org/) 的免费开源代码签名计划处理；签名工作流位于 `.github/workflows/signpath-release.yml`，只有仓库维护者配置 SignPath 项目变量和 API Token 后才可运行。

## 功能

- 扫描 `.minecraft/versions`，解析版本继承、library 规则、natives、classpath、assets 和启动参数。
- 自动探测常见 Java 路径，并按版本的 `javaVersion.majorVersion` 提示兼容性。
- 默认官方源，多线程下载与哈希校验；官方源失败时由用户决定是否使用镜像。
- Mojang 原版、Fabric、Quilt、Forge、NeoForge 安装。
- Modrinth mods、resource packs 与 shaders 搜索和安装。
- 微软 OAuth Authorization Code + PKCE、Xbox Live/XSTS 与 Minecraft Services 登录。
- 正版官方皮肤/披风只读展示。
- 离线 PNG 皮肤/披风校验、本地纹理服务及 authlib-injector 注入。
- 深色、浅色、跟随系统主题和自定义种子色。
- 便携 self-contained 发布与当前用户 MSI。

## 运行要求

- Windows 10 1809+ 或 Windows 11 x64。
- 运行已发布的 self-contained 构建不需要预装 .NET。
- 安装 Minecraft 或运行 Forge/NeoForge 安装器需要 Java。
- 微软登录需要你自己的 Azure 公共客户端 `Application (client) ID`。在设置中填写，并在 Azure 注册以下类型的回调：

```text
http://localhost:53682/callback
```

启动器不会包含客户端密钥，也不会把 access token 写入普通配置或日志。

## 数据目录

便携版优先使用程序旁的 `data/`。如果该目录不可写，则回退到 `%LOCALAPPDATA%\PotatoLauncher`。目录中包含：

```text
settings.json
accounts.json
tokens.bin          Windows DPAPI 加密令牌
cache/
downloads/
logs/
dependencies/
```

已存在的 `.minecraft` 目录只会读取版本元数据。启动和安装会写入用户明确选择的游戏目录。

## 构建

```powershell
.\scripts\build.ps1
```

产物：

```text
artifacts\publish\
artifacts\PotatoLauncher-0.0.1-beta-win-x64-portable.zip
artifacts\PotatoLauncher-0.0.1-beta-win-x64.msi
```

只构建并运行测试：

```powershell
.\scripts\test.ps1
```

在不修改现有实例的前提下做真实启动烟雾测试：

```powershell
$env:POTATO_TEST_MINECRAFT = "D:\Games\.minecraft"
$env:POTATO_LAUNCH_SMOKE = "1"
.\scripts\in-memory-smoke.ps1
```

该脚本只为测试进程创建临时游戏目录，确认 Minecraft 窗口出现后终止自己启动的 Java 进程，不会关闭其他 Java 程序。

生成 MSI 需要 WiX Toolset v4+。

## 测试

测试程序覆盖：

- 版本继承和加载器识别
- Windows/feature rules
- Maven 路径
- Java 版本解析
- 离线 UUID
- 镜像 URL 映射
- 启动参数生成
- 本地签名纹理服务

部分网络与图形界面验收需要可用的网络、真实 Java 和 Azure Client ID：

- 原版与加载器安装
- Microsoft 登录
- Modrinth 下载
- 实际 Minecraft 启动
- 开启自定义外观后的游戏内纹理加载

## 平台限制

微软没有向第三方启动器开放“上传官方皮肤或任意更换官方披风”的稳定公共接口。PotatoLauncher 因此只读取官方外观。自定义上传仅用于离线账号，并通过本地纹理服务和 authlib-injector 在客户端显示；它不会修改 Mojang 账号。

如果 Windows Smart App Control 或组织应用控制策略阻止未签名二进制，请使用可信代码签名证书签署发布产物，或在受控环境中调整策略。项目本身不尝试绕过系统代码完整性策略。

## 第三方组件

`authlib-injector 1.2.8` 从官方 GitHub Release 获取，SHA-256 为：

```text
9c7f4343e6c82034958ffb48c14a2cb0c85928be7283103ce17da00c6d5a7b10
```

许可证与来源说明位于 `src/PotatoLauncher.App/Assets/`。
