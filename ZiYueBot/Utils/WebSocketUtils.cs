using System.Net.WebSockets;
using System.Text;

namespace ZiYueBot.Utils;

public static class WebSocketUtils
{
    private const int BufferSize = 8192;

    /// <summary>
    /// 接收一条完整的文本消息。会按 EndOfMessage 拼接分片，最后统一按 UTF-8 解码，
    /// 避免在多字节字符被拆到两个分片时产生乱码。
    /// </summary>
    public static async Task<string> ReceiveTextMessageAsync(this ClientWebSocket socket,
        CancellationToken cancellationToken = default)
    {
        using MemoryStream stream = new();
        byte[] buffer = new byte[BufferSize];

        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException(
                    $"WebSocket 已关闭：{result.CloseStatus} {result.CloseStatusDescription}".TrimEnd());
            }

            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
