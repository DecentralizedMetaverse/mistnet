using System;

namespace MistNet
{
    public class MistSyncAttribute : Attribute
    {
        public string OnChanged { get; set; }
    }
}