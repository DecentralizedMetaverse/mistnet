using System;
using MistNet;
using UnityEngine;

public class MistTestCode : MonoBehaviour
{
    [SerializeField] private MistSyncObject syncObject;
    [SerializeField] private Animator animator;
    [SerializeField] private MistAnimator mistAnimator;
    private static readonly int Speed = Animator.StringToHash("Speed");
    private float _speed;
    private void Start()
    {
        mistAnimator.Animator = animator;
        // MistManager.I.RPCAll(nameof(RPC_Test), "abcaaa");
        // MistManager.I.RPCAll(nameof(RPC_Test2), "abc", 3, 0.1f);
    }

    private void Update()
    {
        if (!syncObject.IsOwner) return;
        _speed += Time.deltaTime;
        animator.SetFloat(Speed, _speed);
    }

    [MistRpc]
    public void RPC_Test(string a)
    {
        Debug.Log(a);
    }

    [MistRpc]
    public void RPC_Test2(string a, int b, float c, MessageInfo info)
    {
        Debug.Log($"RPC_Test2: {a}, {b}, {c} {info.SourceId} {info.SenderId}");        
    }
}
