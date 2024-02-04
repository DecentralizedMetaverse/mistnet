using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MistNet
{
    public class MistRpcAttribute : Attribute
    {
        public MistRpcAttribute()
        {
            
        }
        
        public MistRpcAttribute(string name)
        {
            this.Value = name;   
        }
        
        public string Value { get; set; }
    }
}