twe.registerPlugin("MessageRelay", "消息转发", [1, 0, 0]);

let chatId = 0; // 填你群组的ChatId

let cache = [];
let players = [];
ws.listen("PlayerChatEvent", (_id, data) => {
    tg.sendMessage(chatId, `<${data.Player}> ${data.Message}`);
});
ws.listen("PlayerJoinEvent", (_id, data) => {
    players.push(String(data.Player));
    tg.sendMessage(
        chatId,
        `${data.Player} 加入了服务器 当前在线${players.length}人`
    );
});
ws.listen("PlayerLeftEvent", (_id, data) => {
    let index = players.indexOf(String(data.Player));
    if (index < 0) {
        return;
    }
    players.splice(index, 1);
    tg.sendMessage(
        chatId,
        `${data.Player} 退出了服务器 当前在线${players.length}人`
    );
});
/*ws.listen("mobdie", (_id, data) => {
    if (data.mobtype == "minecraft:player") {
        let type = "";
        switch (String(data.dmname)) {
            case "entity_attack":
                type = `被 ${
                    String(data.srcname) ? data.srcname : data.srctype
                } 杀死了`;
                break;
            case "projectile":
                type = "被射杀";
                break;
            case "entity_explosion":
                type = `被 ${
                    String(data.srcname) ? data.srcname : data.srctype
                } 炸死了`;
                break;
            case "drowning":
                type = "淹死了";
                break;
            case "fall":
                type = "从高处摔了下来";
                break;
            case "lava":
                type = "试图在熔岩里游泳";
                break;
            case "fire":
                type = "浴火焚身";
                break;
            case "fire_tick":
                type = "被烧死了";
                break;
            case "starve":
                type = "饿死了";
                break;
            case "override":
                type = "死了";
                break;
            case "thorns":
            case "void":
                type = "掉出了这个世界";
                break;
            case "fireworks":
                type = "随着一声巨响消失了";
                break;
            case "magic":
                type = `被 ${
                    String(data.srcname) ? data.srcname : data.srctype
                } 使用的魔法杀死了`;
                break;
            case "anvil":
                type = "被坠落的铁砧压扁了";
                break;
            case "magma":
                type = "发现地面是熔岩";
                break;
            case "contact":
                type = "被戳死了";
                break;
            case "lightning":
                type = "被闪电击中";
                break;
            case "suffocation":
                type = "在墙里窒息而亡";
                break;
            case "block_explosion":
                type = "爆炸了";
                break;
            case "stalactite":
                type = "被坠落的钟乳石刺穿了";
                break;
            case "stalagmite":
                type = "被钉在了石笋上";
                break;
            default:
                type = data.dmname;
                break;
        }
        tg.sendMessage(chatId, `${data.mobname} ${type}`);
    }
});*/
tg.listen("Message", (data) => {
    if (data.Message && data.Message.Chat.Id == chatId) {
        if (data.Message.Type == 1 && data.Message.Text.startsWith("/")) {
            if (
                data.Message.Text == "/list" ||
                data.Message.Text == `/list@${tg.bot.Username}`
            ) {
                cache.push([
                    // mc.runcmd("list"),
                    (result) => {
                        tg.sendMessage(chatId, result);
                    },
                ]);
            }
            return;
        }
        let date = new Date();
        let min = date.getMinutes();
        mc.broadcast(
            `${date.getHours()}:${min < 10 ? 0 : ""}${min} <${
                data.Message.SenderChat
                    ? data.Message.SenderChat.Title
                    : data.Message.From.FirstName
                    ? data.Message.From.FirstName + data.Message.From.LastName
                    : data.Message.From.LastName
            }> ${data.Message.Type == 1 ? data.Message.Text : "§o*胡言乱语*"}`
        );
    }
});
ws.listen("RuncmdResponse", (id, data) => {
    cache.forEach((task) => {
        if (id == task[0]) {
            task[1](data.Message);
            cache.splice(cache.indexOf(task), 1);
        }
    });
});
