# MistNet
完全分散型ネットワークライブラリです

# 特徴
- パーシャルメッシュ型P2Pで接続を行います
- 中央となるサーバーが存在しません
- 通信にはWebRTCが使用されています

# 導入方法
UPM Package
本ソフトウェアは、MemoryPackとUniTaskが使用されています。

事前にImportする必要があります。
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
2種類用意しています。どちらも処理方法は同じです。
## Python
- MistNet/main.py

![](image.png)

## C#
![image](https://github.com/DecentralizedMetaverse/mistnet/assets/38463346/c5b11c4e-4604-455e-8c1d-81f77eee0d3d)

# 初期設定
Scene上に「MistNet」Prefabを置いてください。

Prefabは「Packages/MistNet/Runtime/Prefabs」の中にあります。

![Alt text](image-1.png)

# 接続設定
- SignalingServerAddress
    - Signalingをどこで行うか
- MinConnection (現在は未使用)
- LimitConnection
    - 制限する人数
        - 接続人数がこの制限を超えることがありますが、
        接続人数が制限に収まるように、優先度の低いPeerが自動的に切断されます。
- MaxConnection
    - 最大接続人数
```json
{
    "SignalingServerAddress": "ws://localhost:8080/ws",
    "MinConnection": 2,
    "LimitConnection": 20,
    "MaxConnection": 80
}
```

# 同期するGameObjectの設定方法

## 設定
- 「MistSyncObject」を Add Componentします。
    - RPC呼び出しや、同期するObjectの識別に使用されます。
## 座標同期方法
- 「MistTransform」を Add Componentします。

## Animation同期方法
- 「MistAnimator」を Add Componentします。
- 同期対象とするParameterを下記のように設定してください。
![Alt text](image-3.png)

## 同期GameObjectの設定例
![Alt text](image-4.png)

## Instantiate
- 最初からSceneに同期するGameObjectを配置するのではなく、
MistNetを経由してInstantiateする必要があります。

- Addressable Assets に対象となるGameObjectのPrefabを登録し、下記のように実行してください。
![Alt text](image-2.png)


```csharp
[SerializeField] 
private string prefabAddress = "Assets/Prefab/MistNet/MistPlayerTest.prefab";

MistManager.I.InstantiateAsync(prefabAddress, position, Quaternion.identity).Forget();
```

# RPC
## 登録方法
`[MistRpc]`をメソッドの前につけます。
```csharp
[MistRpc]
private void RPC_○○ () {}
```

## 呼び出し方法
```csharp
[SerializeField] private MistSyncObject syncObject;

// 接続しているPeer全員に送信する方法
syncObject.RPCAll(nameof(RPC_○○), args);

// 送信先のIDを指定して実行する方法
syncObject.RPC(id, nameof(RPC_○○), args);
```