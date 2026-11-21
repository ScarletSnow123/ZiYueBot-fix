using Discord;
using Discord.Net.Rest;
using Discord.Net.WebSockets;
using Discord.WebSocket;
using log4net;
using MySql.Data.MySqlClient;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZiYueBot.Core;
using ZiYueBot.Discord;
using ZiYueBot.QQ;
using ZiYueBot.Utils;

namespace ZiYueBot;

public class ZiYueBot
{
    private static readonly ILog Logger = LogManager.GetLogger("主程序");
    public static ZiYueBot Instance { get; private set; } = null!;

    public ClientWebSocket QqEvent { get; private set; }
    public ClientWebSocket QqApi { get; private set; }
    public uint QqUserId { get; private set; }
    public DiscordSocketClient Discord { get; }

    public readonly Config Config;

    /// <summary>
    /// Discord 是否已配置。未配置 DiscordToken 时，机器人只以 QQ 平台运行。
    /// </summary>
    public bool DiscordEnabled => Config.IsDiscordEnabled;

    private readonly SemaphoreSlim _qqReconnectLock = new(1, 1);
    private Task? _eventTask;

    private ZiYueBot()
    {
        Config = Config.Load();

        QqEvent = CreateQqSocket(Config.QqEventAuthenticate);
        QqApi = CreateQqSocket(Config.QqApiAuthenticate);

        // 没启用 Discord 时完全不需要读取代理配置，也不应受 HTTPS_PROXY 等环境变量影响。
        IWebProxy? proxy = DiscordEnabled ? CreateProxy(Config.DiscordProxy) : null;
        Discord = new DiscordSocketClient(new DiscordSocketConfig
        {
            RestClientProvider = DefaultRestClientProvider.Create(proxy is not null),
            // 未配置代理时必须传 null，而不是 new WebProxy(null)，否则会直接抛异常。
            WebSocketProvider = DefaultWebSocketProvider.Create(proxy)
        });
    }

    public static ZiYueBot Create()
    {
        Instance = new ZiYueBot();

        Commands.Initialize();
        if (Instance.DiscordEnabled) DiscordHandler.Initialize();

        return Instance;
    }

    private static ClientWebSocket CreateQqSocket(string authenticate)
    {
        ClientWebSocket socket = new();
        if (!string.IsNullOrWhiteSpace(authenticate))
        {
            socket.Options.SetRequestHeader("Authorization", "Bearer " + authenticate);
        }

        return socket;
    }

