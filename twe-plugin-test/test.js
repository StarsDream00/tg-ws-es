twe.registerPlugin("Test", "测试用插件", [1, 0, 1]);

let chatId = 0;

twe.listen("ws.chat", (data) => {
    tg.sendMessage(chatId, `<${data.sender}> ${data.text}`);
});
twe.listen("ws.join", (data) => {
    tg.sendMessage(chatId, `${data.sender} 加入了游戏`);
});
twe.listen("ws.left", (data) => {
    tg.sendMessage(chatId, `${data.sender} 退出了游戏`);
});
twe.listen("ws.mobdie", (data) => {
    if (data.mobtype == "minecraft:player") {
        let str = "";
        switch (data.dmname) {
            case "lava":
                str += "被岩浆烧";
                break;
            case "entity_explosion":
                str += `被${data.srctype}炸`;
                break;
        }
        tg.sendMessage(chatId, `${data.mobname} ${str}死了`);
    }
});
