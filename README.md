# MistNet
- [English Documents](README_EN.md)
- [中文文件](README_CN.md)

完全分散型ネットワークライブラリです

**実装例**

https://github.com/DecentralizedMetaverse/mistnet/assets/38463346/cd4a1d95-3422-4b07-b9b6-21f8c63cd1f8



# 特徴
- パーシャルメッシュ型P2Pで接続を行います
- 中央となるサーバーが存在しません
- 通信にはWebRTCを使用します

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
2種類用意しています。どちらも処理内容は同じです。
## Python
- MistNet/main.py

![image](https://github.com/DecentralizedMetaverse/mistnet/assets/38463346/f0c37c6a-aec2-47d7-8b09-99162c56e35a)


## C#
![image](https://github.com/DecentralizedMetaverse/mistnet/assets/38463346/c5b11c4e-4604-455e-8c1d-81f77eee0d3d)

# 初期設定
Scene上に「MistNet」Prefabを置いてください。

Prefabは「Packages/MistNet/Runtime/Prefabs」の中にあります。

![image](https://github.com/DecentralizedMetaverse/mistnet/assets/38463346/e706a9e6-d549-489b-b1cc-1d4a770f6c70)


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

![image](https://github.com/DecentralizedMetaverse/mistnet/assets/38463346/6a52670a-ff8e-4346-9329-32a90db26904)

## 同期GameObjectの設定例

![image](https://github.com/DecentralizedMetaverse/mistnet/assets/38463346/ed16052a-2bae-4dea-bf0f-a7ce367f10b7)


## Instantiate
- 最初からSceneに同期するGameObjectを配置するのではなく、
MistNetを経由してInstantiateする必要があります。

- Addressable Assets に対象となるGameObjectのPrefabを登録し、下記のように実行してください。

![image](https://github.com/DecentralizedMetaverse/mistnet/assets/38463346/8ee873c1-89ff-4774-b762-a9017df5a825)


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
