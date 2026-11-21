using System.Text.RegularExpressions;
using ZiYueBot.Utils;

namespace ZiYueBot.Core;

public class MessageChain : List<IMessageEntity>
{
    public static MessageChain operator +(string str, MessageChain chain)
    {
        return [new TextMessageEntity(str), ..chain];
    }

    public static MessageChain operator +(MessageChain chain, string str)
    {
        return [..chain, new TextMessageEntity(str)];
    }

    public bool IsEmpty() => Count == 0;
    public string Flatten() => ToString().Replace("\n", "\\n").Replace("\r", "\\r");
    public override string ToString() => ToString(null);

    public bool IsLiteralString()
    {
        return Find(entity =>
        {
            return entity switch
            {
                ImageMessageEntity => true,
                TextMessageEntity text => TextMessageEntity.DiscordEmotionRegex().IsMatch(text.Text),
                _ => false
            };
        }) is null;
    }

    public string ToString(Context? context)
    {
        IEnumerable<string> raw = this.Select(message => message.ToString(context));
        return string.Join(null, raw);
    }

    /// <summary>
    /// 将数据库里的字符串变成可被发送的消息链。所有处理的字符串都只能包含本地图片和远程图片两种数据。
    /// \u2408和\u2409包裹一个本地图片的路径，\uE000和\uE001包裹一个远程图片的路径。
    /// </summary>
    public static MessageChain FromDatabase(string message)
    {
        if (!message.Contains('\u2408') && !message.Contains('\uE000')) return [new TextMessageEntity(message)];

        MessageChain chain = [];
        int pos = 0;
        while (pos < message.Length)
        {
            int localStart = message.IndexOf('\u2408', pos);
            int remoteStart = message.IndexOf('\uE000', pos);
            int start = localStart switch
            {
                < 0 => remoteStart,
                _ when remoteStart < 0 => localStart,
                _ => Math.Min(localStart, remoteStart)
            };

            if (start < 0)
            {
                AddText(chain, message[pos..]);
                break;
            }

            if (start > pos) AddText(chain, message[pos..start]);

            bool isLocal = message[start] == '\u2408';
            char endMarker = isLocal ? '\u2409' : '\uE001';
            int end = message.IndexOf(endMarker, start + 1);
            if (end < 0)
            {
                // 数据不完整时保留剩余内容，避免把整条消息吞掉。
                AddText(chain, message[start..]);
                break;
            }

            string path = message[(start + 1)..end];
            if (isLocal)
            {
                chain.Add(ImageMessageEntity.FromPath(path));
            }
            else
            {
                chain.Add(new ImageMessageEntity(path, GetFileName(path)));
            }

            pos = end + 1;
        }

        return chain;
    }

    private static void AddText(MessageChain chain, string text)
    {
        if (text.Length > 0) chain.Add(new TextMessageEntity(text));
    }

    private static string GetFileName(string path)
    {
        int separator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        string fileName = separator < 0 ? path : path[(separator + 1)..];
        return string.IsNullOrEmpty(fileName) ? "image" : fileName;
    }
}

/// <summary>
/// 消息实体类型。
/// </summary>
public enum MessageEntityType
{
    Text,
    Image,
    Ping
}

/// <summary>
/// 消息实体。一条消息由多个消息实体组成，每个实体代表不同的文字、图片、提及。
/// </summary>
public interface IMessageEntity
{
    public MessageEntityType Type { get; }

    public string ToString(Context? context);
}

public partial record TextMessageEntity(string Text) : IMessageEntity
{
    public MessageEntityType Type => MessageEntityType.Text;

    public string ToString(Context? context)
    {
        return Text.FormatDiscordPing(context);
    }

    [GeneratedRegex("<:.*:\\d+>")]
    public static partial Regex DiscordEmotionRegex();
}

/// <summary>
/// 图片消息实体。
/// </summary>
/// <param name="Path">图片的路径，可能为本地路径、远程路径，或一个 base64 编码的二进制图片。</param>
public record ImageMessageEntity(string Path, string FileName) : IMessageEntity
{
    public MessageEntityType Type => MessageEntityType.Image;

    public string ToString(Context? context)
    {
        return "{image=" + Path + "}";
    }

    public static ImageMessageEntity FromPath(string path)
    {
        byte[] imageBytes = File.ReadAllBytes(path);
        string base64String = Convert.ToBase64String(imageBytes);
        int separator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        string filename = separator < 0 ? path : path[(separator + 1)..];
        return new ImageMessageEntity($"base64://{base64String}", string.IsNullOrEmpty(filename) ? "image" : filename);
    }
}

public record PingMessageEntity(ulong UserId) : IMessageEntity
{
    public MessageEntityType Type => MessageEntityType.Ping;

    public string ToString(Context? context)
    {
        if (context is null) return "{ping=" + UserId + "}";
        return $"@{context.FetchUserName(UserId).GetAwaiter().GetResult()}";
    }
}