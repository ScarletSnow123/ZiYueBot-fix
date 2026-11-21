using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZiYueBot.Core;

/// <summary>
/// QQ、Discord 及 MySQL 的相关配置。
/// 子悦机器初始化时会从根目录的 config.json 读取这些敏感信息。请勿公开 config.json。
/// 只有 QQ 是必填的，Discord 可以留空；未配置 DiscordToken 时机器人会以 QQ 单平台运行。
/// </summary>
[Serializable]
public sealed class Config
{
    public string DiscordProxy { get; set; } = "";
    public string DiscordToken { get; set; } = "";
    public string DatabaseSource { get; set; } = "";
    public int DatabasePort { get; set; } = 3306;
    public string DatabaseName { get; set; } = "";
    public string DatabaseUser { get; set; } = "";
    public string DatabasePassword { get; set; } = "";
    public string ChatAgentEndpoint { get; set; } = "";
    public string BailianApiEndpoint { get; set; } = "";
    public string BailianApiKey { get; set; } = "";
    public string QqEventEndpoint { get; set; } = "";
    public string QqEventAuthenticate { get; set; } = "";
    public string QqApiEndpoint { get; set; } = "";
    public string QqApiAuthenticate { get; set; } = "";
    public string AssetsEndpoint { get; set; } = "";
    public string AssetsUploadRegion { get; set; } = "";
    public string AssetsUploadBucket { get; set; } = "";
    public string AssetsUploadSecretId { get; set; } = "";
    public string AssetsUploadSecretKey { get; set; } = "";

    /// <summary>
    /// 是否启用了 Discord。DiscordToken 为空即视为未启用，不需要再配置代理。
    /// </summary>
    [JsonIgnore]
    public bool IsDiscordEnabled => !string.IsNullOrWhiteSpace(DiscordToken);

    /// <summary>
    /// 从 config.json 读取配置。缺少必填项时给出明确的错误信息，而不是在连接阶段抛出难以理解的异常。
    /// </summary>
    public static Config Load(string path = "config.json")
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"找不到配置文件 {fullPath}。请复制 config.example.json 为 config.json 后再填写。", fullPath);
        }

        Config? config;
        try
        {
            using FileStream stream = File.OpenRead(fullPath);
            config = JsonSerializer.Deserialize<Config>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"配置文件 {fullPath} 不是有效的 JSON：{e.Message}", e);
        }

        if (config is null)
        {
            throw new InvalidOperationException($"配置文件 {fullPath} 的内容为空。");
        }

        config.Normalize();
        config.Validate();
        return config;
    }

    private void Normalize()
    {
        DiscordProxy = DiscordProxy?.Trim() ?? "";
        DiscordToken = DiscordToken?.Trim() ?? "";
        DatabaseSource = DatabaseSource?.Trim() ?? "";
        DatabaseName = DatabaseName?.Trim() ?? "";
        DatabaseUser = DatabaseUser?.Trim() ?? "";
        DatabasePassword ??= "";
        ChatAgentEndpoint = ChatAgentEndpoint?.Trim() ?? "";
        BailianApiEndpoint = BailianApiEndpoint?.Trim() ?? "";
        BailianApiKey = BailianApiKey?.Trim() ?? "";
        QqEventEndpoint = QqEventEndpoint?.Trim() ?? "";
        QqEventAuthenticate ??= "";
        QqApiEndpoint = QqApiEndpoint?.Trim() ?? "";
        QqApiAuthenticate ??= "";
        AssetsEndpoint = AssetsEndpoint?.Trim().TrimEnd('/') ?? "";
        AssetsUploadRegion = AssetsUploadRegion?.Trim() ?? "";
        AssetsUploadBucket = AssetsUploadBucket?.Trim() ?? "";
        AssetsUploadSecretId = AssetsUploadSecretId?.Trim() ?? "";
        AssetsUploadSecretKey = AssetsUploadSecretKey?.Trim() ?? "";
    }

    private void Validate()
    {
        List<string> missing = [];
        if (string.IsNullOrWhiteSpace(QqEventEndpoint)) missing.Add("QqEventEndpoint");
        if (string.IsNullOrWhiteSpace(QqApiEndpoint)) missing.Add("QqApiEndpoint");
        if (string.IsNullOrWhiteSpace(DatabaseSource)) missing.Add("DatabaseSource");
        if (string.IsNullOrWhiteSpace(DatabaseName)) missing.Add("DatabaseName");
        if (string.IsNullOrWhiteSpace(DatabaseUser)) missing.Add("DatabaseUser");
        if (DatabasePort is <= 0 or > 65535) missing.Add("DatabasePort（应为 1-65535）");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"config.json 缺少必填项或填写有误：{string.Join("、", missing)}。QQ 与数据库是必填的，Discord 可以留空。");
        }

        ValidateWebSocketEndpoint(QqEventEndpoint, "QqEventEndpoint");
        ValidateWebSocketEndpoint(QqApiEndpoint, "QqApiEndpoint");
    }

    private static void ValidateWebSocketEndpoint(string endpoint, string name)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeWs && uri.Scheme != Uri.UriSchemeWss))
        {
            throw new InvalidOperationException($"config.json 中的 {name} 不是有效的 WebSocket 地址：{endpoint}");
        }
    }
}
