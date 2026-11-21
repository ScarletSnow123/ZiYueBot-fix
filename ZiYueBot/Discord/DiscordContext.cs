using System.Text;
using Discord;
using Discord.WebSocket;
using ZiYueBot.Core;
using ZiYueBot.Utils;

namespace ZiYueBot.Discord;

public class DiscordContext(EventType eventType, string userName, ulong userId, SocketSlashCommand socket) : Context
{
    public override Platform Platform => Platform.Discord;
    public override EventType EventType { get; } = eventType;
    public override string UserName { get; } = userName;
    public override ulong UserId { get; } = userId;
    public override bool HasChannelAdmin => EventType == EventType.GroupMessage &&
                                            (ZiYueBot.Instance.Discord.GetGuild(Socket.GuildId!.Value)
                                                ?.GetUser(UserId)?.GuildPermissions.KickMembers ?? false);

    public SocketSlashCommand Socket { get; } = socket;

    /// <summary>
    /// 由于 Discord 默认回复调用消息，所以第一条需要采取回复，之后的消息必须以普通消息的形式发送。
    /// </summary>
    private bool _firstInvoke = true;

    public override async Task SendMessage(MessageChain messageChain)
    {
        List<FileAttachment> images = [];
        StringBuilder textBuilder = new();

        try
        {
            foreach (IMessageEntity message in messageChain)
            {
                switch (message)
                {
                    case ImageMessageEntity image:
                        images.Add(new FileAttachment(await image.GetStreamAsync(),
                            string.IsNullOrEmpty(image.FileName) ? "image" : image.FileName));
                        break;
                    case TextMessageEntity text:
                        textBuilder.Append(text.Text);
                        break;
                    case PingMessageEntity ping:
                        IUser? user = await Socket.Channel.GetUserAsync(ping.UserId);
                        textBuilder.Append($" {user?.Mention ?? $"<@{ping.UserId}>"} ");
                        break;
                    default:
                        throw new InvalidDataException();
                }
            }

            string textContent = textBuilder.ToString();
            if (images.Count == 0 && string.IsNullOrEmpty(textContent)) return;

            if (_firstInvoke)
            {
                if (images.Count > 0)
                {
                    await Socket.DeferAsync();
                    await Socket.FollowupWithFilesAsync(images, textContent);
                }
                else
                {
                    await Socket.RespondAsync(textContent);
                }

                _firstInvoke = false;
                return;
            }

            if (images.Count > 0)
            {
                await Socket.Channel.SendFilesAsync(images, textContent);
            }
            else
            {
                await Socket.Channel.SendMessageAsync(textContent);
            }
        }
        finally
        {
            // FileAttachment 本身不负责关闭 Stream，发送完成后手动释放，避免文件句柄泄漏。
            foreach (FileAttachment image in images)
            {
                image.Stream.Dispose();
            }
        }
    }

    public override async Task<string> FetchUserName(ulong userId)
    {
        IUser? user = await Socket.Channel.GetUserAsync(userId);
        if (user is not null) return user.GlobalName ?? user.Username;

        DiscordHandler.Logger.Warn($"用户信息获取失败：{userId}");
        return "[未知用户]";
    }
}