    /// <summary>
    /// 读取 Discord 代理配置。优先使用 config.json 的 DiscordProxy，其次使用环境变量。
    /// 未配置代理时返回 null，表示直连。
    /// </summary>
    private static IWebProxy? CreateProxy(string configuredProxy)
    {
        string? address = !string.IsNullOrWhiteSpace(configuredProxy)
            ? configuredProxy
            : Environment.GetEnvironmentVariable("HTTPS_PROXY")
              ?? Environment.GetEnvironmentVariable("HTTP_PROXY")
              ?? Environment.GetEnvironmentVariable("ALL_PROXY");

        if (string.IsNullOrWhiteSpace(address)) return null;

        address = address.Trim();
        if (!address.Contains("://", StringComparison.Ordinal)) address = "http://" + address;

        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri))
        {
            Logger.Warn($"Discord 代理无效，改为直连：{address}");
            return null;
        }

        return new WebProxy(uri);
    }

    private async Task ConnectQqWebSocketAsync()
    {
        await Task.WhenAll(
            ConnectQqSocketAsync(QqEvent, Config.QqEventEndpoint),
            ConnectQqSocketAsync(QqApi, Config.QqApiEndpoint));

        string echo = Guid.NewGuid().ToString("N");
        JsonObject request = new()
        {
            ["action"] = "get_login_info",
            ["echo"] = echo
        };

        await QqApi.SendAsync(Encoding.UTF8.GetBytes(request.ToJsonString()),
            WebSocketMessageType.Text, true, CancellationToken.None);

        string response;
        using (CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30)))
        {
            while (true)
            {
                string payload = await QqApi.ReceiveTextMessageAsync(timeout.Token);
                JsonNode? node = JsonNode.Parse(payload);
                if (node is null) continue;

                string? responseEcho = node["echo"]?.ToString();
                if (responseEcho == echo)
                {
                    response = payload;
                    break;
                }

                // Universal WebSocket 会先推送 lifecycle/meta_event 等事件，跳过它们。
                if (responseEcho is null && node["post_type"] is not null) continue;

                // 兼容不返回 echo 的实现。
                if (responseEcho is null)
                {
                    response = payload;
                    break;
                }
            }
        }

        using JsonDocument qqUserInfoDocument = JsonDocument.Parse(response);
        JsonElement qqUserInfo = qqUserInfoDocument.RootElement;

        if (!qqUserInfo.TryGetProperty("data", out JsonElement data) ||
            !data.TryGetProperty("user_id", out JsonElement userIdElement))
        {
            throw new InvalidOperationException($"QQ 登录信息格式错误：{response}");
        }

        QqUserId = userIdElement.GetUInt32();
        string nickname = data.TryGetProperty("nickname", out JsonElement nicknameElement)
            ? nicknameElement.GetString() ?? string.Empty
            : string.Empty;

        Logger.Info($"QQ 连接成功：{nickname} ({QqUserId})");
    }

    private static async Task ConnectQqSocketAsync(ClientWebSocket socket, string endpoint)
    {
        if (socket.State == WebSocketState.Open) return;
        await socket.ConnectAsync(new Uri(endpoint), CancellationToken.None);
    }

    private async Task ConnectDiscordAsync()
    {
        if (!DiscordEnabled)
        {
            Logger.Info("Discord 未配置，跳过。");
            return;
        }

        try
        {
            await Discord.LoginAsync(TokenType.Bot, Config.DiscordToken);
            await Discord.StartAsync();
            Logger.Info($"Discord 登录成功：{Discord.CurrentUser?.GlobalName ?? Discord.CurrentUser?.Username ?? "未知用户"}");
        }
        catch (Exception e)
        {
            // Discord 是可选的。连接失败不应阻断 QQ 机器人启动。
            Logger.Error("Discord 连接失败，继续运行 QQ。", e);
        }
    }

    private async Task InitializeDatabaseAsync()
    {
        try
        {
            string cmdText = await File.ReadAllTextAsync("resources/initialize.sql");

            await using MySqlConnection connection = ConnectDatabase();
            await using MySqlCommand command = new MySqlCommand(cmdText, connection);

            await command.ExecuteNonQueryAsync();

            Logger.Info("数据库初始化成功");
        }
        catch (Exception e)
        {
            Logger.Error("数据库初始化出错", e);
        }
    }

    public MySqlConnection ConnectDatabase()
    {
        MySqlConnection connection = new MySqlConnection(
            $"""
             Server={Config.DatabaseSource};
             Port={Config.DatabasePort};
             Database={Config.DatabaseName};
             User={Config.DatabaseUser};
             Password={Config.DatabasePassword};
             Charset=utf8mb4;
             AllowUserVariables=True;
             Pooling=true;
             """
        );
        connection.Open();
        return connection;
    }

    /// <summary>
    /// 重新建立与 QQ 的连接。会先结束旧的事件循环并重建两个 WebSocket，避免在已打开的
    /// ClientWebSocket 上再次调用 ConnectAsync 导致 InvalidOperationException。
    /// </summary>
    public async Task ReconnectQqWebSocketAsync()
    {
        await _qqReconnectLock.WaitAsync();
        // 暂停所有 API 请求，避免旧连接上的请求在新连接登录期间串到新连接。
        await QqContext.ApiLock.WaitAsync();
        try
        {
            Task? oldEventTask = _eventTask;
            _eventTask = null;

            if (QqEvent.State != WebSocketState.Closed) QqEvent.Abort();
            if (QqApi.State != WebSocketState.Closed) QqApi.Abort();

            if (oldEventTask is not null && !oldEventTask.IsCompleted)
            {
                try
                {
                    await oldEventTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception e)
                {
                    Logger.Debug("旧 QQ 事件循环退出异常", e);
                }
            }

            QqEvent.Dispose();
            QqApi.Dispose();

            QqEvent = CreateQqSocket(Config.QqEventAuthenticate);
            QqApi = CreateQqSocket(Config.QqApiAuthenticate);

            await ConnectQqWebSocketAsync();
            _eventTask = QqEvents.Initialize();
        }
        finally
        {
            QqContext.ApiLock.Release();
            _qqReconnectLock.Release();
        }
    }

    public async Task StartAsync()
    {
        await InitializeDatabaseAsync();

        // Discord 是可选平台，不能让 Discord 的连接耗时阻塞 QQ 的事件循环。
        Task discordTask = ConnectDiscordAsync();
        await ConnectQqWebSocketAsync();
        _eventTask = QqEvents.Initialize();
        await discordTask;
    }

    public Task WaitAsync()
    {
        return _eventTask ?? Task.CompletedTask;
    }
}
