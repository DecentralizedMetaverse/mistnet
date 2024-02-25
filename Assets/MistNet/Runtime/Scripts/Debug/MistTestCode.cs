using System;
using MistNet;
using UnityEngine;

public class MistTestCode : MistBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private MistAnimator mistAnimator;
    private static readonly int Speed = Animator.StringToHash("Speed");
    private float _speed;

    [SerializeField] private bool mao;
    
    [MistSync(OnChanged = nameof(OnChanged))]
    private string _userName { get; set; }
    
    // [MistSync]
    // private int HP { get; set; }

    // [MistSync] private int mp;
    
    private void Start()
    {
        mistAnimator.Animator = animator;
        // MistManager.I.RPCAll(nameof(RPC_Test), "abcaaa");
        // MistManager.I.RPCAll(nameof(RPC_Test2), "abc", 3, 0.1f);
    }

    private void OnChanged()
    {
        MistDebug.Log($"喵喵喵{_userName}");
    }

    private void Update()
    {
        if (!SyncObject.IsOwner)
        {
            MistDebug.Log($"Another: {_userName}");
            return;
        }
        if(mao) _speed += Time.deltaTime;
        else _speed -= Time.deltaTime;
        
        animator.SetFloat(Speed, _speed);
        _userName = $"{_speed}";
    }

    [MistRpc]
    public void RPC_Test(string a)
    {
        MistDebug.Log(a);
    }

    [MistRpc]
    public void RPC_Test2(string a, int b, float c, MessageInfo info)
    {
        MistDebug.Log($"RPC_Test2: {a}, {b}, {c} {info.SourceId} {info.SenderId}");        
    }
}
