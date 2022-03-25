twe.registerPlugin("Test", "测试用插件", [1, 0, 1]);

let chatId = 0;

let cache = [];
twe.listen("ws.chat", (data) => {
    tg.sendMessage(chatId, `<${data.sender}> ${data.text}`);
});
twe.listen("ws.join", (data) => {
    tg.sendMessage(chatId, `${data.sender} 加入了服务器`);
});
twe.listen("ws.left", (data) => {
    tg.sendMessage(chatId, `${data.sender} 退出了服务器`);
});
twe.listen("ws.mobdie", (data) => {
    if (data.mobtype == "minecraft:player") {
        let str = "";
        switch (data.dmname) {
            case "lava":
                str += "被岩浆烧";
                break;
            case "entity_explosion":
                str += `被 ${data.srctype} 炸`;
                break;
        }
        tg.sendMessage(chatId, `${data.mobname} ${str}死了`);
    }
});
twe.listen("tg.Message", (data) => {
    if (data.Message && data.Message.Chat.Id == chatId) {
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
            return;
        }
        let date = new Date();
        mc.runcmd(
            `say "[${date.getHours()}:${date.getMinutes()}][Telegram]<${
                data.Message.From.LastName
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
