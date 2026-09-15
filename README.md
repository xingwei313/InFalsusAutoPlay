# InFalsusAutoPlay

In Falsus的AutoPlay Mod，使用[MelonLoader](https://github.com/LavaGang/MelonLoader)加载

---

## 构建

需要 [.NET SDK](https://dotnet.microsoft.com/download)（目标框架 `net6.0`）

```cmd
build.bat          :: Release构建
build.bat Debug    :: Debug构建
```

Release构建产物位于 `bin/Release/InFalsusAutoPlay.dll`。

Debug构建产物位于 `bin/Debug/InFalsusAutoPlay.dll`。

---

## 安装

1. 安装 [MelonLoader](https://github.com/LavaGang/MelonLoader)
2. 获取 `InFalsusAutoPlay.dll`
3. 放进游戏目录下的 `Mods` 文件夹
4. 启动游戏

---

## 使用

非标题界面下，**游戏和UI**设置页里的**卡牌制成**>**辅助模式**会变成**In Falsus AutoPlay**>**AUTO**以作为 `AUTO` 的游戏内开关

默认情况下**AUTO**不保留成绩

配置文件位于`GAME_PATH/UserData/InFalsusAutoPlay.cfg`

需要查看完整日志时请使用**Debug构建**

---

## 致谢

- [MelonLoader](https://github.com/LavaGang/MelonLoader) —— mod 加载器

## 真-致谢

- deepseek v4.1 flash —— 重构屎山，查bug，部分逆向工作，build流程