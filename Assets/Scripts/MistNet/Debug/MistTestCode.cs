using System;
using System.Collections;
using System.Collections.Generic;
using MistNet;
using UnityEngine;

public class MistTestCode : MonoBehaviour
{
    private void Start()
    {
        MistManager.I.RPCAll(nameof(RPC_Test2), "abc", 3, 0.1f);
    }
    
    [MistRpc]
    public void RPC_Test(string a)
    {
        Debug.Log(a);
    }

    [MistRpc]
    public void RPC_Test2(string a, int b, float c)
    {
        Debug.Log($"RPC_Test2: {a}, {b}, {c}");        
    }
}
