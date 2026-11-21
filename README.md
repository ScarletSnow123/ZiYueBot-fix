# 子悦机器

**子悦机器 (ZiYue Bot)** 是一个由 [子悦解说](https://github.com/ZiYueCommentary) 开发的，用 C# 编写的 QQ 和 Discord 机器人。

* **子悦机器完全开源。** 你可以自行搭建子悦机器，也可以对子悦机器的开发做出贡献。
* **子悦机器不是框架。** 子悦机器不支持在不改动源码的情况下添加功能。
* **子悦机器独立自主。** 子悦机器不依赖任何机器人框架。
* **子悦机器高度定制。** 安装子悦机器时只需配置 QQ 和 MySQL；Discord 为可选平台，不配置 DiscordToken 时会以 QQ 单平台运行。Discord 代理同样可选，不需要代理时直连即可。

## 配置

在程序工作目录准备 `config.json`。QQ 和 MySQL 为必填项，Discord 相关字段可以留空：

```json
{
  "QqEventEndpoint": "ws://127.0.0.1:3001",
  "QqEventAuthenticate": "",
  "QqApiEndpoint": "ws://127.0.0.1:3002",
  "QqApiAuthenticate": "",
  "DatabaseSource": "127.0.0.1",
  "DatabasePort": 3306,
  "DatabaseName": "ziyuebot",
  "DatabaseUser": "ziyuebot",
  "DatabasePassword": "",
  "DiscordToken": "",
  "DiscordProxy": ""
}
```

`DiscordToken` 留空时不会登录 Discord，如果需要 Discord，填入 Token；代理可填 `http://127.0.0.1:7890` 这样的地址，留空则依次读取 `HTTPS_PROXY`、`HTTP_PROXY`、`ALL_PROXY` 环境变量

### 连接 SnowLuma

SnowLuma 通过 OneBot v11 的 WebSocket 服务端暴露 QQ：
- 默认 HTTP API 端口：`3000`
- 默认 WebSocket 端口：`3001`

子悦机器使用两个独立的 WebSocket 连接，因此建议在 SnowLuma 的 WebUI → OneBot → WebSocket 服务端中开两个服务：

| 用途 | 角色 | 端口 | 对应配置 |
| --- | --- | --- | --- |
| 接收消息事件 | `Event` | `3001` | `QqEventEndpoint` / `QqEventAuthenticate` |
| 调用 OneBot API | `Api` | `3002` | `QqApiEndpoint` / `QqApiAuthenticate` |

两个服务的 `messageFormat` 都设为 `array`，并分别设置 `accessToken`，再把对应 token 填入 `config.json`。

如果 SnowLuma 只提供一个 `Universal` 服务（通常就是 `3001`），也可以把两个 endpoint 都填 `ws://127.0.0.1:3001`；但推荐使用 `Event` + `Api` 分离，避免事件和 API 响应混在同一个连接上。

```text
ws://127.0.0.1:3001  -> Event
ws://127.0.0.1:3002  -> Api
```

不要把 `3001` / `3002` 直接暴露到公网；默认只监听 `127.0.0.1`。

## 在线文档

https://docs.ziyuebot.cn/

## 许可证

子悦机器采用 AGPL-3.0 许可证。
