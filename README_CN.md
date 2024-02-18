# MistNet
這是一個完全分散型的網絡庫。

# 特點
- 使用部分網狀P2P進行連接。
- 沒有中央服務器。
- 通信使用WebRTC。

# 導入方法
UPM Package
本軟件使用了MemoryPack和UniTask。

需要事先導入。
- MemoryPack
```
https://github.com/Cysharp/MemoryPack.git?path=src/MemoryPack.Unity/Assets/Plugins/MemoryPack
```
- UniTask
```
https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
```
- MistNet
```
git@github.com:DecentralizedMetaverse/mistnet.git?path=/Assets/MistNet
```

# Signaling Server
提供了兩種類型，處理方式相同。
## Python
- MistNet/main.py

![image](https://github.com/DecentralizedMetaverse/mistnet/assets/38463346/f0c37c6a-aec2-47d7-8b09-99162c56e35a)

## C#
![image](https://github.com/DecentralizedMetaverse/mistnet/assets/38463346/c5b11c4e-4604-455e-8c1d-81f77eee0d3d)

# 初始設置
請將「MistNet」Prefab放置於Scene中。

Prefab位於「Packages/MistNet/Runtime/Prefabs」中。

![image](https://github.com/DecentralizedMetaverse/mistnet/assets/38463346/e706a9e6-d549-489b-b1cc-1d4a770f6c70)

# 連接設置
- SignalingServerAddress
    - 進行Signaling的地方
- MinConnection (目前未使用)
- LimitConnection
    - 限制的人數
        - 連接人數可能會超過此限制，但系統會自動斷開優先級較低的Peer，以保持連接人數在限制範圍內。
- MaxConnection
    - 最大連接人數
```json
{
    "SignalingServerAddress": "ws://localhost:8080/ws",
    "MinConnection": 2,
    "LimitConnection": 20,
    "MaxConnection": 80
}
```

# 同步GameObject的設定方法

## 設定
- 添加「MistSyncObject」組件。
    - 用於RPC呼叫和同步對象的識別。
    
## 座標同步方法
- 添加「MistTransform」組件。

## 動畫同步方法
- 添加「MistAnimator」組件。
- 請按下面的方式設置同步目標參數。

![image](https://github.com/DecentralizedMetaverse/mistnet/assets/38463346/6a52670a-ff8e-4346-9329-32a90db26904)

## 同步GameObject的設定示例
![image](https://github.com/DecentralizedMetaverse/mistnet/assets/38463346/ed16052a-2bae-4dea-bf0f-a7ce367f10b7)

## Instantiate
- 不是將同步的GameObject從一開始就放置在Scene中，而是需要通過MistNet來Instantiate。

- 將目標GameObject的Prefab註冊到Addressable Assets中，然後按照以下方式執行。

![image](https://github.com/DecentralizedMetaverse/mistnet/assets/38463346/8ee873c1-89ff-4774-b762-a9017df5a825)


```csharp
[SerializeField] 
private string prefabAddress = "Assets/Prefab/MistNet/MistPlayerTest.prefab";

MistManager.I.InstantiateAsync(prefabAddress, position, Quaternion.identity).Forget();
```

# RPC
## 註冊方法
在方法前加上`[MistRpc]`。
```csharp
[MistRpc]
private void RPC_○○ () {}
```

## 調用方法
```csharp
[SerializeField] private MistSyncObject syncObject;

// 向所有連接的Peer發送的方法
syncObject.RPCAll(nameof(RPC_○○), args);

// 指定接收者ID並執行的方法
syncObject.RPC(id, nameof(RPC_○○), args);
```