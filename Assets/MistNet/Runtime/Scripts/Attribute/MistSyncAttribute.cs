using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MistNet
{
    public class MistSyncAttribute : Attribute
    {
        public string OnChanged { get; set; }
    }
}