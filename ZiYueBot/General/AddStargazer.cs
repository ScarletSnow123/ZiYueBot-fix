using ZiYueBot.Core;
using ZiYueBot.Utils;

namespace ZiYueBot.General;

public class AddStargazer : Command
{
    public override string Id => "添加星标";
    public override string Name => "添加星标";
    public override string Summary => "添加云瓶星标";

    public override string Description => """
                                          /添加星标 [id]
                                          对云瓶进行星标操作，将其加入用户的星标列表。
                                          频率限制：每次调用间隔 1 分钟。
                                          在线文档：https://docs.ziyuebot.cn/general/stargazer/add
                                          """;

    public override async Task Invoke(Context context, MessageChain arg)
    {
        if (!this.TryPassRateLimit(context))
        {
            await context.SendMessage("频率已达限制（1 分钟 1 条）");
            return;
        }

        if (arg.Count != 1 || arg[0] is not TextMessageEntity text || !int.TryParse(text.Text, out int bottleId))
        {
            await context.SendMessage("参数无效。使用“/help 添加星标”查看命令用法。");
            return;
        }

        string result = Stargazers.AddStargazer(context.UserId, context.UserName, bottleId, false);
        if (!string.IsNullOrEmpty(result)) await context.SendMessage(result);
    }

    public override TimeSpan GetRateLimit(Context context)
    {
        return TimeSpan.FromMinutes(1);
    }
}
