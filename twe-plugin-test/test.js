twe.registerPlugin("Test", "测试用插件", [1, 0, 1]);

let chatId = 0; // 填你群组的ChatId

let cache = [];
let players = [];
twe.listen("ws.chat", (data) => {
    tg.sendMessage(chatId, `<${data.sender}> ${data.text}`);
});
twe.listen("ws.join", (data) => {
    players.push(String(data.sender));
    tg.sendMessage(
        chatId,
        `${data.sender} 加入了服务器 当前在线${players.length}人`
    );
});
twe.listen("ws.left", (data) => {
    let index = players.indexOf(String(data.sender));
    if (index > -1) {
        players.splice(index, 1);
        tg.sendMessage(
            chatId,
            `${data.sender} 退出了服务器 当前在线${players.length}人`
        );
    }
});
twe.listen("ws.mobdie", (data) => {
    if (data.mobtype == "minecraft:player") {
        let type = "";
        switch (String(data.dmname)) {
            case "entity_attack":
                type = `被${
                    String(data.srcname) ? data.srcname : data.srctype
                }杀死了`;
                break;
            case "projectile":
                type = "被射杀";
                break;
            case "entity_explosion":
                type = `被${
                    String(data.srcname) ? data.srcname : data.srctype
                }炸死了`;
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
                type = `被${
                    String(data.srcname) ? data.srcname : data.srctype
                }使用的魔法杀死了`;
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
            default:
                type = data.dmname;
                break;
        }
        tg.sendMessage(chatId, `${data.mobname} ${type}`);
    }
});
twe.listen("tg.Message", (data) => {
    if (data.Message && data.Message.Chat.Id == chatId) {
        if (data.Message.Text.startsWith("/")) {
            if (
                data.Message.Text == "/list" ||
                data.Message.Text == `/list@${tg.bot.Username}`
            ) {
                cache.push([
                    mc.runcmd("list"),
                    (result) => {
                        tg.sendMessage(chatId, result);
                    },
                ]);
            }
            return;
        }
        let date = new Date();
        mc.runcmd(
            `say "[${date.getHours()}:${date.getMinutes()}][Telegram]<${
                data.Message.SenderChat
                    ? data.Message.SenderChat.Title
                    : data.Message.From.FirstName
                    ? data.Message.From.FirstName + data.Message.From.LastName
                    : data.Message.From.LastName
            }> ${data.Message.Text}"`
        );
    }
});
twe.listen("ws.runcmdfeedback", (data) => {
    cache.forEach((task) => {
        if (data.id == task[0]) {
            task[1](data.result);
            cache.splice(cache.indexOf(task), 1);
        }
    });
});
