using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZiYueBot.Core;
using ZiYueBot.Utils;

namespace ZiYueBot.QQ;

public class QqContext(EventType eventType, string userName, ulong userId, uint sourceUin) : Context
{
    public override Platform Platform => Platform.QQ;
    public override EventType EventType { get; } = eventType;
    public override string UserName { get; } = userName;
    public override ulong UserId { get; } = userId;

    public override bool HasChannelAdmin
    {
        get
        {
            if (EventType == EventType.DirectMessage) return false;
            JsonNode response = SendApiRequest(
                new JsonObject
                {
                    ["action"] = "get_group_member_info",
                    ["params"] = new JsonObject
                    {
                        ["group_id"] = SourceUni,
                        ["user_id"] = UserId,
                        ["no_cache"] = true
                    }
                }).Result;
            string role = response["data"]!["role"]!.GetValue<string>();
            return role is "owner" or "admin";
        }
    }

    /// <summary>
    /// 消息来源。根据 EventType 而变化，可能为群聊 ID，或用户 ID。
    /// </summary>
    public uint SourceUni { get; } = sourceUin;

    public override async Task SendMessage(MessageChain messageChain)
    {
        IEnumerable<JsonObject> array = messageChain.Select(message =>
        {
            return message.Type switch
            {
                MessageEntityType.Text => new JsonObject
                {
                    ["type"] = "text", ["data"] = new JsonObject { ["text"] = ((TextMessageEntity)message).Text }
                },
                MessageEntityType.Image => new JsonObject
                {
                    ["type"] = "image", ["data"] = new JsonObject { ["file"] = ((ImageMessageEntity)message).Path }
                },
                MessageEntityType.Ping => new JsonObject
                {
                    ["type"] = "at", ["data"] = new JsonObject { ["qq"] = ((PingMessageEntity)message).UserId }
                },
                _ => throw new InvalidDataException("未知消息实体类型")
            };
        });
        await Task.Delay(Random.Shared.Next(500, 1500));
        await SendApiRequest(new JsonObject
        {
            ["action"] = EventType == EventType.DirectMessage ? "send_private_msg" : "send_group_msg",
            ["params"] = new JsonObject
            {
                [EventType == EventType.DirectMessage ? "user_id" : "group_id"] = SourceUni,
                ["message"] = new JsonArray(array.ToArray<JsonNode?>())
            }
        });
    }

    public override async Task<string> FetchUserName(ulong userId)
    {
        if (userId == 0) return "全体成员";
        try
        {
            JsonNode response = await SendApiRequest(
                new JsonObject
                {
                    ["action"] = "get_stranger_info",
                    ["params"] = new JsonObject
                    {
                        ["user_id"] = userId,
                        ["no_cache"] = false
                    }
                });
            return response["data"]!["nickname"]!.GetValue<string>();
        }
        catch (Exception e)
        {
            QqEvents.Logger.Warn($"用户信息获取失败：{userId}", e);
            return "[未知用户]";
        }
    }

    public static MessageChain FetchMessageContent(string messageId, out ulong authorUserId)
    {
        MessageChain message;
        try
        {
            JsonNode response = SendApiRequest(new JsonObject
            {
                ["action"] = "get_msg",
                ["params"] = new JsonObject
                {
                    ["message_id"] = messageId
                }
            }).GetAwaiter().GetResult();
            message = Parser.ParseMessage(response["data"]!["message"]!, out _);
            authorUserId = response["data"]!["user_id"]!.GetValue<ulong>();
        }
        catch (Exception e)
        {
            QqEvents.Logger.Warn("获取引用内容出错：", e);
            message = [new TextMessageEntity("[未知引用消息]")];
            authorUserId = 0;
        }

        return message;
    }

    // OneBot 的 API WebSocket 在同一时间只能处理一个请求。并发调用会导致响应串线
    // （A 请求收到 B 的结果），因此这里串行化所有 API 调用。
    private static readonly SemaphoreSlim ApiRequestLock = new(1, 1);

    internal static SemaphoreSlim ApiLock => ApiRequestLock;

    internal static async Task<JsonNode> SendApiRequest(JsonObject json)
    {
        await ApiRequestLock.WaitAsync();
        try
        {
            Exception? lastError = null;
            for (int i = 0; i < 3; i++)
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
                try
                {
                    byte[] payload = Encoding.UTF8.GetBytes(json.ToJsonString(new JsonSerializerOptions
                        { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

                    await ZiYueBot.Instance.QqApi.SendAsync(payload, WebSocketMessageType.Text, true,
                        timeout.Token);

                    string response = await ZiYueBot.Instance.QqApi.ReceiveTextMessageAsync(timeout.Token);
                    JsonNode? node = JsonNode.Parse(response);
                    if (node is not null) return node;

                    lastError = new InvalidDataException("QQ API 空响应");
                }
                catch (Exception e) when (e is WebSocketException or IOException or JsonException
                                              or InvalidOperationException or OperationCanceledException)
                {
                    lastError = e;
                }

                if (i < 2) await Task.Delay(200);
            }

            QqEvents.Logger.Error($"API 请求失败：{json}", lastError);
            throw new HttpRequestException("QQ API 请求失败", lastError);
        }
        finally
        {
            ApiRequestLock.Release();
        }
    }
}